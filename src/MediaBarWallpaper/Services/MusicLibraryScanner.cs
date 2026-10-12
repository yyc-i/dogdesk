using System.IO;
using MediaBarWallpaper.Models;
using NAudio.Wave;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 本地曲库扫描（v0.5）：递归扫描目录下的音频文件，
/// 文件名解析「标题 - 艺人」，目录探测 cover.jpg/folder.jpg/封面.jpg 作为封面，
/// 用 MediaFoundationReader 读取时长（失败降级为 0，不影响列表）。
/// 纯逻辑、不感知 UI。
/// </summary>
public static class MusicLibraryScanner
{
    private static readonly string[] AudioExt = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aac" };
    private static readonly string[] CoverNames =
        { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "album.jpg", "封面.jpg", "封面.png", "front.jpg" };

    public static bool IsAudioFile(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Length > 0 && Array.Exists(AudioExt, e => ext.Equals(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>递归扫描整个曲库目录（可能较慢，建议在线程池调用）。</summary>
    public static List<MediaItem> Scan(string rootFolder, Action<string>? log = null)
    {
        var result = new List<MediaItem>();
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            return result;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootFolder, "*.*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[曲库] 扫描失败：{ex.Message}");
            return result;
        }

        string? lastDir = null;
        string? coverForDir = null;

        foreach (string file in files)
        {
            if (!IsAudioFile(file)) continue;
            try
            {
                string dir = Path.GetDirectoryName(file) ?? "";
                if (dir != lastDir)
                {
                    lastDir = dir;
                    coverForDir = FindCover(dir);
                }

                (string title, string artist) = ParseName(Path.GetFileNameWithoutExtension(file));
                result.Add(new MediaItem
                {
                    Title = title,
                    Artist = artist,
                    DurationSec = TryReadDuration(file),
                    Kind = MediaSourceKind.LocalFile,
                    Uri = Path.GetFullPath(file),
                    LocalCoverPath = coverForDir,
                    Provider = "本地",
                });
            }
            catch { /* 单个文件失败跳过 */ }
        }

        log?.Invoke($"[曲库] 扫描完成：{result.Count} 首（{rootFolder}）");
        return result;
    }

    /// <summary>解析「标题 - 艺人」；容忍 "01. " / "01 - " 音轨号前缀。</summary>
    private static (string Title, string Artist) ParseName(string name)
    {
        // 去掉前导音轨号："01 Title" / "01. Title" / "01 - Title"
        if (name.Length >= 3 && char.IsDigit(name[0]) && char.IsDigit(name[1]))
        {
            int i = 2;
            while (i < name.Length && (name[i] == '.' || name[i] == ' ' || name[i] == '-')) i++;
            name = i < name.Length ? name[i..] : name;
        }

        int sep = name.IndexOf(" - ", StringComparison.Ordinal);
        if (sep <= 0) return (name.Trim(), "");
        return (name[..sep].Trim(), name[(sep + 3)..].Trim());
    }

    private static string? FindCover(string dir)
    {
        try
        {
            foreach (var n in CoverNames)
            {
                string p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
            // 任意目录内图片兜底（第一张 jpg/png）
            foreach (var f in Directory.EnumerateFiles(dir, "*.jpg")) return f;
            foreach (var f in Directory.EnumerateFiles(dir, "*.png")) return f;
        }
        catch { }
        return null;
    }

    /// <summary>用 MediaFoundation 读时长（秒）；失败返回 0。</summary>
    private static double TryReadDuration(string path)
    {
        try
        {
            using var reader = new MediaFoundationReader(path);
            return reader.TotalTime.TotalSeconds;
        }
        catch { return 0; }
    }
}
