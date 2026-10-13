using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DogDesk.Services.Downloads;

namespace DogDesk.Mac.Views;

/// <summary>
/// 下载管理 Tab（v0.8）：UI 接线，内核为 Core 的 DownloadManager（aria2）。
/// 仅对接 Core 已实现的简版 API（Start/Pause/Resume/Cancel/Retry/Remove + TaskChanged）。
/// </summary>
public partial class MainWindow
{
    private void InitDownloadTab()
    {
        DownloadList.ItemsSource = _dlTasks;
        var s = _app.Settings.Current;

        // aria2 状态
        Aria2StatusText.Text = _app.Downloads.Available
            ? $"aria2 已就绪：{_app.Downloads.Aria2Path}"
            : "aria2 未安装 → brew install aria2";

        // 设置回填
        int[] concs = { 1, 2, 3, 4, 6, 8, 16 };
        long[] speeds = { 0, 512, 1024, 2048, 5120, 10240, 20480, 51200 };
        _suppressEvents = true;
        DownloadConcurrencyBox.SelectedIndex = Math.Max(0, Array.IndexOf(concs, s.DownloadMaxConcurrent));
        DownloadLimitBox.SelectedIndex = Math.Max(0, Array.IndexOf(speeds, s.DownloadMaxSpeedKib));
        ClipWatchSwitch.IsChecked = s.DownloadClipboardWatch;
        NotifySwitch.IsChecked = s.DownloadNotifyComplete;
        DownloadDirBox.Text = s.DownloadDir;
        _suppressEvents = false;

        // 已有任务
        foreach (var t in _app.Downloads.Tasks) _dlTasks.Add(new DlTaskViewModel(t));
        DownloadEmptyText.IsVisible = _dlTasks.Count == 0;

        _app.Downloads.TaskChanged += task => Dispatcher.UIThread.Post(() =>
        {
            var vm = _dlTasks.FirstOrDefault(v => v.Id == task.Id);
            if (vm is null) _dlTasks.Insert(0, new DlTaskViewModel(task));
            else { int i = _dlTasks.IndexOf(vm); _dlTasks[i] = new DlTaskViewModel(task); }
            DownloadEmptyText.IsVisible = _dlTasks.Count == 0;
            RefreshDownloadStatus();
            if (s.DownloadNotifyComplete && task.Status == DownloadStatus.Completed)
                ShowDownloadToast($"下载完成：{vm?.FileName ?? task.Url}");
        });
        RefreshDownloadStatus();
    }

    private void RefreshDownloadStatus()
    {
        int active = _dlTasks.Count(v => v.Status == DownloadStatus.Downloading);
        double totalKb = _dlTasks.Where(v => v.Status == DownloadStatus.Downloading)
                                .Sum(v => v.Task.SpeedBps) / 1024.0;
        DownloadStatusText.Text =
            $"任务 {_dlTasks.Count} · 活跃 {active} · 总速 {totalKb:0.0} KB/s" +
            (_app.Downloads.Available ? "" : " · aria2 未就绪");
    }

    private void ShowDownloadToast(string msg)
    {
        DownloadToastText.Text = msg;
        DownloadToast.IsVisible = true;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { DownloadToast.IsVisible = false; t.Stop(); };
        t.Start();
    }

    // ============ 新建下载 ============
    private void DownloadAdd_Click(object? sender, RoutedEventArgs e) => AddDownloadFromInput();

