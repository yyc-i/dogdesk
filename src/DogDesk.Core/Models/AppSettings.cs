namespace DogDesk.Models;

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

/// <summary>音质档位（在线接口可用时生效）。</summary>
public enum AudioQuality { Standard, High, Ultra }

/// <summary>
/// 应用设置（JSON 持久化）。全部可自定义项集中于此，
/// 界面修改即保存；启动时由 <see cref="DogDesk.Services.SettingsService"/> 加载。
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

    // ---- Mineradio 类功能（v0.6）----
    /// <summary>3D 环绕音效总开关（extrastereo 宽度 + aphaser 旋转）。</summary>
    public bool SurroundEnabled { get; set; }
    /// <summary>立体声宽度（0 .. 6，extrastereo m）。</summary>
    public double SurroundWidth { get; set; } = 2.5;
    /// <summary>环绕速度（0.1 .. 2.0 Hz，aphaser speed）。</summary>
    public double SurroundSpeed { get; set; } = 0.5;
    /// <summary>节拍驱动视觉增强总开关（粒子爆发/白色闪烁/泛光脉冲/歌词振动）。</summary>
    public bool BeatVisualEnabled { get; set; } = true;
    /// <summary>倍速播放（0.5 .. 2.0，mpv speed）。</summary>
    public double PlaybackSpeed { get; set; } = 1.0;
    /// <summary>音质档位（标准/高/超清；在线接口支持时生效）。</summary>
    public AudioQuality QualityTier { get; set; } = AudioQuality.Standard;
    /// <summary>双行歌词（当前+下一行）。</summary>
    public bool LyricsDualLine { get; set; } = true;
    /// <summary>竖排歌词。</summary>
    public bool LyricsVertical { get; set; }
    /// <summary>歌词随节拍振动。</summary>
    public bool LyricsBeatVibrate { get; set; } = true;
    /// <summary>歌词垂直位置（0 .. 100，距底部百分比）。</summary>
    public double LyricsOffsetPercent { get; set; } = 50.0;
    /// <summary>主页/播放器面板不透明度（0.3 .. 1.0）。</summary>
    public double MainPanelOpacity { get; set; } = 0.85;
    /// <summary>毛玻璃/半透明效果。</summary>
    public bool GlassEffectEnabled { get; set; } = true;
    /// <summary>自定义视频背景路径（复用 libmpv 视频能力作背景层）。</summary>
    public string VideoBackgroundPath { get; set; } = "";
    /// <summary>退出时记忆播放位置，下次启动续播。</summary>
    public bool ResumePlayback { get; set; } = true;
    /// <summary>高帧率模式（频谱/动画 60fps，默认 30fps）。</summary>
    public bool HighFpsMode { get; set; }
    /// <summary>电台最近风格。</summary>
    public string RadioGenre { get; set; } = "流行";
    /// <summary>桌面歌词弹幕（默认关闭）。</summary>
    public bool DanmakuEnabled { get; set; }
    public double DanmakuSize { get; set; } = 20.0;
    public double DanmakuSpeed { get; set; } = 1.0;
    /// <summary>摄像头手势控制（默认关闭；仅本地处理不上传）。</summary>
    public bool CameraGestureEnabled { get; set; }
    /// <summary>城市天气氛围推歌（默认关闭；需位置权限）。</summary>
    public bool WeatherRecommendEnabled { get; set; }

    // ---- 通用 ----
    public string AccentColor { get; set; } = "#818CF8";          // 主题强调色
    public bool AutoStart { get; set; }                           // 开机自启（启动文件夹快捷方式，非侵入）
    public bool MinimizeToTray { get; set; } = true;              // 关闭时最小化到托盘
    public bool IdleReduceFps { get; set; } = true;               // 空闲降帧省电

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
