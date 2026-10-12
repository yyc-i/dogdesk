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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // 预加载 libmpv 视频内核；缺失/失败不致命，视频壁纸自动回退 MediaElement
        MpvNativeLoader.TryPreload();

        var main = new MainWindow(MediaSession, Wallpaper, Spectrum, Lyrics);
        MainWindow = main;
        main.Show();

        // 后台接入真实系统媒体会话（SMTC 不可用时保持演示数据，不阻塞启动）
        _ = ((SmtcMediaSession)MediaSession).InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Spectrum.Dispose();
        Wallpaper.Dispose();
        base.OnExit(e);
    }
}
