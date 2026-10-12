using System.Text.Json;
using DogDesk.Models;

namespace DogDesk.Services.Playlists;

/// <summary>本地自建歌单（JSON 持久化，跨平台共享）。</summary>
public sealed class DogDeskPlaylist
{
    public string Name { get; set; } = "";
    public List<MediaItem> Items { get; set; } = new();
}

/// <summary>
/// 本地歌单持久化：%AppData%\DogDesk\playlists.json（含备份 playlists.backup.json）。
/// 「我的收藏」作为固定歌单由 UI 保证存在。数据仅存本机。
/// </summary>
public static class PlaylistStore
{
    public const string FavoritesName = "我的收藏";

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DogDesk");
    private static readonly string MainPath = Path.Combine(Dir, "playlists.json");
    private static readonly string BackupPath = Path.Combine(Dir, "playlists.backup.json");

    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    public static List<DogDeskPlaylist> Load()
    {
        try
        {
            if (File.Exists(MainPath))
            {
                var list = JsonSerializer.Deserialize<List<DogDeskPlaylist>>(File.ReadAllText(MainPath), JsonOpt);
                if (list is not null) return list;
            }
        }
        catch { }
        return new List<DogDeskPlaylist>();
    }

    public static void Save(List<DogDeskPlaylist> playlists)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(MainPath))
                try { File.Copy(MainPath, BackupPath, overwrite: true); } catch { }
            File.WriteAllText(MainPath, JsonSerializer.Serialize(playlists ?? new List<DogDeskPlaylist>(), JsonOpt));
        }
        catch { }
    }

    /// <summary>取出列表并确保「我的收藏」存在（不落盘，由调用方 Save）。</summary>
    public static List<DogDeskPlaylist> EnsureFavorites(List<DogDeskPlaylist>? list)
    {
        list ??= new List<DogDeskPlaylist>();
        if (list.All(p => p.Name != FavoritesName))
            list.Insert(0, new DogDeskPlaylist { Name = FavoritesName });
        return list;
    }
}
