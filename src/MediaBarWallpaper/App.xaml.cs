using System.Windows;
using MediaBarWallpaper.Services;
using MediaBarWallpaper.Services.Mpv;

namespace MediaBarWallpaper;

public partial class App : System.Windows.Application
{
    /// <summary>全局媒体会话：SMTC 真实会话，无播放器时自动回退演示数据。</summary>
    public IMediaSession MediaSession { get; } = new SmtcMediaSession();

    /// <summary>全局唯一的壁纸引擎。</summary>
    public WallpaperEngine Wallpaper { get; } = new WallpaperEngine();

    /// <summary>系统音频频谱服务（WASAPI loopback），驱动壁纸律动。</summary>
    public AudioSpectrumService Spectrum { get; } = new AudioSpectrumService();

    /// <summary>歌词服务（网易云 / QQ音乐 / LRCLIB 多源检索）。</summary>
    public LyricsService Lyrics { get; } = new LyricsService();

    /// <summary>设置持久化服务（JSON）。</summary>
    public SettingsService Settings { get; } = new SettingsService();

    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // 预加载 libmpv 视频内核；缺失/失败不致命，视频壁纸自动回退 MediaElement
        MpvNativeLoader.TryPreload();

        var main = new MainWindow(MediaSession, Wallpaper, Spectrum, Lyrics, Settings);
        MainWindow = main;

        // 设置要求自启但快捷方式缺失时补建（非侵入：仅启动文件夹）
        if (Settings.Current.AutoStart && !AutoStartService.IsEnabled())
            AutoStartService.SetEnabled(true);

        // 系统托盘
        _tray = new TrayService();
        _tray.RestoreRequested += () => Dispatcher.BeginInvoke(() =>
        {
            main.Show();
            main.WindowState = WindowState.Normal;
            main.Activate();
        });
        _tray.ApplyWallpaperRequested += () => Dispatcher.BeginInvoke(main.ApplyCurrentSettings);
        _tray.StopWallpaperRequested += () => Dispatcher.BeginInvoke(main.StopWallpaperNow);
        _tray.ExitRequested += () => Dispatcher.BeginInvoke(() =>
        {
            main.ForceClose = true;
            Shutdown();
        });
        _tray.Show();

        main.Show();

        // 后台接入真实系统媒体会话（SMTC 不可用时保持演示数据，不阻塞启动）
        _ = ((SmtcMediaSession)MediaSession).InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Settings.Save();
        _tray?.Dispose();
        _tray = null;
        Spectrum.Dispose();
        Wallpaper.Dispose();
        base.OnExit(e);
    }
}