    private void DownloadUrl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; AddDownloadFromInput(); }
    }

    private void AddDownloadFromInput()
    {
        string url = (DownloadUrlBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(url)) return;
        string dir = (DownloadDirBox.Text ?? "").Trim();
        string name = (DownloadNameBox.Text ?? "").Trim();
        try
        {
            var task = _app.Downloads.Start(url, dir, string.IsNullOrEmpty(name) ? null : name);
            DownloadUrlBox.Text = "";
            DownloadNameBox.Text = "";
            EnvStatusLabel.Text = task.IsValidUrl ? "已加入下载" : "链接无效";
        }
        catch (Exception ex) { EnvStatusLabel.Text = $"添加失败：{ex.Message}"; }
    }

    private async void DownloadBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var dir = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择保存目录" });
        if (dir.Count > 0) DownloadDirBox.Text = dir[0].Path.LocalPath;
    }

    // ============ 设置 ============
    private void DownloadConcurrency_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        int[] concs = { 1, 2, 3, 4, 6, 8, 16 };
        if (DownloadConcurrencyBox.SelectedIndex >= 0 && DownloadConcurrencyBox.SelectedIndex < concs.Length)
        {
            _app.Settings.Current.DownloadMaxConcurrent = concs[DownloadConcurrencyBox.SelectedIndex];
            _app.Downloads.MaxConcurrent = _app.Settings.Current.DownloadMaxConcurrent;
            _app.Settings.Save();
        }
    }

    private void DownloadLimit_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        long[] speeds = { 0, 512, 1024, 2048, 5120, 10240, 20480, 51200 };
        if (DownloadLimitBox.SelectedIndex >= 0 && DownloadLimitBox.SelectedIndex < speeds.Length)
        {
            _app.Settings.Current.DownloadMaxSpeedKib = speeds[DownloadLimitBox.SelectedIndex];
            _app.Downloads.MaxSpeedKb = _app.Settings.Current.DownloadMaxSpeedKib;
            _app.Settings.Save();
        }
    }

    private void ClipWatch_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.DownloadClipboardWatch = ClipWatchSwitch.IsChecked == true;
        _app.Settings.Save();
        EnvStatusLabel.Text = "剪贴板监听设置已保存（macOS 自动捕获需轮询桥接，本版未启用）";
    }

    private void Notify_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.DownloadNotifyComplete = NotifySwitch.IsChecked == true;
        _app.Settings.Save();
    }

    private void CopyBrew_Click(object? sender, RoutedEventArgs e)
        => ShowDownloadToast("请在终端执行：brew install aria2");

    private void ClipAdd_Click(object? sender, RoutedEventArgs e) { ClipBanner.IsVisible = false; }
    private void ClipIgnore_Click(object? sender, RoutedEventArgs e) { ClipBanner.IsVisible = false; }

    // ============ 任务操作（DataTemplate 按钮，DataContext=DlTaskViewModel） ============
    private static DlTaskViewModel? Vm(object? sender) => (sender as Control)?.DataContext as DlTaskViewModel;

    private void TaskPause_Click(object? sender, RoutedEventArgs e)
    { var vm = Vm(sender); if (vm is not null) _app.Downloads.Pause(vm.Id); }

    private void TaskResume_Click(object? sender, RoutedEventArgs e)
    { var vm = Vm(sender); if (vm is not null) _app.Downloads.Resume(vm.Id); }

    private void TaskCancel_Click(object? sender, RoutedEventArgs e)
    { var vm = Vm(sender); if (vm is not null) _app.Downloads.Cancel(vm.Id); }

    private void TaskRetry_Click(object? sender, RoutedEventArgs e)
    { var vm = Vm(sender); if (vm is not null) _app.Downloads.Retry(vm.Id); }

    private void TaskOpenFolder_Click(object? sender, RoutedEventArgs e)
    { var vm = Vm(sender); if (vm is not null) ShowDownloadToast($"目录：{vm.Task.SaveDir}"); }
}

/// <summary>按下载状态决定操作按钮是否可见（ConverterParameter=Pause/Resume/Cancel/Retry/Open）。</summary>
public sealed class DownloadStatusToVisibilityConverter : Avalonia.Data.Converters.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not DownloadStatus st) return false;
        string? op = parameter as string;
        return op switch
        {
            "Pause" => st is DownloadStatus.Downloading or DownloadStatus.Pending,
            "Resume" => st is DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled,
            "Cancel" => st is DownloadStatus.Downloading or DownloadStatus.Pending or DownloadStatus.Paused,
            "Retry" => st is DownloadStatus.Failed or DownloadStatus.Canceled,
            "Open" => st == DownloadStatus.Completed,
            _ => false,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}
