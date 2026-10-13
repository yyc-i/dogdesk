using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DogDesk.Models;

namespace DogDesk.Services.Download;

/// <summary>
/// 下载管理（v0.8.0）：自研任务队列 + aria2 进程/JSON-RPC 集成。
/// 不内嵌 aria2 源码，仅以子进程方式调用本机 aria2c 二进制，通过 127.0.0.1 的
/// JSON-RPC 端口控制多线程分段下载。平台无关（Windows / macOS 双侧共享）。
/// </summary>
public sealed class DownloadManager : IDisposable
{
    private readonly SettingsService _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly object _lock = new();
    private readonly Dictionary<string, DownloadTask> _gidMap = new();
    private readonly string _aria2Path;
    private readonly string _aria2Version = "";
    private readonly string _rpcSecret;
    private readonly string _defaultDownloadDir;

    private Process? _daemon;
    private StreamWriter? _logWriter;
    private int _rpcPort = 6800;
    private CancellationTokenSource? _pollCts;
    private Timer? _persistTimer;
    private bool _disposed;

    public DownloadManager(SettingsService settings)
    {
        _settings = settings;
        _rpcSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        _aria2Path = DetectAria2();
        if (Aria2Available) _aria2Version = DetectVersion();
        _defaultDownloadDir = DetectDefaultDownloadDir();
        LoadHistory();
    }

    // ================= 公共状态 =================

    public ObservableCollection<DownloadTask> Tasks { get; } = new();

    public bool Aria2Available => !string.IsNullOrEmpty(_aria2Path);

    public string Aria2Path => _aria2Path;

    public string Aria2StatusText
    {
        get
        {
            if (Aria2Available)
                return string.IsNullOrEmpty(_aria2Version)
                    ? "aria2 内核：已就绪"
                    : $"aria2 内核：已就绪（{_aria2Version}）";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "aria2 内核：未就绪 — 未找到 aria2c，请将 aria2c.exe 放入程序目录 aria2\\ 文件夹或加入 PATH";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "aria2 内核：未就绪 — 未找到 aria2c，请执行 brew install aria2";
            return "aria2 内核：未就绪 — 未找到 aria2c，请安装 aria2（如 sudo apt install aria2 或 brew install aria2）或加入 PATH";
        }
    }

    public bool IsDaemonRunning
    {
        get { try { return _daemon != null && !_daemon.HasExited; } catch { return false; } }
    }

    public string DefaultDownloadDir => _defaultDownloadDir;

    public event Action<DownloadTask>? TaskCompleted;

    public event Action? StateChanged;

    // ================= 生命周期 =================

    /// <summary>幂等初始化：加载历史后，把重启前未完成的 Waiting 任务重新提交给 aria2，并启动 1s 轮询。UI 启动时调用一次。</summary>
    public async Task InitializeAsync()
    {
        if (!Aria2Available) { StateChanged?.Invoke(); return; }
        bool hasWaiting;
        lock (_lock) { hasWaiting = Tasks.Any(t => t.Status == DownloadStatus.Waiting); }
        if (hasWaiting)
        {
            try
            {
                await EnsureDaemonAsync();
                foreach (DownloadTask t in Tasks.ToList())
                {
                    if (t.Status != DownloadStatus.Waiting) continue;
                    try
                    {
                        string gid = await RpcAddUriAsync(t);
                        SetGid(t, gid);
                    }
                    catch { /* 单任务重提失败不影响其余任务 */ }
                }
                RequestPersist();
                StartPolling();
            }
            catch { /* daemon 不可用时静默，用户手动加任务时会收到明确报错 */ }
        }
        StateChanged?.Invoke();
    }

    public void Shutdown()
    {
        if (_disposed) return;
        try { _pollCts?.Cancel(); } catch { }
        FlushPersist();
        KillDaemon();
    }

    public void Dispose()
    {
        Shutdown();
        _disposed = true;
    }

