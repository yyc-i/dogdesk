using System.Text.Json;

namespace DogDesk.Services.BootAnim;

/// <summary>开机动画素材库条目。</summary>
public sealed class BootAnimEntry
{
    public string Name { get; set; } = "";
    /// <summary>Image / Video。</summary>
    public string Kind { get; set; } = "Image";
    /// <summary>素材绝对路径（存于 %AppData%\DogDesk\boot-anim\ 下）。</summary>
    public string Path { get; set; } = "";
}

/// <summary>
/// 开机动画素材库：%AppData%\DogDesk\boot-anim\（素材文件 + library.json 索引）。
/// 内置动画（极光/粒子/流光文字/纯色 Logo）为代码生成，不占素材库；
/// 素材库仅存用户导入/ AI 生成的图片与视频。
/// </summary>
public static class BootAnimStore
{
    public static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DogDesk", "boot-anim");
    private static readonly string IndexPath = Path.Combine(RootDir, "library.json");

    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    public static List<BootAnimEntry> Load()
    {
        try
        {
            if (File.Exists(IndexPath))
            {
                var list = JsonSerializer.Deserialize<List<BootAnimEntry>>(File.ReadAllText(IndexPath), JsonOpt);
                if (list is not null) return list;
            }
        }
        catch { }
        return new List<BootAnimEntry>();
    }

    public static void Save(List<BootAnimEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(RootDir);
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(entries ?? new List<BootAnimEntry>(), JsonOpt));
        }
        catch { }
    }
}
