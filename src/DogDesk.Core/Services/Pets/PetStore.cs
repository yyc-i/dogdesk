using System.Text.Json;
using DogDesk.Models;

namespace DogDesk.Services.Pets;

/// <summary>
/// 桌宠持久化（v0.7.1）：%AppData%\DogDesk\pets\ 下每只桌宠一个 JSON 文件 + 自动备份
/// （&lt;name&gt;.json 与 &lt;name&gt;.backup.json）。记忆每只桌宠的位置/大小/表情/素材。
/// 兼容迁移：若 pets\ 目录为空且存在旧版 pets.json，则读取旧文件并迁移到新布局。
/// </summary>
public static class PetStore
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DogDesk");
    private static readonly string PetsDir = Path.Combine(BaseDir, "pets");
    private static readonly string LegacyPath = Path.Combine(BaseDir, "pets.json");

    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    /// <summary>文件名安全化（去非法字符，防路径穿越）。</summary>
    private static string SafeName(string name)
    {
        var invalids = Path.GetInvalidFileNameChars();
        string safe = new(name.Select(c => invalids.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "pet" : safe.Trim();
    }

    /// <summary>读取全部桌宠（扫描 pets\ 目录，跳过 *.backup.json）；目录不存在返回空列表。</summary>
    public static List<PetInfo> LoadAll()
    {
        var result = new List<PetInfo>();
        try
        {
            if (Directory.Exists(PetsDir))
            {
                foreach (var file in Directory.EnumerateFiles(PetsDir, "*.json"))
                {
                    if (file.EndsWith(".backup.json", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var pet = JsonSerializer.Deserialize<PetInfo>(File.ReadAllText(file), JsonOpt);
                        if (pet is not null && !string.IsNullOrWhiteSpace(pet.Name)) result.Add(pet);
                    }
                    catch { }
                }
                if (result.Count > 0) return result;
            }
            // 旧版 pets.json 一次性迁移
            if (File.Exists(LegacyPath))
            {
                var legacy = JsonSerializer.Deserialize<List<PetInfo>>(File.ReadAllText(LegacyPath), JsonOpt);
                if (legacy is not null)
                {
                    foreach (var pet in legacy)
                        if (!string.IsNullOrWhiteSpace(pet.Name)) Save(pet);
                    if (legacy.Count > 0) return legacy;
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>保存单只桌宠（独立文件 + 备份）。</summary>
    public static void Save(PetInfo pet)
    {
        if (pet is null || string.IsNullOrWhiteSpace(pet.Name)) return;
        try
        {
            Directory.CreateDirectory(PetsDir);
            string safe = SafeName(pet.Name);
            string main = Path.Combine(PetsDir, safe + ".json");
            string backup = Path.Combine(PetsDir, safe + ".backup.json");
            if (File.Exists(main))
                try { File.Copy(main, backup, overwrite: true); } catch { }
            File.WriteAllText(main, JsonSerializer.Serialize(pet, JsonOpt));
        }
        catch { }
    }

    /// <summary>删除桌宠（连同备份）。</summary>
    public static void Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            string safe = SafeName(name);
            foreach (var p in new[] { Path.Combine(PetsDir, safe + ".json"), Path.Combine(PetsDir, safe + ".backup.json") })
                if (File.Exists(p)) File.Delete(p);
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
