using System.ComponentModel;
using System.Windows;
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

    private readonly IMediaSession _session;
    private readonly AppSettings _settings;
    private readonly SystemVolumeService _volume = new();
    private readonly DispatcherTimer _layoutTimer;
    private NativeMethods.RECT _lastTaskbarRect;
    private bool _positioned;
    private bool _dragging;

    public TaskbarService.DockPosition Dock { get; set; } = TaskbarService.DockPosition.Center;

    public MediaBarWindow(IMediaSession session, AppSettings settings)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _session.PropertyChanged += OnSessionChanged;

        Width = settings.MediaBarWidth;
        Height = BarHeightDip;

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

    /// <summary>应用设置变化（渐变开关 / 自动隐藏）到当前窗口。</summary>
    public void RefreshFromSettings()
    {
        if (!_settings.MediaBarCoverGradient)
        {
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x19, 0x21, 0x2E));
        }
        else
        {
            RefreshSessionUi(); // 会重建封面渐变
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

    // ---- 滚轮音量 ----

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_volume.Available) return;
        float v = _volume.GetVolume();
        v += e.Delta > 0 ? 0.05f : -0.05f;
        _volume.SetVolume(v);
        VolumeText.Text = $"{(int)Math.Round(_volume.GetVolume() * 100)}%";
        e.Handled = true;
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
