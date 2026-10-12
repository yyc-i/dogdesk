namespace DogDesk.Mac.Services;

/// <summary>
/// macOS 侧媒体会话抽象（与 Windows 侧的 System.Windows 版本解耦）。
/// 由 <see cref="MpvAudioPlayer"/> 实现；UI（歌词/封面/进度）只依赖本接口。
/// </summary>
public interface IMediaSession
{
    string Title { get; }
    string Artist { get; }

    /// <summary>封面图原始字节（PNG/JPEG）；无封面为 null。</summary>
    byte[]? ThumbnailBytes { get; }

    bool IsPlaying { get; }
    double Progress01 { get; }
    double PositionSec { get; }
    double DurationSec { get; }

    /// <summary>任何属性变化时触发（已切回 UI 线程由订阅方决定）。</summary>
    event Action? Changed;

    void TogglePlayPause();
    void Seek(double fraction);
    void Next();
    void Previous();
}
