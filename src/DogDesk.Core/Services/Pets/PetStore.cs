using System.Text.Json;
using DogDesk.Models;

namespace DogDesk.Services.Pets;

/// <summary>
/// 桌宠持久化：%AppData%\DogDesk\pets.json（含备份 pets.backup.json）。
/// 记忆每只桌宠的位置/大小/表情；首次启动确保存在一只内置桌宠（喵酱）。
/// </summary>
public static class PetStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DogDesk");
    private static readonly string MainPath = Path.Combine(Dir, "pets.json");
    private static readonly string BackupPath = Path.Combine(Dir, "pets.backup.json");

    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    public static List<PetInfo> Load()
    {
        try
        {
            if (File.Exists(MainPath))
            {
                var list = JsonSerializer.Deserialize<List<PetInfo>>(File.ReadAllText(MainPath), JsonOpt);
                if (list is not null) return list;
            }
        }
        catch { }
        return new List<PetInfo>();
    }

    public static void Save(List<PetInfo> pets)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(MainPath))
                try { File.Copy(MainPath, BackupPath, overwrite: true); } catch { }
            File.WriteAllText(MainPath, JsonSerializer.Serialize(pets ?? new List<PetInfo>(), JsonOpt));
        }
        catch { }
    }

    /// <summary>取出列表并确保至少有一只内置桌宠（不落盘，由调用方 Save）。</summary>
    public static List<PetInfo> EnsureDefault(List<PetInfo>? pets)
    {
        pets ??= new List<PetInfo>();
        if (pets.Count == 0)
            pets.Add(new PetInfo { Name = "喵酱", ModelType = "Builtin", Expression = "idle" });
        return pets;
    }
}
