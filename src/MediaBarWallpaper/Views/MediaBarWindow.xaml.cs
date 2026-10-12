using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MediaBarWallpaper.Interop;
using MediaBarWallpaper.Services;

namespace MediaBarWallpaper.Views;

/// <summary>
/// 任务栏媒体条：无边框透明悬浮窗，吸附到任务栏区域。
/// 通过定时器检测任务栏矩形变化并重新定位；未播放时自动降低透明度。
/// </summary>
public partial class MediaBarWindow : Window
{
    private const double BarWidthDip = 360;
    private const double BarHeightDip = 52;

    private readonly IMediaSession _session;
    private readonly DispatcherTimer _layoutTimer;
    private NativeMethods.RECT _lastTaskbarRect;
    private bool _positioned;

    public TaskbarService.DockPosition Dock { get; set; } = TaskbarService.DockPosition.Center;

    public MediaBarWindow(IMediaSession session)
    {
        InitializeComponent();
        _session = session;
        _session.PropertyChanged += OnSessionChanged;

        Width = BarWidthDip;
        Height = BarHeightDip;

        SourceInitialized += (_, _) => ApplyWindowStyle();
        Loaded += (_, _) => Reposition();

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

        // 任务栏矩形未变化且已定位过，跳过（避免无谓的 SetWindowPos 抖动）
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
            // 窗口未完全初始化时忽略，使用 1.0 缩放
        }

        int wPx = (int)(Width * scaleX);
        int hPx = (int)(Height * scaleY);

        (int x, int y, int w, int h) =
            TaskbarService.ComputeAnchor(taskbar.Value, wPx, hPx, Dock);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            x, y, w, h, NativeMethods.SWP_NOACTIVATE);
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IMediaSession.Title)
            or nameof(IMediaSession.Artist)
            or nameof(IMediaSession.Progress01)
            or nameof(IMediaSession.Thumbnail)
            or nameof(IMediaSession.IsReal))
            RefreshSessionUi();
        else if (e.PropertyName == nameof(IMediaSession.IsPlaying))
            AnimateOpacity(_session.IsPlaying ? 1.0 : 0.35);
    }

    private void RefreshSessionUi()
    {
        TitleText.Text = _session.Title;
        ArtistText.Text = _session.Artist;
        PlayButton.Content = _session.IsPlaying ? "\u23F8" : "\u25B6";
        double p = Math.Clamp(_session.Progress01, 0.0, 1.0);
        ProgressFill.Width = TrackBorder.ActualWidth * p;

        if (_session.Thumbnail is { } thumb)
        {
            CoverImage.Source = thumb;
            CoverImage.Visibility = Visibility.Visible;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            CoverImage.Source = null;
            CoverImage.Visibility = Visibility.Collapsed;
            CoverPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void AnimateOpacity(double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => _session.Previous();
    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _session.TogglePlayPause();
    private void OnNextClick(object sender, RoutedEventArgs e) => _session.Next();

    protected override void OnClosed(EventArgs e)
    {
        _layoutTimer.Stop();
        _session.PropertyChanged -= OnSessionChanged;
        base.OnClosed(e);
    }
}
