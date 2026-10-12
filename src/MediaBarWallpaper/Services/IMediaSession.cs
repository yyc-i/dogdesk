using System.ComponentModel;
using System.Windows.Media;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 媒体会话抽象。真实实现见 <see cref="SmtcMediaSession"/>（Windows 系统媒体会话 SMTC），
/// 无会话时内部回退演示数据；<see cref="MockMediaSession"/> 保留用于纯演示场景。
/// </summary>
public interface IMediaSession : INotifyPropertyChanged
{
    string Title { get; }
    string Artist { get; }

    /// <summary>封面图（SMTC 提供）；无封面时返回 null。</summary>
    ImageSource? Thumbnail { get; }

    bool IsPlaying { get; }
    double Progress01 { get; }

    /// <summary>当前播放位置（秒，相对曲目起点），用于歌词同步。</summary>
    double PositionSec { get; }

    /// <summary>曲目总时长（秒），用于歌词检索匹配。</summary>
    double DurationSec { get; }

    /// <summary>是否已接入真实系统媒体会话（false = 演示数据）。</summary>
    bool IsReal { get; }

    void TogglePlayPause();
    void Next();
    void Previous();

    /// <summary>跳转到曲目进度（0..1）。</summary>
    void Seek(double fraction);
}
