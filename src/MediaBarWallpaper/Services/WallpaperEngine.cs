using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MediaBarWallpaper.Interop;
using MediaBarWallpaper.Services.Mpv;
using MediaBarWallpaper.Views;
using Path = System.IO.Path;

namespace MediaBarWallpaper.Services;

public enum WallpaperKind { Animated, Image, Video }

/// <summary>
/// 壁纸引擎：创建壁纸窗口并 SetParent 到 WorkerW，铺满整个虚拟桌面。
/// 视频源优先使用 libmpv（全格式 + 硬解，独立 MpvWallpaperWindow）；
/// libmpv 缺失时回退 WPF MediaElement。频谱律动：
/// WPF 源同窗口叠加 SpectrumBarsControl；mpv 源因 airspace 用独立透明覆盖窗口。
/// </summary>
public sealed class WallpaperEngine : IDisposable
{
    private Window? _window;
    private Window? _overlayWindow;
    private SpectrumBarsControl? _inlineBars;
    private IntPtr _workerW;
    private AudioSpectrumService? _spectrum;

    private const long WS_EX_TRANSPARENT = 0x00000020; // 鼠标穿透（分层覆盖窗口）

    public event Action<string>? Log;
    public bool IsRunning => _window is not null;

    public bool Start(WallpaperKind kind, string? filePath = null, AudioSpectrumService? spectrum = null)
    {
        Stop();
        _spectrum = spectrum;

        _workerW = WallpaperHost.FindOrCreateWorkerW();
        if (_workerW == IntPtr.Zero)
        {
            Log?.Invoke("[壁纸] 未找到 WorkerW 桌面宿主，启动失败（请确认桌面 Explorer 正常运行）");
            return false;
        }

        if (kind == WallpaperKind.Video)
        {
            if (MpvNativeLoader.IsLoaded)
                StartMpvVideo(filePath, spectrum);
            else
            {
                Log?.Invoke("[壁纸] libmpv 未加载，视频回退 WPF MediaElement（建议 mp4/wmv，格式支持有限）");
                StartWpfWindow(BuildVideoContent(filePath), spectrum, "WPF 视频壁纸（MediaElement 回退）");
            }
        }
        else
        {
            UIElement content = kind == WallpaperKind.Image
                ? BuildImageContent(filePath)
                : BuildAnimatedContent();
            StartWpfWindow(content, spectrum, kind == WallpaperKind.Image ? "图片壁纸" : "内置动画壁纸");
        }

        return true;
    }

    // ---- mpv 视频壁纸 ----

    private void StartMpvVideo(string? filePath, AudioSpectrumService? spectrum)
    {
        TryResolveFile(filePath, out string full);

        var win = new MpvWallpaperWindow();
        _window = win;
        ShowAndInject(win, NativeMethods.HWND_BOTTOM);
        win.StartPlayback(full, Log);

        if (spectrum is not null)
        {
            var overlay = new SpectrumOverlayWindow();
            _overlayWindow = overlay;
            ShowTransparentOverlay(overlay);
            overlay.Start(spectrum);
        }

        Log?.Invoke($"[壁纸] libmpv 视频壁纸已启动，硬解+全格式，频谱律动={(spectrum is not null ? "开" : "关")}");
    }

    // ---- WPF 内容壁纸（动画 / 图片 / 回退视频）----

    private void StartWpfWindow(UIElement content, AudioSpectrumService? spectrum, string label)
    {
        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.Black,
        };

        var root = new Grid();
        root.Children.Add(content);

