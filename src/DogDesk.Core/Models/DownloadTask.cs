using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace DogDesk.Models;

/// <summary>下载任务状态机。</summary>
public enum DownloadStatus { Waiting, Downloading, Paused, Completed, Failed }

/// <summary>
/// 下载任务（INPC 模型，供 WPF/Avalonia 绑定）。
/// 只读属性在构造后固定；可变属性由 DownloadManager 通过 internal setter 更新；
/// 计算属性每次取值现算，依赖字段变化时统一触发 PropertyChanged 刷新 UI。
/// </summary>
public sealed class DownloadTask : INotifyPropertyChanged
{
    private string _fileName;
    private long _totalLength;
    private long _downloadedLength;
    private double _speedBps;
    private DownloadStatus _status;
    private string? _error;
    private DateTime? _finishedAt;
    private string? _ariaGid;

    internal DownloadTask(string id, string url, string saveDir, string fileName, DateTime createdAt)
    {
        Id = id;
        Url = url;
        SaveDir = saveDir;
        _fileName = fileName;
        CreatedAt = createdAt;
    }

    // ---- 只读属性 ----
    public string Id { get; }
    public string Url { get; }
    public string SaveDir { get; }
    public DateTime CreatedAt { get; }
    public string FullPath => Path.Combine(SaveDir, FileName);

    // ---- 可变属性 ----
    /// <summary>文件名（允许用户改名；改名后 FullPath 与后续落盘校验随之变化）。</summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName == value) return;
            _fileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FullPath));
            NotifyDerived();
        }
    }

    /// <summary>总字节数（-1 = 未知）。</summary>
    public long TotalLength
    {
        get => _totalLength;
        internal set { if (_totalLength == value) return; _totalLength = value; OnPropertyChanged(); NotifyDerived(); }
    }

    public long DownloadedLength
    {
        get => _downloadedLength;
        internal set { if (_downloadedLength == value) return; _downloadedLength = value; OnPropertyChanged(); NotifyDerived(); }
    }

    /// <summary>瞬时速度（字节/秒）。</summary>
    public double SpeedBps
    {
        get => _speedBps;
        internal set { if (_speedBps == value) return; _speedBps = value; OnPropertyChanged(); NotifyDerived(); }
    }

    public DownloadStatus Status
    {
        get => _status;
        internal set { if (_status == value) return; _status = value; OnPropertyChanged(); NotifyDerived(); }
    }

    public string? Error
    {
        get => _error;
        internal set { if (_error == value) return; _error = value; OnPropertyChanged(); }
    }

    public DateTime? FinishedAt
    {
        get => _finishedAt;
        internal set { if (_finishedAt == value) return; _finishedAt = value; OnPropertyChanged(); NotifyDerived(); }
    }

    /// <summary>aria2 RPC gid（不持久化，重启后重新 addUri 获得新 gid）。</summary>
    public string? AriaGid
    {
        get => _ariaGid;
        internal set { _ariaGid = value; }
    }

    // ---- 计算属性（UI 显示用，现算） ----
    /// <summary>0..100；TotalLength&gt;0 时 = Downloaded/Total*100，否则 0。</summary>
    public double ProgressPercent =>
        TotalLength > 0 ? Math.Min(100.0, DownloadedLength * 100.0 / TotalLength) : 0;

    public string ProgressText =>
        TotalLength > 0 ? $"{ProgressPercent:0.#}%" : (DownloadedLength > 0 ? FormatSize(DownloadedLength) : "—");

    /// <summary>如 "2.4 MB/s"；静止时为空串。</summary>
    public string SpeedText => SpeedBps > 0 ? $"{FormatSize((long)SpeedBps)}/s" : "";

    /// <summary>如 "12.3 MB / 45.0 MB"；总大小未知时仅显示已下载。</summary>
    public string SizeText =>
        TotalLength > 0 ? $"{FormatSize(DownloadedLength)} / {FormatSize(TotalLength)}" : FormatSize(DownloadedLength);

    /// <summary>自 CreatedAt 到 FinishedAt 或当前时刻，HH:mm:ss。</summary>
    public string ElapsedText
    {
        get
        {
            var span = (FinishedAt ?? DateTime.Now) - CreatedAt;
            return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }
    }

    public string StatusText => Status switch
    {
        DownloadStatus.Waiting => "等待中",
        DownloadStatus.Downloading => "下载中",
        DownloadStatus.Paused => "已暂停",
        DownloadStatus.Completed => "已完成",
        _ => "失败",
    };

    // ---- 内部更新辅助（DownloadManager 轮询/事件时调用） ----
    internal void UpdateProgress(long downloaded, long total, double speedBps)
    {
        bool changed = false;
        if (_downloadedLength != downloaded) { _downloadedLength = downloaded; OnPropertyChanged(nameof(DownloadedLength)); changed = true; }
        if (_totalLength != total) { _totalLength = total; OnPropertyChanged(nameof(TotalLength)); changed = true; }
        if ((long)_speedBps != (long)speedBps) { _speedBps = speedBps; OnPropertyChanged(nameof(SpeedBps)); changed = true; }
        if (changed) NotifyDerived();
    }

    internal void UpdateStatus(DownloadStatus status, string? error = null, DateTime? finishedAt = null)
    {
        if (error != null) Error = error;
        FinishedAt = finishedAt;
        Status = status;
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(StatusText));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1048576.0:0.#} MB";
        return $"{bytes / 1073741824.0:0.##} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