    // ================= 任务操作 =================

    public async Task AddTaskAsync(string url, string? saveDir = null, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
             !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("仅支持 http/https 直链", nameof(url));
        if (!Aria2Available)
            throw new InvalidOperationException(Aria2StatusText);

        // ---- 同步阶段：集合增删必须发生在调用线程（UI 线程），不得 await ----
        string name = string.IsNullOrWhiteSpace(fileName) ? GuessFileName(url) : fileName!;
        string dir = string.IsNullOrWhiteSpace(saveDir)
            ? (string.IsNullOrWhiteSpace(_settings.Current.DownloadDir) ? DefaultDownloadDir : _settings.Current.DownloadDir)
            : saveDir!;
        Directory.CreateDirectory(dir);

        var task = new DownloadTask(Guid.NewGuid().ToString("N"), url, dir, name, DateTime.Now);
        Tasks.Add(task);
        task.UpdateStatus(DownloadStatus.Waiting);
        RequestPersist();
        StateChanged?.Invoke();
        // ---- 同步阶段结束 ----

        try
        {
            await EnsureDaemonAsync();
            string gid = await RpcAddUriAsync(task);
            SetGid(task, gid);
            RequestPersist();
            StartPolling();
        }
        catch (Exception ex)
        {
            task.UpdateStatus(DownloadStatus.Failed, ex.Message, DateTime.Now);
            RequestPersist();
            StateChanged?.Invoke();
            throw;
        }
    }

    public async Task PauseAsync(DownloadTask task)
    {
        if (task.Status == DownloadStatus.Completed || task.Status == DownloadStatus.Failed) return;
        if (task.Status != DownloadStatus.Paused)
            task.UpdateStatus(DownloadStatus.Paused, finishedAt: DateTime.Now);
        StateChanged?.Invoke();
        RequestPersist();
        if (IsDaemonRunning && !string.IsNullOrEmpty(task.AriaGid))
        {
            try { await RpcCallAsync("aria2.pause", task.AriaGid); } catch { }
        }
    }

    public async Task ResumeAsync(DownloadTask task)
    {
        if (task.Status == DownloadStatus.Completed) return;
        task.UpdateStatus(DownloadStatus.Waiting, finishedAt: null);
        StateChanged?.Invoke();
        try
        {
            if (!IsDaemonRunning)
            {
                // daemon 已空闲退出：重新拉起并 addUri（--continue=true 会读取 .aria2 控制文件续传）
                await EnsureDaemonAsync();
                string gid = await RpcAddUriAsync(task);
                SetGid(task, gid);
            }
            else if (!string.IsNullOrEmpty(task.AriaGid))
            {
                try { await RpcCallAsync("aria2.unpause", task.AriaGid); }
                catch
                {
                    // gid 已失效（如 daemon 重启过）→ 重新提交
                    string gid = await RpcAddUriAsync(task);
                    SetGid(task, gid);
                }
            }
            else
            {
                string gid = await RpcAddUriAsync(task);
                SetGid(task, gid);
            }
            RequestPersist();
            StartPolling();
        }
        catch (Exception ex)
        {
            task.UpdateStatus(DownloadStatus.Failed, ex.Message, DateTime.Now);
            StateChanged?.Invoke();
        }
    }

    public async Task CancelAsync(DownloadTask task)
    {
        if (task.Status == DownloadStatus.Completed || task.Status == DownloadStatus.Failed) return;
        if (IsDaemonRunning && !string.IsNullOrEmpty(task.AriaGid))
        {
            try { await RpcCallAsync("aria2.remove", task.AriaGid); } catch { }
        }
        task.UpdateStatus(DownloadStatus.Failed, "用户取消", DateTime.Now);
        StateChanged?.Invoke();
        RequestPersist();
    }

