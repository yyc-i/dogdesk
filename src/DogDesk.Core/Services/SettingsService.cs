using System.IO;
using System.Text.Json;
using DogDesk.Models;

namespace DogDesk.Services;

/// <summary>
/// 设置持久化：读写 %AppData%\DogDesk\settings.json。
/// 加载失败时使用默认设置（不崩溃）。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public AppSettings Current { get; private set; }

    public SettingsService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DogDesk");
        // v0.5.0 起由 MediaBarWallpaper 更名：首次启动自动迁移旧配置（不删除旧文件）。
        MigrateFromLegacy(dir);
        _path = Path.Combine(dir, "settings.json");
        Current = Load();
    }

    /// <summary>若新路径无配置且旧 %AppData%\MediaBarWallpaper\settings.json 存在，则一次性复制过来。</summary>
    private static void MigrateFromLegacy(string newDir)
    {
        try
        {
            string newPath = Path.Combine(newDir, "settings.json");
            if (File.Exists(newPath)) return;
            string oldDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MediaBarWallpaper"); // 旧目录名（仅迁移用）
            string oldPath = Path.Combine(oldDir, "settings.json");
            if (!File.Exists(oldPath)) return;
            Directory.CreateDirectory(newDir);
            File.Copy(oldPath, newPath, overwrite: false); // 复制、不删除旧文件
        }
        catch
        {
            // 迁移失败静默，回退默认配置
        }
    }

    /// <summary>立即把当前设置写入磁盘（目录不存在会自动创建）。</summary>
    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // 设置保存失败不应影响主流程
        }
    }

    public void Reset()
    {
        Current = new AppSettings();
        Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // 损坏的配置回退默认值
        }
        return new AppSettings();
    }
}
