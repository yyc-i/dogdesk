namespace MediaBarWallpaper.Models;

/// <summary>媒体条目来源类型。</summary>
public enum MediaSourceKind { LocalFile, Online, Navidrome }

/// <summary>
/// 播放队列中的单个媒体条目：本地文件 / 在线音源 / Navidrome 统一表示。
/// <see cref="Uri"/> 为本地绝对路径或可直接播放的 URL。
/// </summary>
public sealed class MediaItem
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string? Album { get; set; }
    public double DurationSec { get; set; }

    public MediaSourceKind Kind { get; set; }
    /// <summary>本地绝对路径，或可直接播放的 URL（在线/Navidrome）。</summary>
    public string Uri { get; set; } = "";
    /// <summary>封面图 URL（在线音源/Navidrome 提供；本地曲库可为 null 并回退文件封面）。</summary>
    public string? CoverUrl { get; set; }
    /// <summary>本地封面图绝对路径（曲库扫描时检测 cover.jpg/folder.jpg 等）。</summary>
    public string? LocalCoverPath { get; set; }
    /// <summary>来源名（"本地"/"网易云"/"Navidrome" 等），用于界面标注。</summary>
    public string Provider { get; set; } = "";

    public bool IsValid => !string.IsNullOrWhiteSpace(Uri) && !string.IsNullOrWhiteSpace(Title);

    public string DisplayName => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Title} - {Artist}";
}
