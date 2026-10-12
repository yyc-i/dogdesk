using System.Windows;
using MediaBarWallpaper.Services;
using MediaBarWallpaper.Services.Mpv;
using MediaBarWallpaper.Services.MusicSource;
using MediaBarWallpaper.Services.Playback;

namespace MediaBarWallpaper;

public partial class App : System.Windows.Application
{
    /// <summary>系统 SMTC 真实媒体会话（无播放器时回退演示数据）。</summary>
    public SmtcMediaSession SystemMedia { get; } = new();

    /// <summary>内置 libmpv 音频播放内核（本地曲库/在线/Navidrome）。</summary>
    public MpvAudioPlayer BuiltinPlayer { get; }

    /// <summary>会话路由：媒体条/歌词/全屏歌词统一观察此会话，内置与 SMTC 自动切换。</summary>
    public IMediaSession MediaSession { get; }

    /// <summary>共享播放队列（内置会话驱动）。</summary>
    public PlaybackQueue Queue { get; } = new();

    /// <summary>在线音源（网易云，第三方非官方）。</summary>
    public NeteaseMusicSource Netease { get; } = new();

    /// <summary>Navidrome（自建 Subsonic）客户端。</summary>
    public NavidromeClient Navidrome { get; } = new();

    /// <summary>全局唯一的壁纸引擎。</summary>
    public WallpaperEngine Wallpaper { get; } = new WallpaperEngine();

    /// <summary>系统音频频谱服务（WASAPI loopback），驱动壁纸律动。</summary>
    public AudioSpectrumService Spectrum { get; } = new AudioSpectrumService();

    /// <summary>歌词服务（网易云 / QQ音乐 / LRCLIB 多源检索）。</summary>
    public LyricsService Lyrics { get; } = new LyricsService();

    /// <summary>设置持久化服务（JSON）。</summary>
    public SettingsService Settings { get; } = new SettingsService();

    private TrayService? _tray;

    public App()
    {
        BuiltinPlayer = new MpvAudioPlayer();
        MediaSession = new MediaSessionRouter(SystemMedia, BuiltinPlayer);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // 预加载 libmpv 内核；缺失/失败不致命（视频壁纸回退 MediaElement，内置音频整体降级）
        MpvNativeLoader.TryPreload();

        var main = new MainWindow(MediaSession, SystemMedia, BuiltinPlayer, Queue, Netease, Navidrome,
            Wallpaper, Spectrum, Lyrics, Settings);
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
        _ = SystemMedia.InitializeAsync();

        main.OnAppStarted();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Settings.Save();
        _tray?.Dispose();
        _tray = null;
        Spectrum.Dispose();
        Wallpaper.Dispose();
        BuiltinPlayer.Dispose();
        base.OnExit(e);
    }
}
