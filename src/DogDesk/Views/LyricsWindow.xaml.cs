using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Lyricify.Lyrics.Models;
using DogDesk.Interop;
using DogDesk.Models;
using DogDesk.Services;

namespace DogDesk.Views;

/// <summary>
/// 桌面歌词窗口：吸附在任务栏上边缘上方、水平居中，
/// 显示当前行（高亮）与下一行（暗色），按播放位置定时同步。
/// 逐字歌词（音节级）按设置启用：已唱音节用主题色高亮，未唱保持白色；
/// 逐行歌词降级为整行高亮。字号/透明度/主题色均可配置。
/// </summary>
public partial class LyricsWindow : Window
{
    private const int WinW = 820;
    private const int WinH = 112;
    private const long WS_EX_TRANSPARENT = 0x00000020;

    private readonly IMediaSession _session;
    private readonly LyricsService _lyrics;
    private readonly AppSettings _settings;
    private DispatcherTimer? _timer;
    private int _currentIndex = -1;
    private bool _styled;

    // 逐字歌词渲染状态
    private FullSyllableLineInfo? _sylLine;
    private Run[]? _sylRuns;
    private SolidColorBrush? _accentBrush;
    private SolidColorBrush _plainBrush = new(Colors.White);
    private readonly TranslateTransform _vibrate = new();

    /// <summary>桌面歌词行推进时触发（当前行/下一行纯文本），供媒体条双行歌词订阅。</summary>
    public event Action<string, string>? LineChanged;

    public LyricsWindow(IMediaSession session, LyricsService lyrics, AppSettings settings)
    {
        InitializeComponent();
        _session = session;
        _lyrics = lyrics;
        _settings = settings;

        ApplyStyleSettings();
    }

