using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Lyricify.Lyrics.Models;
using MediaBarWallpaper.Interop;
using MediaBarWallpaper.Services;

namespace MediaBarWallpaper.Views;

/// <summary>
/// 桌面歌词窗口：吸附在任务栏上边缘上方、水平居中，
/// 显示当前行（高亮）与下一行（暗色），按播放位置定时同步。
/// 逐字歌词自动降级为逐行显示（行级 StartTime/Text）。
/// </summary>
public partial class LyricsWindow : Window
{
    private const int WinW = 800;
    private const int WinH = 104;
    private const long WS_EX_TRANSPARENT = 0x00000020;

    private readonly IMediaSession _session;
    private readonly LyricsService _lyrics;
    private DispatcherTimer? _timer;
    private int _currentIndex = -1;
    private bool _styled;

    public LyricsWindow(IMediaSession session, LyricsService lyrics)
    {
        InitializeComponent();
        _session = session;
        _lyrics = lyrics;
    }

    public void Start()
    {
        Relocate();
        Show();
        EnsureStyle();

        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
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
                y = (int)tb.Bottom + 8;   // 顶部任务栏：放任务栏下方
            else
                y = (int)tb.Top - WinH - 8; // 底部任务栏：放任务栏上方
        }
        else
        {
            // 垂直任务栏/取不到矩形：主屏工作区底部居中
            Rect wa = SystemParameters.WorkArea;
            x = (int)(wa.Left + (wa.Width - WinW) / 2);
            y = (int)(wa.Bottom - WinH - 8);
        }

        // 水平不超出虚拟屏幕
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
            CurrentLine.Text = idx >= 0 ? Clean(data.Lines[idx].Text) : "♪";
            NextLine.Text = idx + 1 < data.Lines.Count ? Clean(data.Lines[idx + 1].Text) : "";
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
}
