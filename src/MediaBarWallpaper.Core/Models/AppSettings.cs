namespace MediaBarWallpaper.Models;

/// <summary>壁纸内容类型。</summary>
public enum WallpaperKind { Animated, Image, Video }

/// <summary>图片/视频填充模式。</summary>
public enum StretchMode { Cover, Contain, Stretch }

/// <summary>内置动画样式。</summary>
public enum AnimationStyle { Aurora, Particles }

/// <summary>频谱可视化样式。</summary>
public enum SpectrumStyle { Bars, Ring, Wave }

/// <summary>全屏沉浸式歌词动画样式（Folia 风格自研复刻）。</summary>
public enum LyricsAnimationStyle { Flow, Name, Chorus }

/// <summary>
/// 应用设置（JSON 持久化）。全部可自定义项集中于此，
/// 界面修改即保存；启动时由 <see cref="MediaBarWallpaper.Services.SettingsService"/> 加载。
/// </summary>
public sealed class AppSettings
{
    // ---- 壁纸 ----
    public WallpaperKind WallpaperKind { get; set; } = WallpaperKind.Animated;
    public string WallpaperPath { get; set; } = "";
    public string WallpaperFolder { get; set; } = "";
    public StretchMode StretchMode { get; set; } = StretchMode.Cover;
    public double Brightness { get; set; } = 1.0;                 // 0.3 .. 1.0（1=原亮度）
    public AnimationStyle AnimationStyle { get; set; } = AnimationStyle.Aurora;
    public bool SlideshowEnabled { get; set; }
    public double SlideshowIntervalSec { get; set; } = 15.0;

    // ---- 频谱 ----
    public bool SpectrumEnabled { get; set; }
    public SpectrumStyle SpectrumStyle { get; set; } = SpectrumStyle.Bars;
    public double SpectrumSensitivity { get; set; } = 1.0;        // 0.5 .. 3.0
    public double SpectrumSmoothing { get; set; } = 0.72;         // 0.4 .. 0.95 下降系数

    // ---- 任务栏媒体条 ----
    public bool MediaBarEnabled { get; set; }
    public int MediaBarDock { get; set; } = 1;                    // 0=左 1=中 2=右
    public double MediaBarWidth { get; set; } = 360.0;            // 240 .. 520
    public bool MediaBarAutoHide { get; set; } = true;            // 未播放自动淡出
    public bool MediaBarCoverGradient { get; set; } = true;       // 封面主色玻璃感渐变

    // ---- 桌面歌词 ----
    public bool LyricsEnabled { get; set; }
    public double LyricsFontSize { get; set; } = 22.0;            // 14 .. 34
    public double LyricsOpacity { get; set; } = 1.0;              // 0.3 .. 1.0
    public bool LyricsKaraoke { get; set; } = true;               // 逐字（音节级）高亮

    // ---- Folia 类功能（v0.5）----
    /// <summary>全屏沉浸式歌词动画开关。</summary>
    public bool FullscreenLyricsEnabled { get; set; }
    /// <summary>全屏歌词动画样式（流光/浮名/群唱）。</summary>
    public LyricsAnimationStyle LyricsAnimationStyle { get; set; } = LyricsAnimationStyle.Flow;
    /// <summary>全屏歌词背景遮罩不透明度（0.4 .. 1.0）。</summary>
    public double FullscreenLyricsOpacity { get; set; } = 0.92;
    /// <summary>本地曲库根目录（扫描 mp3/flac/wav/m4a/ogg/aac）。</summary>
    public string MusicLibraryFolder { get; set; } = "";
    /// <summary>均衡器总开关。</summary>
    public bool EqualizerEnabled { get; set; }
    /// <summary>10 段均衡器增益（dB，-12 .. +12，31Hz..16kHz）。</summary>
    public double[] EqualizerGains { get; set; } = new double[10];
    /// <summary>封面动态配色主题（扩展 CoverPalette 主色为渐变主题）。</summary>
    public bool DynamicThemeEnabled { get; set; } = true;
    /// <summary>Navidrome 服务器地址（Subsonic API）。</summary>
    public string NavidromeServer { get; set; } = "";
    public string NavidromeUser { get; set; } = "";
    /// <summary>Navidrome 密码：仅明文保存在本机 settings.json（不加密，README 已说明）。</summary>
    public string NavidromePassword { get; set; } = "";

    // ---- 通用 ----
    public string AccentColor { get; set; } = "#818CF8";          // 主题强调色
    public bool AutoStart { get; set; }                           // 开机自启（启动文件夹快捷方式，非侵入）
    public bool MinimizeToTray { get; set; } = true;              // 关闭时最小化到托盘
    public bool IdleReduceFps { get; set; } = true;               // 空闲降帧省电

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
