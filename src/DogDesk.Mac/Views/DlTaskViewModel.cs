using DogDesk.Services.Downloads;

namespace DogDesk.Mac.Views;

/// <summary>下载任务列表项视图模型（包装 Core DownloadTask，提供展示字段）。</summary>
public sealed class DlTaskViewModel
{
    public DownloadTask Task { get; }
    public DlTaskViewModel(DownloadTask t) { Task = t; }

    public string Id => Task.Id;
    public string FileName => string.IsNullOrWhiteSpace(Task.FileName)
        ? (string.IsNullOrWhiteSpace(Task.Url) ? "(未命名)" : System.IO.Path.GetFileName(Task.Url) ?? Task.Url)
        : Task.FileName;
    public DownloadStatus Status => Task.Status;

    public double ProgressPercent => Task.Percent;

    public string SizeText
    {
        get
        {
            string dl = Human(Task.DownloadedBytes);
            string total = Task.TotalBytes > 0 ? Human(Task.TotalBytes) : "?";
            return $"{dl} / {total}";
        }
    }

    public string SpeedText => Task.Status == DownloadStatus.Downloading
        ? $"{Human((long)Task.SpeedBps)}/s" : "";

    public string ElapsedText => "";

    public string StatusText => Task.Status switch
    {
        DownloadStatus.Pending => "等待中",
        DownloadStatus.Downloading => "下载中",
        DownloadStatus.Paused => "已暂停",
        DownloadStatus.Completed => "已完成",
        DownloadStatus.Failed => "失败" + (string.IsNullOrEmpty(Task.Error) ? "" : $"：{Task.Error}"),
        DownloadStatus.Canceled => "已取消",
        _ => Task.Status.ToString(),
    };

    private static string Human(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} K";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.0} M";
        return $"{bytes / 1024.0 / 1024 / 1024:0.00} G";
    }
}
