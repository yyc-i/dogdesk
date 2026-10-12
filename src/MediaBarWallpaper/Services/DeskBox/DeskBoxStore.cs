using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MediaBarWallpaper.Services.DeskBox;

/// <summary>桌面格子项：对原文件/文件夹的链接（不移动原位）。</summary>
public sealed class DeskBoxTile
{
    public string Name { get; set; } = "";
    /// <summary>原文件/文件夹绝对路径（双击打开）。</summary>
    public string Path { get; set; } = "";
    public string Color { get; set; } = "#818CF8";
    public bool Folded { get; set; }
}

public sealed class DeskBoxTodo
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

/// <summary>单屏布局（按屏幕矩形 key 记录）。</summary>
public sealed class ScreenLayout
{
    public List<DeskBoxTile> Tiles { get; set; } = new();
}

/// <summary>DeskBox 全量数据（独立 JSON，不改 Core AppSettings）。</summary>
public sealed class DeskBoxData
{
    /// <summary>key = 屏幕矩形 "x,y,w,h"。</summary>
    public Dictionary<string, ScreenLayout> Screens { get; set; } = new();
    public string Note { get; set; } = "";
    public List<DeskBoxTodo> Todos { get; set; } = new();

    public bool ShowTodo { get; set; } = true;
    public bool ShowNote { get; set; }
    public bool ShowMusic { get; set; } = true;
}

/// <summary>
/// DeskBox 持久化：%AppData%\MediaBarWallpaper\deskbox.json，
/// 保存时同时备份为 deskbox.backup.json（定时/退出快照）。
/// </summary>
public static class DeskBoxStore
{
    private static readonly string Dir =
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "MediaBarWallpaper");
    private static readonly string PathMain = Path.Combine(Dir, "deskbox.json");
    private static readonly string BackupPath = Path.Combine(Dir, "deskbox.backup.json");

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static DeskBoxData Load()
    {
        try
        {
            if (File.Exists(PathMain))
                return JsonSerializer.Deserialize<DeskBoxData>(File.ReadAllText(PathMain)) ?? new DeskBoxData();
        }
        catch { }
        return new DeskBoxData();
    }

    public static void Save(DeskBoxData data)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            // 先备份上一版，再写新数据
            if (File.Exists(PathMain))
                try { File.Copy(PathMain, BackupPath, overwrite: true); } catch { }
            File.WriteAllText(PathMain, JsonSerializer.Serialize(data, Opt));
        }
        catch { }
    }

    public static void Reset()
    {
        try
        {
            if (File.Exists(PathMain)) File.Delete(PathMain);
        }
        catch { }
    }
}
