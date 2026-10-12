using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lyricify.Lyrics.Models;
using DogDesk.Models;
using DogDesk.Services;

namespace DogDesk.Views;

/// <summary>
/// 全屏沉浸式歌词动画窗口（Folia 风格自研复刻）：
/// Topmost 无边框全屏，Esc / 点击退出，背景按 FullscreenLyricsOpacity 遮罩。
/// 复用 <see cref="LyricsService"/> 的逐字引擎与 <see cref="FullSyllableLineInfo"/> 数据，随 PositionSec 同步。
/// 样式：Flow 流光（横向渐变扫过当前行）、Name 浮名（逐字放大浮现）、Chorus 群唱（当前行高亮脉冲）。
/// </summary>
public partial class FullscreenLyricsWindow : Window
{
    private readonly IMediaSession _session;
    private readonly LyricsService _lyrics;
    private readonly AppSettings _settings;
    private DispatcherTimer? _timer;
    private int _currentIndex = -1;

    private FullSyllableLineInfo? _sylLine;
    private Run[]? _sylRuns;

    private SolidColorBrush _accentBrush = new(Color.FromRgb(0x81, 0x8C, 0xF8));
    private readonly SolidColorBrush _plainBrush = new(Colors.White);

    public FullscreenLyricsWindow(IMediaSession session, LyricsService lyrics, AppSettings settings)
    {
        InitializeComponent();
        _session = session;
        _lyrics = lyrics;
        _settings = settings;
        ApplyStyleSettings();
    }

    public void ApplyStyleSettings()
    {
        byte mask = (byte)(Math.Clamp(_settings.FullscreenLyricsOpacity, 0.3, 1.0) * 255);
        MaskBorder.Background = new SolidColorBrush(Color.FromArgb(mask, 0, 0, 0));

        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_settings.AccentColor);
            _accentBrush = new SolidColorBrush(c);
        }
        catch { _accentBrush = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8)); }
        _accentBrush.Freeze();
    }

    /// <summary>由封面主色动态联动（DynamicTheme）。</summary>
    public void ApplyAccent(Color c)
    {
        _accentBrush = new SolidColorBrush(c);
        _accentBrush.Freeze();
    }

    public void Start()
    {
        Show();
        _currentIndex = -1;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
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

    private async void OnTick(object? sender, EventArgs e)
    {
        await _lyrics.TryFetchAsync(_session.Title, _session.Artist, _session.DurationSec);

        LyricsData? data = _lyrics.Data;
        if (data?.Lines is null || data.Lines.Count == 0)
        {
            if (_currentIndex != -1)
            {
                _currentIndex = -1;
                _sylLine = null; _sylRuns = null;
                CurrentLine.Text = "♪";
                NextLine.Text = "";
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
        else if (_sylLine is not null && _sylRuns is not null)
        {
            UpdateKaraoke(tMs);
        }
    }

    private void RenderLine(System.Collections.Generic.List<ILineInfo> lines, int idx)
    {
        _sylLine = null; _sylRuns = null;
        CurrentLine.Inlines.Clear();
        CurrentLine.Foreground = _plainBrush;

        if (idx < 0) { CurrentLine.Text = "♪"; NextLine.Text = ""; return; }

        var line = lines[idx];
        bool syllable = _settings.LyricsKaraoke && line is FullSyllableLineInfo full && full.Syllables is { Count: > 0 };

        switch (_settings.LyricsAnimationStyle)
        {
            case LyricsAnimationStyle.Flow:
                RenderFlow(line, syllable);
                break;
            case LyricsAnimationStyle.Chorus:
                RenderChorus(line, syllable);
                break;
            default: // Name 浮名
                RenderName(line, syllable ? (FullSyllableLineInfo)line : null);
                break;
        }

        NextLine.Text = idx + 1 < lines.Count ? Clean(lines[idx + 1].Text) : "";
    }

    /// <summary>流光：整行渐变横向扫过。</summary>
    private void RenderFlow(ILineInfo line, bool syllable)
    {
        CurrentLine.Text = Clean(line.Text);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.0));
        brush.GradientStops.Add(new GradientStop(_accentBrush.Color, 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 1.0));
        // 横向扫动：移动中间高亮 stop 的位置
        var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(3.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        brush.GradientStops[1].BeginAnimation(GradientStop.OffsetProperty, anim);
        CurrentLine.Foreground = brush;
    }

    /// <summary>群唱：当前行主题色高亮脉冲（加分项）。</summary>
    private void RenderChorus(ILineInfo line, bool syllable)
    {
        CurrentLine.Text = Clean(line.Text);
        CurrentLine.Foreground = _accentBrush;
        var pulse = new DoubleAnimation(0.85, 1.0, TimeSpan.FromSeconds(1.4))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        CurrentLine.BeginAnimation(OpacityProperty, pulse);
    }

    /// <summary>浮名：逐字放大浮现（未唱 dim，已唱 accent + 放大）。</summary>
    private void RenderName(ILineInfo line, FullSyllableLineInfo? full)
    {
        CurrentLine.BeginAnimation(OpacityProperty, null);
        CurrentLine.Opacity = 1.0;

        if (full is null) { CurrentLine.Text = Clean(line.Text); CurrentLine.Foreground = _plainBrush; return; }

        _sylLine = full;
        _sylRuns = new Run[full.Syllables.Count];
        for (int i = 0; i < full.Syllables.Count; i++)
        {
            var run = new Run(full.Syllables[i].Text) { Foreground = _plainBrush, FontSize = 84 };
            _sylRuns[i] = run;
            CurrentLine.Inlines.Add(run);
        }
        UpdateKaraoke(_session.PositionSec * 1000.0);
    }

    private void UpdateKaraoke(double tMs)
    {
        if (_sylLine is null || _sylRuns is null) return;
        var syllables = _sylLine.Syllables!;

        int done = -1;
        for (int i = 0; i < syllables.Count; i++)
        {
            if (syllables[i].StartTime <= tMs) done = i;
            else break;
        }

        for (int i = 0; i < _sylRuns.Length; i++)
        {
            bool sung = i <= done;
            // 浮名：已唱字放大+主题色，未唱字缩小+白色（Run 无 Opacity/RenderTransform，用 FontSize 模拟）
            _sylRuns[i].Foreground = sung ? _accentBrush : _plainBrush;
            _sylRuns[i].FontSize = sung ? 100.0 : 80.0;
        }
    }

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

    private static string Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "♪" : text.Trim();

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) Close();
    }

    private void OnWindowMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => Close();
}