    public async Task RetryAsync(DownloadTask task)
    {
        if (task.Status != DownloadStatus.Failed) return;
        if (IsDaemonRunning && !string.IsNullOrEmpty(task.AriaGid))
        {
            try { await RpcCallAsync("aria2.remove", task.AriaGid); } catch { }
        }
        // 保留 DownloadedLength，配合 --continue=true 续传
        task.Error = null;
        task.FinishedAt = null;
        task.UpdateStatus(DownloadStatus.Waiting);
        StateChanged?.Invoke();
        try
        {
            await EnsureDaemonAsync();
            string gid = await RpcAddUriAsync(task);
            SetGid(task, gid);
            RequestPersist();
            StartPolling();
        }
        catch (Exception ex)
        {
            task.UpdateStatus(DownloadStatus.Failed, ex.Message, DateTime.Now);
            StateChanged?.Invoke();
        }
    }

    public void OpenContainingFolder(DownloadTask task)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string arg = File.Exists(task.FullPath)
                    ? $"/select,\"{task.FullPath}\""
                    : $"\"{task.SaveDir}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = false, CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string target = File.Exists(task.FullPath) ? task.FullPath : task.SaveDir;
                Process.Start("open", $"-R \"{target}\"");
            }
        }
        catch { /* 打开失败静默 */ }
    }

    public async Task SetConcurrencyAsync(int maxConcurrent)
    {
        int v = Math.Clamp(maxConcurrent, 1, 16);
        _settings.Current.DownloadMaxConcurrent = v;
        _settings.Save();
        if (IsDaemonRunning)
        {
            try
            {
                await RpcCallAsync("aria2.changeGlobalOption",
                    new Dictionary<string, string> { ["max-concurrent-downloads"] = v.ToString() });
            }
            catch { }
        }
        StateChanged?.Invoke();
    }

    public Task SetGlobalLimitAsync(long speedKib)
    {
        long v = Math.Max(0, speedKib);
        _settings.Current.DownloadMaxSpeedKib = v;
        _settings.Save();
        if (IsDaemonRunning)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RpcCallAsync("aria2.changeGlobalOption",
                        new Dictionary<string, string> { ["max-overall-download-limit"] = SpeedToAriaString(v) });
                }
                catch { }
            });
        }
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    // ================= aria2c 探测 =================

    private static string DetectAria2()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string local = Path.Combine(AppContext.BaseDirectory, "aria2", "aria2c.exe");
            if (File.Exists(local)) return local;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (File.Exists("/opt/homebrew/bin/aria2c")) return "/opt/homebrew/bin/aria2c";
            if (File.Exists("/usr/local/bin/aria2c")) return "/usr/local/bin/aria2c";
        }
        // PATH 顺序查找（Linux 构建/运行环境也走这里）
        try
        {
            string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "aria2c.exe" : "aria2c";
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string p = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
        }
        catch { }
        return "";
    }

    private string DetectVersion()
    {
        try
        {
            var psi = new ProcessStartInfo(_aria2Path, "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            Task<string?> read = proc.StandardOutput.ReadLineAsync();
            if (!proc.WaitForExit(2000))
            {
                try { proc.Kill(); } catch { }
                return "";
            }
            string? line = read.Result;
            var m = System.Text.RegularExpressions.Regex.Match(line ?? "", @"(\d+\.\d+\.\d+)");
            return m.Success ? m.Groups[1].Value : "";
        }
        catch { return ""; }
    }

    private static string DetectDefaultDownloadDir()
    {
        try
        {
            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads)) return downloads;
        }
        catch { }
        try { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { return "/"; }
    }

    // ================= daemon 进程管理 =================

    private string RpcUrl => $"http://127.0.0.1:{_rpcPort}/jsonrpc";

    private static int FindFreePort()
    {
        for (int p = 6800; p <= 6900; p++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, p);
                listener.Start();
                listener.Stop();
                return p;
            }
            catch { }
        }
        return 6800;
    }

    private async Task EnsureDaemonAsync()
    {
        if (IsDaemonRunning && await TestRpcReadyAsync()) return;
        StartDaemon();
        await WaitReadyAsync(5000);
    }

    private async Task WaitReadyAsync(int timeoutMs)
    {
        int ticks = timeoutMs / 200;
        for (int i = 0; i < ticks; i++)
        {
            await Task.Delay(200);
            if (await TestRpcReadyAsync()) return;
            if (_daemon == null || _daemon.HasExited)
                throw new InvalidOperationException("aria2 进程启动失败");
        }
        throw new InvalidOperationException("aria2 RPC 端口就绪超时");
    }

    private async Task<bool> TestRpcReadyAsync()
    {
        try { await RpcOnceAsync("aria2.getVersion"); return true; }
        catch { return false; }
    }

    private void StartDaemon()
    {
        KillDaemon();
        _rpcPort = FindFreePort();

        string dir = PersistDir;
        Directory.CreateDirectory(dir);
        try
        {
            _logWriter?.Dispose();
            _logWriter = new StreamWriter(
                new FileStream(Path.Combine(dir, "aria2.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            { AutoFlush = true };
        }
        catch { _logWriter = null; }

        var psi = new ProcessStartInfo(_aria2Path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--enable-rpc");
        psi.ArgumentList.Add($"--rpc-listen-port={_rpcPort}");
        psi.ArgumentList.Add("--rpc-listen-all=false");
        psi.ArgumentList.Add($"--dir={DefaultDownloadDir}");
        psi.ArgumentList.Add($"--max-concurrent-downloads={_settings.Current.DownloadMaxConcurrent}");
        long limitKib = _settings.Current.DownloadMaxSpeedKib;
        if (limitKib > 0)
            psi.ArgumentList.Add($"--max-overall-download-limit={SpeedToAriaString(limitKib)}");
        psi.ArgumentList.Add("--continue=true");
        psi.ArgumentList.Add("--auto-file-renaming=false");
        psi.ArgumentList.Add("--allow-overwrite=false");
        psi.ArgumentList.Add("--split=16");
        psi.ArgumentList.Add("--max-connection-per-server=16");
        psi.ArgumentList.Add("--min-split-size=1M");
        psi.ArgumentList.Add("--file-allocation=none");
        psi.ArgumentList.Add("--quiet=true");
        psi.ArgumentList.Add("--summary-interval=0");
        psi.ArgumentList.Add("--console-log-level=error");
        psi.ArgumentList.Add($"--rpc-secret={_rpcSecret}");

        _daemon = Process.Start(psi)!;
        _daemon.OutputDataReceived += (_, e) => { if (e.Data != null) { try { _logWriter?.WriteLine(e.Data); } catch { } } };
        _daemon.ErrorDataReceived += (_, e) => { if (e.Data != null) { try { _logWriter?.WriteLine(e.Data); } catch { } } };
        _daemon.BeginOutputReadLine();
        _daemon.BeginErrorReadLine();
    }

    private void KillDaemon()
    {
        try
        {
            if (_daemon != null && !_daemon.HasExited)
            {
                try { _daemon.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }
        finally
        {
            try { _daemon?.Dispose(); } catch { }
            _daemon = null;
            try { _logWriter?.Dispose(); } catch { }
            _logWriter = null;
        }
    }

    internal static string SpeedToAriaString(long kib)
    {
        if (kib >= 1024) return $"{(long)Math.Round(kib / 1024.0)} M";
        return $"{kib} K";
    }

    // ================= JSON-RPC =================

    private async Task<JsonElement> RpcCallAsync(string method, params object?[] args)
    {
        try
        {
            return await RpcOnceAsync(method, args);
        }
        catch
        {
            // daemon 可能已退出：重启一次后重试
            if (Aria2Available && !IsDaemonRunning)
            {
                try
                {
                    StartDaemon();
                    await WaitReadyAsync(5000);
                    return await RpcOnceAsync(method, args);
                }
                catch { }
            }
            throw;
        }
    }

    private async Task<JsonElement> RpcOnceAsync(string method, params object?[] args)
    {
        object[] all = new object[args.Length + 1];
        all[0] = "token:" + _rpcSecret;
        for (int i = 0; i < args.Length; i++) all[i + 1] = args[i]!;
        var payload = new { jsonrpc = "2.0", id = "dogdesk", method, @params = all };
        string json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(RpcUrl, content);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
        {
            string msg = err.TryGetProperty("message", out var m) ? (m.GetString() ?? err.ToString()) : err.ToString();
            throw new InvalidOperationException($"aria2 RPC 错误: {msg}");
        }
        return doc.RootElement.GetProperty("result");
    }

    private async Task<string> RpcAddUriAsync(DownloadTask task)
    {
        var opts = new Dictionary<string, string>
        {
            ["out"] = task.FileName,
            ["dir"] = task.SaveDir,
        };
        JsonElement result = await RpcCallAsync("aria2.addUri", new object?[] { new[] { task.Url }, opts });
        return result.GetString() ?? "";
    }

    private void SetGid(DownloadTask task, string gid)
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(task.AriaGid)) _gidMap.Remove(task.AriaGid);
            task.AriaGid = gid;
            if (!string.IsNullOrEmpty(gid)) _gidMap[gid] = task;
        }
    }

    // ================= 轮询循环 =================

    private void StartPolling()
    {
        lock (_lock)
        {
            if (_pollCts != null) return;
            _pollCts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_pollCts.Token));
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
                try { await PollOnceAsync(); }
                catch { }

                bool hasActive;
                lock (_lock)
                {
                    hasActive = Tasks.Any(t => t.Status == DownloadStatus.Waiting
                                            || t.Status == DownloadStatus.Downloading);
                }
                if (!hasActive)
                {
                    // 空闲无驻留：无 Waiting/Downloading（含全部终态或 Paused）→ 停轮询、杀 daemon
                    FlushPersist();
                    KillDaemon();
                    StateChanged?.Invoke();
                    break;
                }
            }
        }
        finally
        {
            lock (_lock) { _pollCts = null; }
        }
    }

    private async Task PollOnceAsync()
    {
        JsonElement active = await RpcCallAsync("aria2.tellActive");
        JsonElement waiting = await RpcCallAsync("aria2.tellWaiting", 0, 32);
        JsonElement stopped = await RpcCallAsync("aria2.tellStopped", 0, 32);

        foreach (JsonElement e in active.EnumerateArray()) ApplyEntry(e, stopped: false);
        foreach (JsonElement e in waiting.EnumerateArray()) ApplyEntry(e, stopped: false);
        foreach (JsonElement e in stopped.EnumerateArray()) ApplyEntry(e, stopped: true);
    }

    private void ApplyEntry(JsonElement e, bool stopped)
    {
        string gid = e.TryGetProperty("gid", out var g) ? (g.GetString() ?? "") : "";
        DownloadTask? task;
        lock (_lock) { _gidMap.TryGetValue(gid, out task); }
        if (task == null) return;

        string st = e.TryGetProperty("status", out var s) ? (s.GetString() ?? "") : "";
        long dl = GetLong(e, "completedLength");
        long total = GetLong(e, "totalLength");
        long speed = GetLong(e, "downloadSpeed");
        bool wasTerminal = task.Status == DownloadStatus.Completed || task.Status == DownloadStatus.Failed;

        if (!stopped)
        {
            task.UpdateProgress(dl, total, speed);
            DownloadStatus local = st switch
            {
                "waiting" => DownloadStatus.Waiting,
                "paused" => DownloadStatus.Paused,
                _ => DownloadStatus.Downloading,
            };
            if (task.Status != local)
                task.UpdateStatus(local, finishedAt: local == DownloadStatus.Paused ? DateTime.Now : null);
        }
        else
        {
            if (st == "error")
            {
                string? msg = e.TryGetProperty("errorMessage", out var em) ? em.GetString() : null;
                task.UpdateProgress(dl, total, 0);
                task.UpdateStatus(DownloadStatus.Failed,
                    string.IsNullOrWhiteSpace(msg) ? "下载失败" : msg, DateTime.Now);
            }
            else
            {
                // 完成：以本地落盘文件为准（用户改名后 FullPath 已变化）
                task.UpdateProgress(dl > 0 ? dl : total, total, 0);
                if (File.Exists(task.FullPath))
                    task.UpdateStatus(DownloadStatus.Completed, finishedAt: DateTime.Now);
                else
                    task.UpdateStatus(DownloadStatus.Failed, "文件未找到", DateTime.Now);
            }
            RequestPersist();
        }

        if (!wasTerminal && task.Status == DownloadStatus.Completed)
            TaskCompleted?.Invoke(task);
        StateChanged?.Invoke();
    }

    private static long GetLong(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long l) ? l : 0;

    // ================= 持久化 =================

    private static string PersistDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DogDesk", "downloads");

    private static string GuessFileName(string url)
    {
        try
        {
            var u = new Uri(url);
            string seg = Uri.UnescapeDataString(u.Segments.LastOrDefault()?.TrimEnd('/') ?? "");
            if (!string.IsNullOrWhiteSpace(seg)) return seg;
        }
        catch { }
        return $"download_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
    }

    private sealed class TaskDto
    {
        public string Id { get; set; } = "";
        public string Url { get; set; } = "";
        public string FileName { get; set; } = "";
        public string SaveDir { get; set; } = "";
        public string Status { get; set; } = "";
        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public long TotalLength { get; set; }
        public long DownloadedLength { get; set; }
    }

    private void LoadHistory()
    {
        try
        {
            string path = Path.Combine(PersistDir, "tasks.json");
            if (!File.Exists(path)) return;
            var list = JsonSerializer.Deserialize<List<TaskDto>>(File.ReadAllText(path));
            if (list == null) return;
            foreach (TaskDto d in list)
            {
                if (string.IsNullOrEmpty(d.Id) || string.IsNullOrEmpty(d.Url)) continue;
                DownloadStatus status = Enum.TryParse<DownloadStatus>(d.Status, out var s) ? s : DownloadStatus.Waiting;
                if (status == DownloadStatus.Downloading) status = DownloadStatus.Waiting; // 重启后统一为 Waiting，由 InitializeAsync 重新提交
                var t = new DownloadTask(d.Id, d.Url, d.SaveDir, d.FileName, d.CreatedAt)
                {
                    TotalLength = d.TotalLength,
                    DownloadedLength = d.DownloadedLength,
                    Error = d.Error,
                    FinishedAt = d.FinishedAt,
                };
                t.UpdateStatus(status);
                Tasks.Add(t);
            }
        }
        catch { /* 加载失败静默回退空列表 */ }
    }

    private void RequestPersist()
    {
        try
        {
            _persistTimer ??= new Timer(_ => FlushPersist(), null, Timeout.Infinite, Timeout.Infinite);
            _persistTimer.Change(1000, Timeout.Infinite);
        }
        catch { }
    }

    private void FlushPersist()
    {
        try { _persistTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        try
        {
            string dir = PersistDir;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "tasks.json");
            if (File.Exists(path))
            {
                try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
            }
            var list = Tasks.Select(t => new TaskDto
            {
                Id = t.Id,
                Url = t.Url,
                FileName = t.FileName,
                SaveDir = t.SaveDir,
                Status = t.Status.ToString(),
                Error = t.Error,
                CreatedAt = t.CreatedAt,
                FinishedAt = t.FinishedAt,
                TotalLength = t.TotalLength,
                DownloadedLength = t.DownloadedLength,
            }).ToList();
            File.WriteAllText(path, JsonSerializer.Serialize(list,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
