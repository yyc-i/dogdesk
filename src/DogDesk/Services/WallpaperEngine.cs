using System.IO;
using System.Runtime.InteropServices;
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
using Microsoft.Web.WebView2.Wpf;
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
        public ParticleFieldControl? Particles;  // 粒子/场景预设控件（降帧/暂停用）
        public WebView2? HtmlView;               // HTML 壁纸
        public Rect Bounds;                      // 屏幕矩形（物理像素）
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    private const uint GW_OWNER = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private readonly List<WallpaperInstance> _instances = new();
    private AppSettings? _settings;
    private AudioSpectrumService? _spectrum;
    private IntPtr _workerW;
    private DispatcherTimer? _slideshowTimer;
    private string[] _slideFiles = Array.Empty<string>();
    private int _slideIndex;

    // v0.8.0：视频轮播 / 全屏降载
    private List<string> _videoPlaylist = new();
    private int _videoIndex;
    private DispatcherTimer? _fsTimer;
    private bool _pausedByFullscreen;

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

        // 先解析视频轮播列表，再逐屏创建实例（mpv 窗口启动时即取当前视频）
        StartVideoPlaylistIfNeeded();

        foreach (Rect bounds in screens)
            CreateInstance(bounds);

        StartSlideshowIfNeeded();
        StartFullscreenWatcherIfNeeded();

        Log?.Invoke($"[壁纸] 已启动 {_instances.Count} 个显示器实例（{DescribeSource()}）");
        return true;
    }

    // ---- 视频轮播列表 ----

    /// <summary>解析有效视频列表：优先多视频轮播，否则回退单个 WallpaperPath。</summary>
    private List<string> ResolveVideoList()
    {
        var list = new List<string>();
        if (_settings!.VideoPlaylist is not null)
            foreach (var p in _settings.VideoPlaylist)
                if (TryResolveFile(p, out string full)) list.Add(full);
        if (list.Count == 0 && TryResolveFile(_settings.WallpaperPath, out string single))
            list.Add(single);
        return list;
    }

    private string? CurrentVideoFile =>
        _videoIndex >= 0 && _videoIndex < _videoPlaylist.Count ? _videoPlaylist[_videoIndex] : null;

    private void StartVideoPlaylistIfNeeded()
    {
        _videoPlaylist = ResolveVideoList();
        _videoIndex = _videoPlaylist.Count > 0 ? 0 : -1;
    }

    /// <summary>运行时切换到指定视频文件（mpv 实例重新加载）。</summary>
    private void PlayVideo(string file)
    {
        foreach (var inst in _instances)
        {
            if (inst.Win is MpvWallpaperWindow mpv)
                mpv.LoadFile(file);
            else if (inst.FallbackVideo is not null)
                try { inst.FallbackVideo.Source = new Uri(file); inst.FallbackVideo.Play(); } catch { }
        }
    }

    /// <summary>下一个视频（壁纸 Tab「下一」按钮调用）。</summary>
    public void NextVideo()
    {
        if (_videoPlaylist.Count == 0) return;
        _videoIndex = (_videoIndex + 1) % _videoPlaylist.Count;
        PlayVideo(_videoPlaylist[_videoIndex]);
        Log?.Invoke($"[壁纸] 切换到下一个视频（{_videoIndex + 1}/{_videoPlaylist.Count}）");
    }

    /// <summary>上一个视频。</summary>
    public void PrevVideo()
    {
        if (_videoPlaylist.Count == 0) return;
        _videoIndex = (_videoIndex - 1 + _videoPlaylist.Count) % _videoPlaylist.Count;
        PlayVideo(_videoPlaylist[_videoIndex]);
        Log?.Invoke($"[壁纸] 切换到上一个视频（{_videoIndex + 1}/{_videoPlaylist.Count}）");
    }

    /// <summary>暂停/恢复视频（壁纸 Tab「暂停」按钮）。</summary>
    public void ToggleVideoPause() => SetVideoPaused(!_videoManuallyPaused);

    private bool _videoManuallyPaused;

    private void SetVideoPaused(bool paused)
    {
        _videoManuallyPaused = paused;
        ApplyVideoPauseState();
    }

    private void ApplyVideoPauseState()
    {
        bool paused = _videoManuallyPaused || _pausedByFullscreen;
        foreach (var inst in _instances)
        {
            if (inst.Win is MpvWallpaperWindow mpv)
                mpv.SetPaused(paused);
            else if (inst.FallbackVideo is not null)
                try { if (paused) inst.FallbackVideo.Pause(); else inst.FallbackVideo.Play(); } catch { }
        }
    }

    // ---- 全屏检测：前台全屏视频/游戏时自动暂停壁纸降载 ----

    private void StartFullscreenWatcherIfNeeded()
    {
        StopFullscreenWatcher();
        if (!_settings!.WallpaperPauseOnFullscreen) return;
        _fsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fsTimer.Tick += (_, _) => UpdateFullscreenPause();
        _fsTimer.Start();
    }

    private void StopFullscreenWatcher()
    {
        _fsTimer?.Stop();
        _fsTimer = null;
    }

    private void UpdateFullscreenPause()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            bool fs = false;
            if (fg != IntPtr.Zero && GetWindowRect(fg, out RECT r))
            {
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                var wa = SystemParameters.WorkArea;
                // 前台窗口没有 owner 且其面积 ≥ 95% 工作区 → 视为全屏
                if (w > 0 && h > 0 && GetWindow(fg, GW_OWNER) == IntPtr.Zero
                    && w * h >= wa.Width * wa.Height * 0.95)
                    fs = true;
            }
            if (fs != _pausedByFullscreen)
            {
                _pausedByFullscreen = fs;
                ApplyVideoPauseState();
                foreach (var inst in _instances)
                    if (inst.Particles is not null)
                        inst.Particles.Fps = fs ? 8 : (_settings!.IdleReduceFps ? 20 : 30);
                if (fs) Log?.Invoke("[壁纸] 检测到前台全屏，已暂停壁纸降载");
            }
        }
        catch { }
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
        string? file = CurrentVideoFile;
        if (file is not null)
            win.StartPlayback(file, Log, _settings!.StretchMode);
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
        // HTML 壁纸（默认关闭；WebView2 只读浏览本地 html，隐藏系统 UI）
        if (_settings!.HtmlWallpaperEnabled && TryResolveFile(_settings.HtmlWallpaperPath, out string html))
            return BuildHtmlContent(inst, html);

        return _settings.WallpaperKind switch
        {
            WallpaperKind.Image => BuildImageContent(inst),
            WallpaperKind.Video => BuildFallbackVideoContent(inst),
            _ => _settings.AnimationStyle == AnimationStyle.Particles
                ? BuildParticlesContent(inst)
                : BuildAuroraContent(inst),
        };
    }

    private UIElement BuildHtmlContent(WallpaperInstance inst, string htmlFile)
    {
        try
        {
            var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
            inst.HtmlView = web;
            _ = InitWebViewAsync(web, htmlFile);
            return web;
        }
        catch (Exception ex)
        {
            Log?.Invoke("[壁纸] WebView2 初始化失败，回退纯色：" + ex.Message);
            return new Grid { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black) };
        }
    }

    private async System.Threading.Tasks.Task InitWebViewAsync(WebView2 web, string htmlFile)
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DogDesk", "webview2");
            Directory.CreateDirectory(userData);
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userData, null);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.CoreWebView2.Navigate(new Uri(htmlFile).AbsoluteUri);
            Log?.Invoke("[壁纸] HTML 壁纸已加载：" + Path.GetFileName(htmlFile));
        }
        catch (Exception ex)
        {
            Log?.Invoke("[壁纸] HTML 壁纸加载失败：" + ex.Message);
        }
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
        string? vfile = CurrentVideoFile;
        if (vfile is not null)
            media.Source = new Uri(vfile);
        else if (TryResolveFile(_settings.WallpaperPath, out string single))
            media.Source = new Uri(single);
        media.MediaEnded += (_, _) =>
        {
            if (_settings!.WallpaperLoop)
            {
                media.Position = TimeSpan.Zero;
                media.Play();
            }
        };
        return media;
    }

    private UIElement BuildParticlesContent(WallpaperInstance inst)
    {
        var host = new Grid { Background = BuildSpaceGradient() };
        var ctrl = new ParticleFieldControl
        {
            Preset = _settings!.ParticlePreset ?? ParticleFieldControl.PresetStarfield,
            MouseInteraction = _settings.WallpaperMouseInteraction,
            Fps = _settings.IdleReduceFps ? 20 : 30,
        };
        inst.Particles = ctrl;
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
        StopFullscreenWatcher();
        _pausedByFullscreen = false;
        _videoManuallyPaused = false;
        _videoPlaylist = new List<string>();
        _videoIndex = -1;

        foreach (var inst in _instances)
        {
            inst.InlineBars?.Stop();
            inst.Particles?.Stop();
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

    private string DescribeSource()
    {
        if (_settings!.HtmlWallpaperEnabled && !string.IsNullOrWhiteSpace(_settings.HtmlWallpaperPath))
            return "HTML 网页壁纸（WebView2）";
        if (_settings.WallpaperKind == WallpaperKind.Video)
            return _settings.VideoPlaylist?.Count > 1 ? $"libmpv 视频轮播（{_settings.VideoPlaylist.Count} 个）" : "libmpv 视频（全格式+硬解）";
        if (_settings.WallpaperKind == WallpaperKind.Image)
            return _settings.SlideshowEnabled ? "图片文件夹轮播" : "图片";
        return _settings.AnimationStyle == AnimationStyle.Particles
            ? $"粒子场景预设（{_settings.ParticlePreset}）" : "极光动画";
    }

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
