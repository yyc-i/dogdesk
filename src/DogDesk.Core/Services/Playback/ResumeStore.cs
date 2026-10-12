using System.Text.Json;
using DogDesk.Models;

namespace DogDesk.Services.Playback;

/// <summary>记忆播放数据：队列 + 当前索引 + 播放位置（秒）。</summary>
public sealed class ResumeData
{
    public List<MediaItem> Queue { get; set; } = new();
    public int CurrentIndex { get; set; } = -1;
    public double PositionSec { get; set; }
    public bool IsValid => CurrentIndex >= 0 && CurrentIndex < Queue.Count;
}

/// <summary>
/// 记忆播放位置持久化：退出时保存曲目队列与位置，下次启动续播。
/// 文件 %AppData%\DogDesk\resume.json；<see cref="ResumeData.Queue"/> 为轻量快照（无封面字节）。
/// </summary>
public static class ResumeStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DogDesk");
    private static readonly string MainPath = Path.Combine(Dir, "resume.json");

    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    public static void Save(ResumeData data)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(MainPath, JsonSerializer.Serialize(data, JsonOpt));
        }
        catch { }
    }

    public static ResumeData? Load()
    {
        try
        {
            if (!File.Exists(MainPath)) return null;
            var d = JsonSerializer.Deserialize<ResumeData>(File.ReadAllText(MainPath), JsonOpt);
            return d is { IsValid: true } ? d : null;
        }
        catch { return null; }
    }

    public static void Clear()
    {
        try { if (File.Exists(MainPath)) File.Delete(MainPath); } catch { }
    }
}
