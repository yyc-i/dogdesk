namespace MediaBarWallpaper.Models;

/// <summary>壁纸内容类型。</summary>
public enum WallpaperKind { Animated, Image, Video }

/// <summary>图片/视频填充模式。</summary>
public enum StretchMode { Cover, Contain, Stretch }

/// <summary>内置动画样式。</summary>
public enum AnimationStyle { Aurora, Particles }

/// <summary>频谱可视化样式。</summary>
public enum SpectrumStyle { Bars, Ring, Wave }

/// <summary>
/// 应用设置（JSON 持久化）。全部可自定义项集中于此，
/// 界面修改即保存；启动时由 <see cref="Services.SettingsService"/> 加载。
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

    // ---- 通用 ----
    public string AccentColor { get; set; } = "#818CF8";          // 主题强调色
    public bool AutoStart { get; set; }                           // 开机自启（启动文件夹快捷方式，非侵入）
    public bool MinimizeToTray { get; set; } = true;              // 关闭时最小化到托盘
    public bool IdleReduceFps { get; set; } = true;               // 空闲降帧省电

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
