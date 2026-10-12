using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DogDesk.Interop;
using DogDesk.Models;
using DogDesk.Services.Mpv;
using DogDesk.Views;
using Path = System.IO.Path;

namespace DogDesk.Services;

/// <summary>
/// 壁纸引擎 v2：每个显示器一个独立壁纸实例（SetParent 到 WorkerW），
/// 支持多显示器、cover/contain/stretch 填充、亮度调节、图片文件夹轮播、
/// 多种内置动画；视频源优先 libmpv（全格式+硬解），缺失回退 MediaElement；
/// 频谱律动：WPF 源同窗口叠加，mpv 源用独立透明覆盖窗口。
/// </summary>
public sealed class WallpaperEngine : IDisposable
{
    private const long WS_EX_TRANSPARENT = 0x00000020;

    private sealed class WallpaperInstance
    {
        public Window? Win;                      // 壁纸窗口
        public Window? Overlay;                  // mpv 频谱覆盖窗口
        public SpectrumBarsControl? InlineBars;  // WPF 源同窗口频谱
        public Image? ImageHost;                 // 图片内容（轮播更新）
        public MediaElement? FallbackVideo;      // 回退视频
        public Rect Bounds;                      // 屏幕矩形（物理像素）
    }

    private readonly List<WallpaperInstance> _instances = new();
    private AppSettings? _settings;
    private AudioSpectrumService? _spectrum;
    private IntPtr _workerW;
    private DispatcherTimer? _slideshowTimer;
    private string[] _slideFiles = Array.Empty<string>();
    private int _slideIndex;

    public event Action<string>? Log;
    public bool IsRunning => _instances.Count > 0;

    /// <summary>按当前设置启动壁纸（每显示器一个实例）。</summary>
    public bool Start(AppSettings s, AudioSpectrumService? spectrum)
    {
        Stop();
        _settings = s;
        _spectrum = spectrum;

        if (spectrum is not null)
        {
            spectrum.Sensitivity = s.SpectrumSensitivity;
            spectrum.Smoothing = s.SpectrumSmoothing;
        }

        _workerW = WallpaperHost.FindOrCreateWorkerW();
        if (_workerW == IntPtr.Zero)
        {
            Log?.Invoke("[壁纸] 未找到 WorkerW 桌面宿主，启动失败（请确认桌面 Explorer 正常运行）");
            return false;
        }

        List<Rect> screens = GetScreens();
        if (screens.Count == 0)
        {
            Log?.Invoke("[壁纸] 未枚举到显示器");
            return false;
        }

        foreach (Rect bounds in screens)
            CreateInstance(bounds);

        StartSlideshowIfNeeded();

        Log?.Invoke($"[壁纸] 已启动 {_instances.Count} 个显示器实例（{DescribeSource()}）");
        return true;
    }

    // ---- 实例创建 ----

    private void CreateInstance(Rect bounds)
    {
        var inst = new WallpaperInstance { Bounds = bounds };

        if (_settings!.WallpaperKind == WallpaperKind.Video && MpvNativeLoader.IsLoaded)
            inst.Win = BuildMpvWindow(inst);
        else
            inst.Win = BuildWpfWindow(inst);

        ShowAndInject(inst);

        if (_spectrum is not null)
        {
            if (inst.Win is MpvWallpaperWindow)
            {
                var overlay = new SpectrumOverlayWindow();
                inst.Overlay = overlay;
                ShowTransparentOverlay(overlay, bounds);
                overlay.Start(_spectrum, bounds.Height * 0.16, _settings.SpectrumStyle);
            }
            else if (inst.InlineBars is not null)
            {
                inst.InlineBars.SpectrumStyle = _settings.SpectrumStyle;
                inst.InlineBars.Start(_spectrum);
            }
        }
    }

    private Window BuildMpvWindow(WallpaperInstance inst)
    {
        var win = new MpvWallpaperWindow();
        _instances.Add(inst);
        if (TryResolveFile(_settings!.WallpaperPath, out string full))
            win.StartPlayback(full, Log, _settings.StretchMode);
        else
            Log?.Invoke("[壁纸] 视频文件不存在，mpv 已创建但未加载内容");
        return win;
    }

    private Window BuildWpfWindow(WallpaperInstance inst)
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
        root.Children.Add(BuildWpfContent(inst));

        // 亮度遮罩（1=不遮，0.3=很暗）
        var dimmer = new Border
        {
            Background = new SolidColorBrush(Colors.Black),
            Opacity = 1.0 - Math.Clamp(_settings!.Brightness, 0.3, 1.0),
            IsHitTestVisible = false,
        };
        root.Children.Add(dimmer);

