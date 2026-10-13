using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DogDesk.Models;
using DogDesk.Services.Download;

namespace DogDesk.Mac.Views;

/// <summary>
/// 下载管理 Tab（v0.8）：仅负责 UI 接线，下载内核由 <see cref="DownloadManager"/>（DogDesk.Core）实现。
/// 所有用户操作均经 <see cref="AppServices.Downloads"/> 转发；异常一律吞掉并在页内 Toast 提示。
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _clipboardTimer = new();
    private readonly DispatcherTimer _downloadToastTimer = new();
    private string? _pendingClipUrl;
    private string? _lastClipboardText;

    /// <summary>并发档位与设置值的映射（索引即 ComboBox SelectedIndex）。</summary>
    private static readonly int[] ConcurrencyOptions = { 1, 2, 3, 4, 6, 8, 16 };
    /// <summary>限速档位（KiB/s），与 DownloadLimitBox 顺序一致。</summary>
    private static readonly long[] LimitOptions = { 0, 512, 1024, 2048, 5120, 10240, 20480, 51200 };

    // ============ 初始化 ============
    private void InitDownloadTab()
    {
        // —— 设置回填（抑制事件，避免触发保存）——
        var s = _app.Settings.Current;
        _suppressEvents = true;
        int ci = Array.IndexOf(ConcurrencyOptions, s.DownloadMaxConcurrent);
        DownloadConcurrencyBox.SelectedIndex = ci < 0 ? 2 : ci; // 默认 3
        int li = Array.IndexOf(LimitOptions, s.DownloadMaxSpeedKib);
        DownloadLimitBox.SelectedIndex = li < 0 ? 0 : li; // 默认不限
        ClipWatchSwitch.IsChecked = s.DownloadClipboardWatch;
        NotifySwitch.IsChecked = s.DownloadNotifyComplete;
        DownloadDirBox.Text = s.DownloadDir;
        _suppressEvents = false;

        // —— 任务列表与内核状态 ——
        DownloadList.ItemsSource = _app.Downloads.Tasks;
        RefreshDownloadUi();

        _app.Downloads.StateChanged += () => Dispatcher.UIThread.Post(RefreshDownloadUi);
        _app.Downloads.TaskCompleted += task =>
            Dispatcher.UIThread.Post(() => OnDownloadTaskCompleted(task));
        _app.Downloads.Tasks.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RefreshDownloadUi);
        ClipboardLinkWatcher.LinkDetected += url =>
            Dispatcher.UIThread.Post(() => ShowClipBanner(url));

        // —— 剪贴板轮询（1s）——
        _clipboardTimer.Interval = TimeSpan.FromSeconds(1);
        _clipboardTimer.Tick += async (_, _) => await PollClipboardAsync();
        if (ClipWatchSwitch.IsChecked == true) _clipboardTimer.Start();
        Closed += (_, _) => _clipboardTimer.Stop();

        // —— Toast 3s 自动隐藏 ——
        _downloadToastTimer.Interval = TimeSpan.FromSeconds(3);
        _downloadToastTimer.Tick += (_, _) =>
        {
            _downloadToastTimer.Stop();
            DownloadToast.IsVisible = false;
        };

        // —— 异步初始化 aria2 内核 ——
        Aria2StatusText.Text = "正在检测 aria2 内核…";
        CopyBrewBtn.IsVisible = true;
        _ = InitDownloadCoreAsync();
    }

    private async Task InitDownloadCoreAsync()
    {
        try
        {
            await _app.Downloads.InitializeAsync();
        }
        catch (Exception ex)
        {
            Aria2StatusText.Text = $"aria2 初始化异常：{ex.Message}";
            return;
        }
        finally
        {
            // 无论成败，都把内核状态文案与「复制安装命令」按钮对齐到真实状态
            Aria2StatusText.Text = _app.Downloads.Aria2StatusText;
            CopyBrewBtn.IsVisible = !_app.Downloads.Aria2Available;
        }
        RefreshDownloadUi();
    }

    // ============ 状态刷新 ============
    private void RefreshDownloadUi()
    {
        var dl = _app.Downloads;
        int active = 0;
        double totalBps = 0;
        foreach (var t in dl.Tasks)
        {
            if (t.Status == DownloadStatus.Downloading)
            {
                active++;
                totalBps += t.SpeedBps;
            }
        }
        DownloadStatusText.Text =
            $"共 {dl.Tasks.Count} 个任务 · {active} 个下载中 · 总速度 {FormatSpeed(totalBps)}" +
            (dl.Aria2Available ? "" : " · aria2 未就绪，任务将排队等待");
        DownloadEmptyText.IsVisible = dl.Tasks.Count == 0;
    }

    private static string FormatSpeed(double bps)
    {
        if (bps < 1024) return $"{bps:0} B/s";
        if (bps < 1024.0 * 1024) return $"{bps / 1024:0.0} KiB/s";
        return $"{bps / 1024.0 / 1024:0.0} MiB/s";
    }

    private void OnDownloadTaskCompleted(DownloadTask task)
    {
        RefreshDownloadUi();
        if (NotifySwitch.IsChecked == true)
            ShowDownloadToast($"下载完成：{task.FileName}");
    }

    private void ShowDownloadToast(string text)
    {
        DownloadToastText.Text = text;
        DownloadToast.IsVisible = true;
        _downloadToastTimer.Stop();
        _downloadToastTimer.Start();
    }

    // ============ 添加下载 ============
    private async void DownloadAdd_Click(object? sender, RoutedEventArgs e) =>
        await AddDownloadFromUrl(DownloadUrlBox.Text?.Trim());

    private void DownloadUrl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = AddDownloadFromUrl(DownloadUrlBox.Text?.Trim());
        }
    }

    private async Task AddDownloadFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowDownloadToast("请输入有效的下载链接");
            return;
        }
        string? dir = string.IsNullOrWhiteSpace(DownloadDirBox.Text) ? null : DownloadDirBox.Text.Trim();
        string? name = string.IsNullOrWhiteSpace(DownloadNameBox.Text) ? null : DownloadNameBox.Text.Trim();
        try
        {
            await _app.Downloads.AddTaskAsync(url, dir, name);
            DownloadUrlBox.Text = "";
            DownloadNameBox.Text = "";
            RefreshDownloadUi();
        }
        catch (ArgumentException ex)
        {
            ShowDownloadToast($"添加失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            ShowDownloadToast($"添加异常：{ex.Message}");
        }
    }

    private async void DownloadBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var dir = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择下载保存目录",
            AllowMultiple = false,
        });
        if (dir.Count > 0)
            DownloadDirBox.Text = dir[0].Path.LocalPath;
    }

    // ============ 任务操作按钮 ============
    private static DownloadTask? TaskFromSender(object? sender) =>
        (sender as Control)?.DataContext as DownloadTask;

    private async void TaskPause_Click(object? sender, RoutedEventArgs e)
    {
        if (TaskFromSender(sender) is { } t)
            try { await _app.Downloads.PauseAsync(t); } catch (Exception ex) { ShowDownloadToast(ex.Message); }
    }

    private async void TaskResume_Click(object? sender, RoutedEventArgs e)
    {
        if (TaskFromSender(sender) is { } t)
            try { await _app.Downloads.ResumeAsync(t); } catch (Exception ex) { ShowDownloadToast(ex.Message); }
    }

    private async void TaskCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (TaskFromSender(sender) is { } t)
            try { await _app.Downloads.CancelAsync(t); } catch (Exception ex) { ShowDownloadToast(ex.Message); }
    }

    private async void TaskRetry_Click(object? sender, RoutedEventArgs e)
    {
        if (TaskFromSender(sender) is { } t)
            try { await _app.Downloads.RetryAsync(t); } catch (Exception ex) { ShowDownloadToast(ex.Message); }
    }

    private void TaskOpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (TaskFromSender(sender) is { } t)
            try { _app.Downloads.OpenContainingFolder(t); } catch (Exception ex) { ShowDownloadToast(ex.Message); }
    }

    // ============ 并发 / 限速 ============
    private async void DownloadConcurrency_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || DownloadConcurrencyBox.SelectedIndex < 0) return;
        int v = ConcurrencyOptions[DownloadConcurrencyBox.SelectedIndex];
        _app.Settings.Current.DownloadMaxConcurrent = v;
        _app.Settings.Save();
        try { await _app.Downloads.SetConcurrencyAsync(v); } catch { }
    }

    private async void DownloadLimit_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || DownloadLimitBox.SelectedIndex < 0) return;
        long kib = LimitOptions[DownloadLimitBox.SelectedIndex];
        _app.Settings.Current.DownloadMaxSpeedKib = kib;
        _app.Settings.Save();
        try { await _app.Downloads.SetGlobalLimitAsync(kib); } catch { }
    }

    // ============ 开关 ============
    private void ClipWatch_Toggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        bool on = ClipWatchSwitch.IsChecked == true;
        _app.Settings.Current.DownloadClipboardWatch = on;
        _app.Settings.Save();
        if (on) _clipboardTimer.Start();
        else { _clipboardTimer.Stop(); ClipBanner.IsVisible = false; }
    }

    private void Notify_Toggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.DownloadNotifyComplete = NotifySwitch.IsChecked == true;
        _app.Settings.Save();
    }

    // ============ aria2 安装命令复制 ============
    private async void CopyBrew_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        try
        {
            await clipboard.SetTextAsync("brew install aria2");
            ShowDownloadToast("已复制：brew install aria2（终端粘贴执行即可）");
        }
        catch { /* 剪贴板写入失败忽略 */ }
    }

    // ============ 剪贴板监听 ============
    private async Task PollClipboardAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            string? text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text) || text == _lastClipboardText) return;
            _lastClipboardText = text;
            ClipboardLinkWatcher.OfferText(text);
        }
        catch { /* 剪贴板访问失败忽略 */ }
    }

    private void ShowClipBanner(string url)
    {
        _pendingClipUrl = url;
        ClipUrlText.Text = url;
        ClipBanner.IsVisible = true;
    }

    private async void ClipAdd_Click(object? sender, RoutedEventArgs e)
    {
        ClipBanner.IsVisible = false;
        string? url = _pendingClipUrl;
        _pendingClipUrl = null;
        if (!string.IsNullOrWhiteSpace(url))
            await AddDownloadFromUrl(url);
    }

    private void ClipIgnore_Click(object? sender, RoutedEventArgs e)
    {
        ClipBanner.IsVisible = false;
        _pendingClipUrl = null;
    }
}

/// <summary>
/// 把 <see cref="DownloadStatus"/> 映射为按钮可见性（ConverterParameter: Pause/Resume/Cancel/Retry/Open）。
/// </summary>
public sealed class DownloadStatusToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DownloadStatus status) return true;
        string? mode = parameter as string;
        return mode switch
        {
            "Pause" => status == DownloadStatus.Downloading || status == DownloadStatus.Waiting,
            "Resume" => status == DownloadStatus.Paused || status == DownloadStatus.Failed,
            "Cancel" => status == DownloadStatus.Waiting
                        || status == DownloadStatus.Downloading
                        || status == DownloadStatus.Paused,
            "Retry" => status == DownloadStatus.Failed,
            "Open" => status == DownloadStatus.Completed,
            _ => true,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
