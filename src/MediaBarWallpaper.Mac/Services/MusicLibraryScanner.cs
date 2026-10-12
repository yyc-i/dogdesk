using MediaBarWallpaper.Models;

namespace MediaBarWallpaper.Mac.Services;

/// <summary>
/// 本地曲库递归扫描器：按扩展名过滤音轨，从文件名解析「Title - Artist」，
/// 并探测同目录封面（cover.jpg / folder.jpg / cover.png / folder.png）。
/// 时长不依赖 taglib（降级项，DurationSec 置 0），mpv 加载后会回写真实时长。
/// </summary>
public static class MusicLibraryScanner
{
    private static readonly string[] AudioExtensions =
        { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aac" };

    private static readonly string[] CoverNames =
        { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "AlbumArt.jpg" };

    public static List<MediaItem> Scan(string root)
    {
        var items = new List<MediaItem>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return items;

        foreach (string file in SafeEnumerateFiles(root))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (!AudioExtensions.Contains(ext)) continue;

            string fileName = Path.GetFileNameWithoutExtension(file);
            (string title, string artist) = ParseName(fileName);

            items.Add(new MediaItem
            {
                Title = title,
                Artist = artist,
                Kind = MediaSourceKind.LocalFile,
                Uri = file,
                LocalCoverPath = DetectCover(Path.GetDirectoryName(file)!),
                Provider = "本地",
            });
        }
        return items;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> subDirs = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(dir);
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch { /* 无权限目录跳过 */ }

            foreach (string f in files) yield return f;
            foreach (string d in subDirs) pending.Push(d);
        }
    }

    /// <summary>文件名按首个「 - 」拆分为标题与艺人。</summary>
    private static (string Title, string Artist) ParseName(string fileName)
    {
        int idx = fileName.IndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0)
        {
            string title = fileName[..idx].Trim();
            string artist = fileName[(idx + 3)..].Trim();
            if (title.Length > 0) return (title, artist);
        }
        return (fileName.Trim(), "");
    }

    private static string? DetectCover(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        foreach (string name in CoverNames)
        {
            string path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