        if (_spectrum is not null)
        {
            var bars = new SpectrumBarsControl
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                BarHeight = inst.Bounds.Height * 0.16,
            };
            inst.InlineBars = bars;
            root.Children.Add(bars);
        }

        win.Content = root;
        return win;
    }

    private UIElement BuildWpfContent(WallpaperInstance inst)
    {
        return _settings!.WallpaperKind switch
        {
            WallpaperKind.Image => BuildImageContent(inst),
            WallpaperKind.Video => BuildFallbackVideoContent(inst),
            _ => _settings.AnimationStyle == AnimationStyle.Particles
                ? BuildParticlesContent(inst)
                : BuildAuroraContent(inst),
        };
    }

    private UIElement BuildImageContent(WallpaperInstance inst)
    {
        var img = new Image
        {
            Stretch = MapStretch(_settings!.StretchMode),
        };
        inst.ImageHost = img;
        if (TryResolveFile(_settings.WallpaperPath, out string full))
            img.Source = LoadImage(full);
        else if (Directory.Exists(_settings.WallpaperFolder))
        {
            string[] files = CollectImages(_settings.WallpaperFolder);
            if (files.Length > 0) img.Source = LoadImage(files[0]);
        }
        return img;
    }

    private UIElement BuildFallbackVideoContent(WallpaperInstance inst)
    {
        var media = new MediaElement
        {
            Stretch = MapStretch(_settings!.StretchMode),
            LoadedBehavior = MediaState.Play,
            UnloadedBehavior = MediaState.Stop,
            IsMuted = true,
        };
        inst.FallbackVideo = media;
        if (TryResolveFile(_settings.WallpaperPath, out string full))
            media.Source = new Uri(full);
        media.MediaEnded += (_, _) =>
        {
            media.Position = TimeSpan.Zero;
            media.Play();
        };
        return media;
    }

    private UIElement BuildParticlesContent(WallpaperInstance inst)
    {
        var host = new Grid { Background = BuildSpaceGradient() };
        var ctrl = new ParticleFieldControl();
        host.Children.Add(ctrl);
        ctrl.Start();
        return host;
    }

    private UIElement BuildAuroraContent(WallpaperInstance inst)
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
            Text = "动态壁纸\n任务栏媒体条 + 壁纸（libmpv / SMTC / 频谱 / 歌词）",
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

    private static LinearGradientBrush BuildSpaceGradient()
    {
        var g = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x0F, 0x2A), 0.00));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x16, 0x14, 0x3E), 0.55));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x24, 0x1B, 0x52), 1.00));
        return g;
    }

    // ---- 窗口注入 ----

    private void ShowAndInject(WallpaperInstance inst)
    {
        Window win = inst.Win!;
        win.Show();
        IntPtr hwnd = new WindowInteropHelper(win).EnsureHandle();

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));

        NativeMethods.SetParent(hwnd, _workerW);

        Rect b = inst.Bounds;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_BOTTOM,
            (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void ShowTransparentOverlay(Window overlay, Rect bounds)
    {
        overlay.Show();
        IntPtr hwnd = new WindowInteropHelper(overlay).EnsureHandle();

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT));

        NativeMethods.SetParent(hwnd, _workerW);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    // ---- 图片文件夹轮播 ----

    private void StartSlideshowIfNeeded()
    {
        StopSlideshow();

        if (_settings!.WallpaperKind != WallpaperKind.Image || !_settings.SlideshowEnabled)
            return;

        string folder = _settings.WallpaperFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        _slideFiles = CollectImages(folder);
        if (_slideFiles.Length == 0) return;
        _slideIndex = 0;

        _slideshowTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.SlideshowIntervalSec, 3, 600))
        };
        _slideshowTimer.Tick += (_, _) => AdvanceSlide();
        _slideshowTimer.Start();
        Log?.Invoke($"[壁纸] 图片轮播已启动：{_slideFiles.Length} 张，每 {_settings.SlideshowIntervalSec:0}s 切换");
    }

    private void AdvanceSlide()
    {
        if (_slideFiles.Length == 0) return;
        _slideIndex = (_slideIndex + 1) % _slideFiles.Length;
        string file = _slideFiles[_slideIndex];
        foreach (var inst in _instances)
            if (inst.ImageHost is not null)
                inst.ImageHost.Source = LoadImage(file);
    }

    private void StopSlideshow()
    {
        if (_slideshowTimer is null) return;
        _slideshowTimer.Stop();
        _slideshowTimer = null;
        _slideFiles = Array.Empty<string>();
    }

    private static string[] CollectImages(string folder) =>
        Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static BitmapImage LoadImage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        // B9：按屏幕宽度上限解码，避免超大壁纸/轮播图全尺寸解码驻留内存
        bmp.DecodePixelWidth = (int)Math.Min(SystemParameters.VirtualScreenWidth, 2560);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static Stretch MapStretch(StretchMode mode) => mode switch
    {
        StretchMode.Contain => Stretch.Uniform,
        StretchMode.Stretch => Stretch.Fill,
        _ => Stretch.UniformToFill,
    };

    // ---- 停止 / 释放 ----

    public void Stop()
    {
        StopSlideshow();

        foreach (var inst in _instances)
        {
            inst.InlineBars?.Stop();
            if (inst.Overlay is not null)
                CloseInjected(inst.Overlay);
            if (inst.Win is not null)
                CloseInjected(inst.Win);
        }
        _instances.Clear();
        _spectrum = null;

        if (_workerW != IntPtr.Zero)
            Log?.Invoke("[壁纸] 已停止");
    }

    private void CloseInjected(Window win)
    {
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

    // ---- 工具 ----

    private string DescribeSource() => _settings!.WallpaperKind switch
    {
        WallpaperKind.Video => "libmpv 视频（全格式+硬解）",
        WallpaperKind.Image => _settings.SlideshowEnabled ? "图片文件夹轮播" : "图片",
        _ => _settings.AnimationStyle == AnimationStyle.Particles ? "粒子星空动画" : "极光动画",
    };

    private static List<Rect> GetScreens()
    {
        var list = new List<Rect>();
        try
        {
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                list.Add(new Rect(b.X, b.Y, b.Width, b.Height));
            }
        }
        catch
        {
            list.Add(new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight));
        }
        return list;
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
}