        if (spectrum is not null)
        {
            var bars = new SpectrumBarsControl
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                BarHeight = 110,
            };
            _inlineBars = bars;
            root.Children.Add(bars);
        }

        win.Content = root;
        _window = win;
        ShowAndInject(win, NativeMethods.HWND_BOTTOM);

        // Show 之后 ActualWidth 就绪，再启动频谱
        if (spectrum is not null && _inlineBars is not null)
            _inlineBars.Start(spectrum);

        GetVirtualScreen(out int vx, out int vy, out int vw, out int vh);
        Log?.Invoke($"[壁纸] {label} 已注入 WorkerW(0x{_workerW.ToInt64():X})，{vw}x{vh} @ ({vx},{vy})，频谱律动={(spectrum is not null ? "开" : "关")}");
    }

    // ---- 窗口注入与定位 ----

    private IntPtr ShowAndInject(Window win, IntPtr insertAfter)
    {
        win.Show();
        IntPtr hwnd = new WindowInteropHelper(win).EnsureHandle();

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));

        NativeMethods.SetParent(hwnd, _workerW);

        GetVirtualScreen(out int vx, out int vy, out int vw, out int vh);
        NativeMethods.SetWindowPos(hwnd, insertAfter, vx, vy, vw, vh,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        return hwnd;
    }

    private void ShowTransparentOverlay(Window overlay)
    {
        overlay.Show();
        IntPtr hwnd = new WindowInteropHelper(overlay).EnsureHandle();

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT));

        NativeMethods.SetParent(hwnd, _workerW);

        GetVirtualScreen(out int vx, out int vy, out int vw, out int vh);
        // IntPtr.Zero = HWND_TOP：放到 WorkerW 子窗口 z 序顶部（非全局置顶），盖在视频窗口上
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, vx, vy, vw, vh,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public void Stop()
    {
        CloseInjected(_overlayWindow);
        _overlayWindow = null;

        _inlineBars?.Stop();
        _inlineBars = null;

        CloseInjected(_window);
        _window = null;
        _spectrum = null;

        if (_workerW != IntPtr.Zero)
            Log?.Invoke("[壁纸] 已停止");
    }

    private void CloseInjected(Window? win)
    {
        if (win is null) return;

        IntPtr hwnd = new WindowInteropHelper(win).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetParent(hwnd, IntPtr.Zero);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        win.Close();
    }

    public void Dispose() => Stop();

    // ---- 内容构建 ----

    private static UIElement BuildAnimatedContent()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x1E, 0x1B, 0x4B), 0.00));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x3B, 0x1D, 0x60), 0.50));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x2E, 0x59), 1.00));

        var grid = new Grid { Background = gradient };

        var glow = new Ellipse
        {
            Width = 900,
            Height = 900,
            Fill = new RadialGradientBrush(Color.FromArgb(110, 129, 140, 248), Colors.Transparent),
        };
        var tf = new TranslateTransform();
        glow.RenderTransform = tf;
        grid.Children.Add(glow);

        var ax = new DoubleAnimation(
            -400, SystemParameters.VirtualScreenWidth + 400, TimeSpan.FromSeconds(26))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        var ay = new DoubleAnimation(
            -200, SystemParameters.VirtualScreenHeight + 200, TimeSpan.FromSeconds(34))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        tf.BeginAnimation(TranslateTransform.XProperty, ax);
        tf.BeginAnimation(TranslateTransform.YProperty, ay);

        var txt = new TextBlock
        {
            Text = "动态壁纸\n任务栏媒体条 + Wallpaper（libmpv / SMTC / 频谱 / 歌词）",
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 140),
        };
        grid.Children.Add(txt);

        return grid;
    }

    private static UIElement BuildImageContent(string? path)
    {
        var img = new Image { Stretch = Stretch.UniformToFill };
        if (TryResolveFile(path, out string full))
            img.Source = new BitmapImage(new Uri(full));
        return img;
    }

    private static UIElement BuildVideoContent(string? path)
    {
        var media = new MediaElement
        {
            Stretch = Stretch.UniformToFill,
            LoadedBehavior = MediaState.Play,
            UnloadedBehavior = MediaState.Stop,
            IsMuted = true,
        };

        if (TryResolveFile(path, out string full))
            media.Source = new Uri(full);

        media.MediaEnded += (_, _) =>
        {
            media.Position = TimeSpan.Zero;
            media.Play();
        };
        return media;
    }

    private static bool TryResolveFile(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        string candidate = Path.GetFullPath(path);
        if (!File.Exists(candidate)) return false;
        full = candidate;
        return true;
    }

    private static void GetVirtualScreen(out int x, out int y, out int w, out int h)
    {
        x = (int)SystemParameters.VirtualScreenLeft;
        y = (int)SystemParameters.VirtualScreenTop;
        w = (int)SystemParameters.VirtualScreenWidth;
        h = (int)SystemParameters.VirtualScreenHeight;
    }
}
