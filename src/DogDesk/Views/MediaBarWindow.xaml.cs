using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DogDesk.Interop;
using DogDesk.Models;
using DogDesk.Services;

namespace DogDesk.Views;

/// <summary>
/// 任务栏媒体条：无边框透明悬浮窗，吸附到任务栏区域。
/// 定时器检测任务栏矩形变化并重新定位；可拖动进度条；滚轮调节系统音量；
/// 未播放自动淡出（可配置）；封面主色玻璃感渐变背景（可配置）。
/// </summary>
public partial class MediaBarWindow : Window
{
    private const double BarHeightDip = 56;
    private const int GWL_STYLE = -16;
    private const long WS_CHILD = 0x40000000;
    private const long WS_POPUP = 0x80000000;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private readonly IMediaSession _session;
    private readonly AppSettings _settings;
    private readonly SystemVolumeService _volume = new();
    private readonly DispatcherTimer _layoutTimer;
    private NativeMethods.RECT _lastTaskbarRect;
    private bool _positioned;
    private bool _dragging;
    private bool _embedded;
    private IntPtr _embedParent;

    public TaskbarService.DockPosition Dock { get; set; } = TaskbarService.DockPosition.Center;

    public MediaBarWindow(IMediaSession session, AppSettings settings, AudioSpectrumService? spectrum = null)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _session.PropertyChanged += OnSessionChanged;

        Width = settings.MediaBarWidth;
        Height = BarHeightDip;

        if (settings.MediaBarInlineSpectrum && spectrum is not null)
        {
            InlineSpectrum.SpectrumStyle = settings.SpectrumStyle;
            InlineSpectrum.Visibility = Visibility.Visible;
            InlineSpectrum.Loaded += (_, _) => InlineSpectrum.Start(spectrum);
        }

        SourceInitialized += (_, _) => ApplyWindowStyle();
        Loaded += (_, _) => Reposition();
        MouseWheel += OnMouseWheel;

        _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _layoutTimer.Tick += (_, _) => Reposition();
        _layoutTimer.Start();