    /// <summary>应用字号/透明度/主题色设置（窗口已显示后也适用）。</summary>
    public void ApplyStyleSettings()
    {
        CurrentLine.FontSize = Math.Clamp(_settings.LyricsFontSize, 14, 34);
        NextLine.FontSize = Math.Max(12, CurrentLine.FontSize * 0.62);
        Opacity = Math.Clamp(_settings.LyricsOpacity, 0.3, 1.0);

        // F7：双行开关
        NextLine.Visibility = _settings.LyricsDualLine ? Visibility.Visible : Visibility.Collapsed;

        // F7：节拍振动的 RenderTransform
        CurrentLine.RenderTransform = _vibrate;

        // F7：竖排（整卡旋转 90°，最小实现）
        LyricsCard.LayoutTransform = _settings.LyricsVertical
            ? new RotateTransform(90)
            : null;

        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_settings.AccentColor);
            _accentBrush = new SolidColorBrush(c);
        }
        catch
        {
            _accentBrush = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
        }
        _accentBrush.Freeze();
    }

    /// <summary>F7：节拍命中时的当前行快速振荡（±2-4px）。</summary>
    public void TriggerBeat()
    {
        if (!_settings.LyricsBeatVibrate) return;
        try
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = -3.5, To = 0, Duration = TimeSpan.FromMilliseconds(90),
            };
            _vibrate.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        catch { }
    }

    public void Start()
    {
        Relocate();
        Show();
        EnsureStyle();

        // HighFpsMode：歌词 tick 60fps，否则 30fps
        int intervalMs = _settings.HighFpsMode ? 16 : 33;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        Hide();
        _currentIndex = -1;
        _sylLine = null;
        _sylRuns = null;
    }

    private void EnsureStyle()
    {
        if (_styled) return;
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT));
        _styled = true;
    }

    private void Relocate()
    {
        int x, y;
        if (TaskbarService.GetTaskbarRect() is Rect tb && tb.Width >= tb.Height)
        {
            x = (int)tb.Left + ((int)tb.Width - WinW) / 2;
            int screenTop = (int)SystemParameters.VirtualScreenTop;
            if (tb.Top <= screenTop + 2)
                y = (int)tb.Bottom + 8;    // 顶部任务栏：放任务栏下方
            else
                y = (int)tb.Top - WinH - 8; // 底部任务栏：放任务栏上方
        }
        else
        {
            Rect wa = SystemParameters.WorkArea;
            x = (int)(wa.Left + (wa.Width - WinW) / 2);
            y = (int)(wa.Bottom - WinH - 8);
        }

        // F7：自定义位置偏移（LyricsOffsetPercent 0-100，50 为默认位，±120px）
        int offsetPx = (int)((Math.Clamp(_settings.LyricsOffsetPercent, 0, 100) - 50) / 50.0 * 120);
        y += offsetPx;

        int minX = (int)SystemParameters.VirtualScreenLeft;
        int maxX = (int)(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - WinW);
        x = Math.Clamp(x, minX, maxX);

        Left = x;
        Top = y;
        Width = WinW;
        Height = WinH;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        // TryFetchAsync 内部按曲目去重，切歌才会真正重新请求
        await _lyrics.TryFetchAsync(_session.Title, _session.Artist, _session.DurationSec);

        LyricsData? data = _lyrics.Data;
        if (data?.Lines is null || data.Lines.Count == 0)
        {
            if (_currentIndex != -1)
            {
                _currentIndex = -1;
                _sylLine = null;
                _sylRuns = null;
                CurrentLine.Text = "♪";
                NextLine.Text = "";
                LineChanged?.Invoke("", "");
            }
            return;
        }

        double tMs = _session.PositionSec * 1000.0;
        int idx = FindLineIndex(data.Lines, tMs);

        if (idx != _currentIndex)
        {
            _currentIndex = idx;
            RenderLine(data.Lines, idx);
        }
        else if (_sylLine is not null && _sylRuns is not null && _settings.LyricsKaraoke)
        {
            UpdateKaraoke(tMs);
        }
    }

    private void RenderLine(System.Collections.Generic.List<ILineInfo> lines, int idx)
    {
        _sylLine = null;
        _sylRuns = null;

        if (idx < 0)
        {
            CurrentLine.Text = "♪";
            NextLine.Text = "";
            LineChanged?.Invoke("", "");
            return;
        }

        var line = lines[idx];

        if (_settings.LyricsKaraoke && line is FullSyllableLineInfo full
            && full.Syllables is { Count: > 0 })
        {
            _sylLine = full;
            CurrentLine.Inlines.Clear();
            _sylRuns = new Run[full.Syllables.Count];
            for (int i = 0; i < full.Syllables.Count; i++)
            {
                var run = new Run { Text = full.Syllables[i].Text };
                _sylRuns[i] = run;
                CurrentLine.Inlines.Add(run);
            }
            UpdateKaraoke(_session.PositionSec * 1000.0);
        }
        else
        {
            CurrentLine.Text = Clean(line.Text);
        }

        NextLine.Text = idx + 1 < lines.Count ? Clean(lines[idx + 1].Text) : "";

        // 推送纯文本双行给媒体条（无下一行/空行时给空串，回退封面+标题）
        string nextPlain = idx + 1 < lines.Count ? CleanOrEmpty(lines[idx + 1].Text) : "";
        LineChanged?.Invoke(Clean(line.Text), nextPlain);
    }

    /// <summary>按播放位置把已唱音节染成主题色。</summary>
    private void UpdateKaraoke(double tMs)
    {
        if (_sylLine is null || _sylRuns is null) return;

        int done = -1;
        var syllables = _sylLine.Syllables!;
        for (int i = 0; i < syllables.Count; i++)
        {
            if (syllables[i].StartTime <= tMs) done = i;
            else break;
        }

        for (int i = 0; i < _sylRuns.Length; i++)
        {
            if (i <= done)
            {
                _sylRuns[i].Foreground = _accentBrush;
                _sylRuns[i].FontWeight = FontWeights.Bold;
            }
            else
            {
                _sylRuns[i].Foreground = _plainBrush;
                _sylRuns[i].FontWeight = FontWeights.Normal;
            }
        }
    }

    /// <summary>最后一个 StartTime ≤ t 的行索引；无时间信息返回 -1。</summary>
    private static int FindLineIndex(System.Collections.Generic.List<ILineInfo> lines, double tMs)
    {
        int idx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            int? st = lines[i].StartTime;
            if (!st.HasValue) continue;
            if (st.Value <= tMs) idx = i;
            else break;
        }
        return idx;
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "♪";
        return text.Trim();
    }

    /// <summary>同 Clean，但空行返回空串（用于媒体条"下一行"，无内容时回退封面）。</summary>
    private static string CleanOrEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
}
