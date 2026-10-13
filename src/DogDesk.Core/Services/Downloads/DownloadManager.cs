using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DogDesk.Services.Downloads;

public enum DownloadStatus { Pending, Downloading, Paused, Completed, Failed, Canceled }

/// <summary>下载任务（aria2 多线程分段下载；仅限用户拥有权限的合法链接）。</summary>
public sealed class DownloadTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string SaveDir { get; set; } = "";
    public string FileName { get; set; } = "";
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public double SpeedBps { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
    public string Error { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsValidUrl =>
        Uri.TryCreate(Url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    [JsonIgnore] public double Percent => TotalBytes > 0 ? Math.Clamp(DownloadedBytes * 100.0 / TotalBytes, 0, 100) : 0;
}

/// <summary>
/// 下载管理器：以独立进程方式调用 aria2c（GPL-2.0 独立程序，不修改其源码、不链接进本项目二进制）。
/// 多线程分段（-x16 -s16）、断点续传（--continue）、并发/限速可配；任务持久化 %AppData%\DogDesk\downloads.json + 备份。
/// 合规：不实现任何网盘绕过/破解/盗链，仅下载用户拥有权限的合法链接。
/// </summary>
public sealed class DownloadManager : IDisposable
{
    public static readonly string[] Aria2Candidates =
    {
        "aria2c",
        Path.Combine(AppContext.BaseDirectory, "aria2", "aria2c"),
        Path.Combine(AppContext.BaseDirectory, "aria2", "aria2c.exe"),
        "/opt/homebrew/bin/aria2c",
        "/usr/local/bin/aria2c",
        "/usr/bin/aria2c",
    };

    private readonly List<DownloadTask> _tasks = new();
    private readonly Dictionary<string, Process> _procs = new();
    private readonly object _lock = new();
    private readonly string _aria2;

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DogDesk");
    private static readonly string MainPath = Path.Combine(DataDir, "downloads.json");
    private static readonly string BackupPath = Path.Combine(DataDir, "downloads.backup.json");

    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };
    private static readonly Regex ProgressRx = new(
        @"\[#[^\]]+\s+([\d.]+)([KMG])?iB/([\d.]+)([KMG])?iB\((\d+)%\)[^\]]*DL:([\d.]+)([KMG])?iB",
        RegexOptions.Compiled);

    public int MaxConcurrent { get; set; } = 3;
    /// <summary>全局限速 KB/s，0=不限。</summary>
    public double MaxSpeedKb { get; set; }

    public event Action<DownloadTask>? TaskChanged;

    public DownloadManager(string? aria2Path = null)
    {
        _aria2 = string.IsNullOrWhiteSpace(aria2Path) ? (FindAria2() ?? "") : aria2Path;
    }

    public static string? FindAria2()
    {
        foreach (var c in Aria2Candidates)
        {
            try { if (File.Exists(c)) return c; } catch { }
        }
        return null; // 让调用方提示安装/随包分发
    }

    public bool Available => _aria2 is not null && File.Exists(_aria2);
    public string? Aria2Path => _aria2;

    public IReadOnlyList<DownloadTask> Tasks { get { lock (_lock) return _tasks.ToList(); } }

    public DownloadTask? Get(string id) { lock (_lock) return _tasks.FirstOrDefault(t => t.Id == id); }

    /// <summary>新建任务并入队（超出并发数则 Pending 等待）。</summary>
    public DownloadTask Start(string url, string saveDir, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(saveDir)) saveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var task = new DownloadTask { Url = url.Trim(), SaveDir = saveDir, FileName = fileName ?? "" };
        lock (_lock) _tasks.Insert(0, task);
        TaskChanged?.Invoke(task);
        Pump();
        return task;
    }

    public void Pause(string id) { var t = Get(id); if (t is null) return; Kill(t.Id); lock (_lock) { t.Status = DownloadStatus.Paused; } TaskChanged?.Invoke(t); }

    public void Resume(string id) { var t = Get(id); if (t is null) return; lock (_lock) { t.Status = DownloadStatus.Pending; } TaskChanged?.Invoke(t); Pump(); }

    public void Cancel(string id) { var t = Get(id); if (t is null) return; Kill(t.Id); lock (_lock) { t.Status = DownloadStatus.Canceled; } TaskChanged?.Invoke(t); }

    public void Retry(string id)
    {
        var t = Get(id); if (t is null) return;
        lock (_lock) { t.Status = DownloadStatus.Pending; t.Error = ""; t.DownloadedBytes = 0; }
        TaskChanged?.Invoke(t);
        Pump();
    }

    public void Remove(string id)
    {
        var t = Get(id); if (t is null) return;
        Kill(t.Id);
        lock (_lock) _tasks.RemoveAll(x => x.Id == id);
        TaskChanged?.Invoke(t);
        Save();
    }

    private void Pump()
    {
        lock (_lock)
        {
            int running = _tasks.Count(t => t.Status == DownloadStatus.Downloading);
            foreach (var t in _tasks.Where(t => t.Status == DownloadStatus.Pending).ToList())
            {
                if (running >= MaxConcurrent) break;
                running++;
                Spawn(t);
            }
        }
    }

    private void Spawn(DownloadTask t)
    {
        if (!t.IsValidUrl || !Available) { t.Status = DownloadStatus.Failed; t.Error = "aria2 不可用或链接非法"; TaskChanged?.Invoke(t); return; }
        try
        {
            Directory.CreateDirectory(t.SaveDir);
            var psi = new ProcessStartInfo
            {
                FileName = _aria2,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--continue=true");
            psi.ArgumentList.Add("-x16");
            psi.ArgumentList.Add("-s16");
            psi.ArgumentList.Add("--summary-interval=1");
            psi.ArgumentList.Add("--console-log-level=warn");
            psi.ArgumentList.Add("--dir=" + t.SaveDir);
            if (!string.IsNullOrWhiteSpace(t.FileName)) psi.ArgumentList.Add("--out=" + t.FileName);
            if (MaxSpeedKb > 0) psi.ArgumentList.Add($"--max-overall-download-limit={MaxSpeedKb:0}K");
            psi.ArgumentList.Add(t.Url);

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            t.Status = DownloadStatus.Downloading;
            p.OutputDataReceived += (_, e) => OnProgress(t, e.Data);
            p.ErrorDataReceived += (_, e) => OnProgress(t, e.Data);
            p.Exited += (_, _) => OnExited(t);
            lock (_lock) _procs[t.Id] = p;
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            TaskChanged?.Invoke(t);
        }
        catch (Exception ex)
        {
            t.Status = DownloadStatus.Failed; t.Error = ex.Message; TaskChanged?.Invoke(t);
        }
    }

    private void OnProgress(DownloadTask t, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var m = ProgressRx.Match(line);
        if (!m.Success) return;
        try
        {
            double done = ParseSize(m.Groups[1].Value, m.Groups[2].Value);
            double total = ParseSize(m.Groups[3].Value, m.Groups[4].Value);
            double dl = ParseSize(m.Groups[6].Value, m.Groups[7].Value);
            lock (_lock)
            {
                t.DownloadedBytes = (long)done;
                if (total > 0) t.TotalBytes = (long)total;
                t.SpeedBps = dl;
            }
            TaskChanged?.Invoke(t);
        }
        catch { }
    }

    private void OnExited(DownloadTask t)
    {
        lock (_lock) _procs.Remove(t.Id);
        bool completed = t.TotalBytes > 0 && t.DownloadedBytes >= t.TotalBytes - 1024;
        lock (_lock)
        {
            if (t.Status == DownloadStatus.Downloading)
                t.Status = completed ? DownloadStatus.Completed : DownloadStatus.Failed;
        }
        TaskChanged?.Invoke(t);
        Save();
        Pump();
    }

    private void Kill(string id)
    {
        Process? p;
        lock (_lock) { _procs.TryGetValue(id, out p); _procs.Remove(id); }
        try { if (p is not null && !p.HasExited) p.Kill(true); } catch { }
        try { p?.Dispose(); } catch { }
    }

    private static double ParseSize(string num, string unit)
    {
        double v = double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        return unit.ToUpperInvariant() switch
        {
            "G" => v * 1024 * 1024 * 1024,
            "M" => v * 1024 * 1024,
            "K" => v * 1024,
            _ => v,
        };
    }

    // ---- 持久化 ----
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            if (File.Exists(MainPath)) try { File.Copy(MainPath, BackupPath, overwrite: true); } catch { }
            var snapshot = Tasks.Where(t => t.Status is DownloadStatus.Pending or DownloadStatus.Paused
                or DownloadStatus.Downloading or DownloadStatus.Failed).ToList();
            File.WriteAllText(MainPath, JsonSerializer.Serialize(snapshot, JsonOpt));
        }
        catch { }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(MainPath)) return;
            var list = JsonSerializer.Deserialize<List<DownloadTask>>(File.ReadAllText(MainPath), JsonOpt);
            if (list is null) return;
            lock (_lock)
            {
                foreach (var t in list.Where(t => t.IsValidUrl))
                {
                    if (t.Status is DownloadStatus.Downloading) t.Status = DownloadStatus.Paused;
                    _tasks.Add(t);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Process[] all;
        lock (_lock) { all = _procs.Values.ToArray(); _procs.Clear(); }
        foreach (var p in all)
            try { if (!p.HasExited) p.Kill(true); p.Dispose(); } catch { }
    }
}