        RefreshSessionUi();
    }

    /// <summary>设置 ToolWindow 扩展样式，避免出现在 Alt+Tab。</summary>
    private void ApplyWindowStyle()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_TOOLWINDOW));
    }

    /// <summary>按任务栏当前位置重新吸附媒体条（物理像素坐标）。</summary>
    private void Reposition()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !IsLoaded) return;

        Rect? taskbar = TaskbarService.GetTaskbarRect();
        if (taskbar is null) return;

        if (_positioned &&
            Math.Abs(taskbar.Value.X - _lastTaskbarRect.Left) < 0.5 &&
            Math.Abs(taskbar.Value.Y - _lastTaskbarRect.Top) < 0.5 &&
            Math.Abs(taskbar.Value.Width - _lastTaskbarRect.Width) < 0.5 &&
            Math.Abs(taskbar.Value.Height - _lastTaskbarRect.Height) < 0.5)
        {
            return;
        }

        _lastTaskbarRect = new NativeMethods.RECT
        {
            Left = (int)taskbar.Value.X,
            Top = (int)taskbar.Value.Y,
            Right = (int)(taskbar.Value.X + taskbar.Value.Width),
            Bottom = (int)(taskbar.Value.Y + taskbar.Value.Height),
        };
        _positioned = true;

        double scaleX = 1.0, scaleY = 1.0;
        try
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            scaleX = dpi.DpiScaleX;
            scaleY = dpi.DpiScaleY;
        }
        catch (InvalidOperationException)
        {
        }

        int wPx = (int)(Width * scaleX);
        int hPx = (int)(Height * scaleY);

        (int x, int y, int w, int h) =
            TaskbarService.ComputeAnchor(taskbar.Value, wPx, hPx, Dock);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            x, y, w, h, NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>强制重算吸附位置（设置变化后调用）。</summary>
    public void ForceReposition()
    {
        _positioned = false;
        Reposition();
    }

    // ---- 任务栏嵌入（SetParent 子窗口，自包含，不影响其它窗口/服务） ----

    /// <summary>切换嵌入⇄悬浮。失败自动回退悬浮并写日志。</summary>
    public void SetEmbed(bool on)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (on)
            {
                IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) { LogFallback("未找到任务栏 Shell_TrayWnd"); return; }
                IntPtr rebar = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
                if (rebar == IntPtr.Zero) rebar = tray; // 兜底：直接挂托盘
                if (NativeMethods.GetWindowRect(rebar, out var r) == false) { LogFallback("读取 Rebar 矩形失败"); return; }

                _layoutTimer.Stop();
                NativeMethods.SetParent(hwnd, rebar);
                long style = NativeMethods.GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
                NativeMethods.SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr((style & ~WS_POPUP) | WS_CHILD));
                long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
                    new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));

                int barW = (int)Width;
                int barH = (r.Bottom - r.Top) - 4;
                // 槽位：右侧避开任务按钮区，靠左贴边偏移 8
                int slotX = 8;
                int slotY = 2;
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, slotX, slotY, barW, barH,
                    NativeMethods.SWP_NOACTIVATE | SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
                InvalidateVisual();
                UpdateLayout();
                _embedded = true;
                _embedParent = rebar;
            }
            else
            {
                if (!_embedded) return;
                NativeMethods.SetParent(hwnd, IntPtr.Zero);
                long style = NativeMethods.GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
                NativeMethods.SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr((style & ~WS_CHILD) | WS_POPUP));
                _embedded = false;
                _embedParent = IntPtr.Zero;
                _positioned = false;
                _layoutTimer.Start();
                Reposition();
            }
        }
        catch (Exception ex)
        {
            LogFallback("嵌入异常：" + ex.Message);
            try { SetEmbed(false); } catch { }
        }
    }

    private void LogFallback(string msg)
    {
        System.Diagnostics.Debug.WriteLine("[MediaBar] " + msg);
    }

    /// <summary>滚轮音量（修饰键：Ctrl 细调 / Shift 静音切换）。</summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_volume.Available) return;
        float v = _volume.GetVolume();
        double step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 0.02 : 0.05;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            _volume.SetMute(!_volume.GetMute());
        }
        else
        {
            v += e.Delta > 0 ? (float)step : -(float)step;
            _volume.SetVolume(Math.Clamp(v, 0f, 1f));
        }
        VolumeText.Text = $"{(int)Math.Round(_volume.GetVolume() * 100)}%";
        if (_settings.MediaBarOsd) ShowVolumeOsd();
        e.Handled = true;
    }

    private Window? _osd;
    private void ShowVolumeOsd()
    {
        try
        {
            _osd?.Close();
            _osd = new Window
            {
                Width = 120, Height = 60,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(200, 20, 22, 31)),
                Topmost = true, ShowInTaskbar = false,
                Owner = this,
            };
            var tb = new TextBlock
            {
                Text = $"🔊 {VolumeText.Text}",
                Foreground = Brushes.White, FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _osd.Content = tb;
            _osd.Show();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            t.Tick += (_, _) => { t.Stop(); try { _osd?.Close(); } catch { } _osd = null; };
            t.Start();
        }
        catch { }
    }

    /// <summary>媒体条两行歌词：由主窗口在歌词滚动时推送（当前行+下一行）。</summary>
    public void UpdateLyric(string current, string next)
    {
        if (!_settings.LyricsDualLine) { LyricPanel.Visibility = Visibility.Collapsed; return; }
        LyricPanel.Visibility = string.IsNullOrEmpty(current) ? Visibility.Collapsed : Visibility.Visible;
        LyricCurrent.Text = current;
        LyricNext.Text = next;
    }

    /// <summary>应用设置变化（渐变开关 / 自动隐藏）到当前窗口。</summary>
    public void RefreshFromSettings()
    {
        switch (_settings.MediaBarMaterial)
        {
            case "Transparent":
                RootBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0));
                break;
            case "Custom":
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(_settings.MediaBarCustomColor ?? "#14161F");
                    RootBorder.Background = new SolidColorBrush(c);
                }
                catch { goto default; }
                break;
            default: // Acrylic：真亚克力需 SetWindowCompositionAttribute，当前回退封面渐变
                if (!_settings.MediaBarCoverGradient)
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x19, 0x21, 0x2E));
                else
                    RefreshSessionUi();
                break;
        }
        AnimateOpacity(_session.IsPlaying ? 1.0 : (_settings.MediaBarAutoHide ? 0.12 : 0.35));
    }

    // ---- 会话刷新 ----

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IMediaSession.Title)
            or nameof(IMediaSession.Artist)
            or nameof(IMediaSession.Progress01)
            or nameof(IMediaSession.Thumbnail)
            or nameof(IMediaSession.IsReal))
            RefreshSessionUi();
        else if (e.PropertyName == nameof(IMediaSession.IsPlaying))
            AnimateOpacity(_session.IsPlaying ? 1.0 : (_settings.MediaBarAutoHide ? 0.12 : 0.35));
    }

    private void RefreshSessionUi()
    {
        TitleText.Text = _session.Title;
        ArtistText.Text = _session.Artist;
        UpdateLyric("", ""); // 切歌清空旧歌词，等主窗口推送新行
        PlayButton.Content = _session.IsPlaying ? "\u23F8" : "\u25B6";

        RefreshProgress(_session.Progress01);

        if (_session.Thumbnail is { } thumb)
        {
            CoverImage.Source = thumb;
            CoverImage.Visibility = Visibility.Visible;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
            if (_settings.MediaBarCoverGradient)
                RootBorder.Background = BuildCoverGradient(CoverPalette.Extract(thumb));
        }
        else
        {
            CoverImage.Source = null;
            CoverImage.Visibility = Visibility.Collapsed;
            CoverPlaceholder.Visibility = Visibility.Visible;
            if (_settings.MediaBarCoverGradient)
                RootBorder.Background = BuildCoverGradient(CoverPalette.Default);
        }

        if (_volume.Available)
            VolumeText.Text = $"{(int)Math.Round(_volume.GetVolume() * 100)}%";
    }

    private void RefreshProgress(double fraction)
    {
        if (_dragging) return;
        double w = TrackBorder.ActualWidth;
        ProgressFill.Width = Math.Max(0, Math.Min(w, w * Math.Clamp(fraction, 0, 1)));
    }

    private static LinearGradientBrush BuildCoverGradient(Color accent)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, accent.R, accent.G, accent.B), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xCC, 0x11, 0x12, 0x1C), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0x0B, 0x0C, 0x14), 1.00));
        return brush;
    }

    private void AnimateOpacity(double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, anim);
    }

    // ---- 进度拖动 ----

    private void OnTrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        UpdateSeek(e.GetPosition(TrackBorder));
        e.Handled = true;
    }

    private void OnTrackMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateSeek(e.GetPosition(TrackBorder));
    }

    private void OnTrackMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        UpdateSeek(e.GetPosition(TrackBorder));
    }

    private void UpdateSeek(Point p)
    {
        double w = TrackBorder.ActualWidth;
        if (w <= 1) return;
        double f = Math.Clamp(p.X / w, 0, 1);
        ProgressFill.Width = w * f;
        _session.Seek(f);
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => _session.Previous();
    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _session.TogglePlayPause();
    private void OnNextClick(object sender, RoutedEventArgs e) => _session.Next();

    protected override void OnClosed(EventArgs e)
    {
        _layoutTimer.Stop();
        _session.PropertyChanged -= OnSessionChanged;
        _volume.Dispose();
        base.OnClosed(e);
    }
}
