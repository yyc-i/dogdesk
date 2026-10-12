using System.IO;
using System.Text.Json;
using MediaBarWallpaper.Models;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 设置持久化：读写 %AppData%\MediaBarWallpaper\settings.json。
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
            "MediaBarWallpaper");
        _path = Path.Combine(dir, "settings.json");
        Current = Load();
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
