using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DogDesk.Models;
using DogDesk.Services.Download;

namespace DogDesk;

/// <summary>
/// v0.8：下载加速与下载管理 UI 接线（内核 aria2 由 DogDesk.Core 的 DownloadManager 提供）。
/// 本 partial 只做界面回填、事件转发与托盘通知，不实现下载内核。
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _clipTimer;
    private string? _pendingClipUrl;

    /// <summary>v0.8 初始化入口：回填设置、订阅内核事件、启动剪贴板监听（如开启）。</summary>
    private void InitDownloadTab()
    {
        try
        {
            DownloadGrid.ItemsSource = _downloads.Tasks;

            _loadingUi = true;
            SelectComboByTag(DownloadConcurrencyCombo, _settings.DownloadMaxConcurrent.ToString());
            SelectComboByTag(DownloadLimitCombo, _settings.DownloadMaxSpeedKib.ToString());
            ClipWatchCheck.IsChecked = _settings.DownloadClipboardWatch;
            NotifyCheck.IsChecked = _settings.DownloadNotifyComplete;
            DownloadDirBox.Text = _settings.DownloadDir;
            _loadingUi = false;

            _downloads.StateChanged += OnDownloadStateChanged;
            _downloads.TaskCompleted += OnDownloadTaskCompleted;
            ClipboardLinkWatcher.LinkDetected += OnClipboardLinkDetected;

            if (_settings.DownloadClipboardWatch) StartClipWatcher();

            RefreshDownloadStatus();
            _ = InitEngineAsync();
        }
        catch (Exception ex)
        {
            Log("[下载] 初始化异常：" + ex.Message);
        }
    }

    private async Task InitEngineAsync()
    {
        try { await _downloads.InitializeAsync(); }
        catch (Exception ex) { Log("[下载] aria2 内核初始化失败：" + ex.Message); }
    }

    // ================= 新建下载 =================

    private async void OnDownloadAddClick(object sender, RoutedEventArgs e)
    {
        string url = DownloadUrlBox.Text?.Trim() ?? "";
        if (url.Length == 0)
        {
            MessageBox.Show("请先粘贴 http/https 下载链接。", "新建下载", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string dir = DownloadDirBox.Text?.Trim() ?? "";
        string name = DownloadNameBox.Text?.Trim() ?? "";
        _settings.DownloadDir = dir;
        _settingsService.Save();
        try
        {
            await _downloads.AddTaskAsync(url, dir.Length == 0 ? null : dir, name.Length == 0 ? null : name);
            DownloadUrlBox.Text = "";
            DownloadNameBox.Text = "";
            Log("[下载] 已添加：" + url);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "下载链接无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            Log("[下载] 链接无效：" + ex.Message);
        }
        catch (Exception ex)
        {
            MessageBox.Show("添加下载失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Log("[下载] 添加失败：" + ex.Message);
        }
    }

    private void OnDownloadBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择下载保存目录",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DownloadDirBox.Text = dlg.SelectedPath;
    }

    // ================= 下载设置 =================

    private void OnDownloadConcurrencyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        int v = SelectedTagInt(DownloadConcurrencyCombo);
        if (v <= 0) return;
        _settings.DownloadMaxConcurrent = v;
        _settingsService.Save();
        _ = ApplyConcurrencyAsync(v);
    }

    private async Task ApplyConcurrencyAsync(int v)
    {
        try { await _downloads.SetConcurrencyAsync(v); }
        catch (Exception ex) { Log("[下载] 设置并发失败：" + ex.Message); }
    }

    private void OnDownloadLimitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        long v = SelectedTagLong(DownloadLimitCombo);
        _settings.DownloadMaxSpeedKib = v;
        _settingsService.Save();
        _ = ApplyLimitAsync(v);
    }

    private async Task ApplyLimitAsync(long v)
    {
        try { await _downloads.SetGlobalLimitAsync(v); }
        catch (Exception ex) { Log("[下载] 设置限速失败：" + ex.Message); }
    }

    private void OnClipWatchChanged(object sender, RoutedEventArgs e)
    {
        bool on = ClipWatchCheck.IsChecked == true;
        _settings.DownloadClipboardWatch = on;
        _settingsService.Save();
        if (on) StartClipWatcher();
        else StopClipWatcher();
    }

    private void OnNotifyCheckChanged(object sender, RoutedEventArgs e)
    {
        _settings.DownloadNotifyComplete = NotifyCheck.IsChecked == true;
        _settingsService.Save();
    }

    // ================= 任务行操作 =================

    private static DownloadTask? RowTask(object sender) =>
        (sender as FrameworkElement)?.DataContext as DownloadTask;

    private async void OnRowPauseClick(object sender, RoutedEventArgs e)
    {
        var t = RowTask(sender); if (t is null) return;
        try { await _downloads.PauseAsync(t); } catch (Exception ex) { Log("[下载] 暂停失败：" + ex.Message); }
    }

    private async void OnRowResumeClick(object sender, RoutedEventArgs e)
    {
        var t = RowTask(sender); if (t is null) return;
        try { await _downloads.ResumeAsync(t); } catch (Exception ex) { Log("[下载] 恢复失败：" + ex.Message); }
    }

    private async void OnRowCancelClick(object sender, RoutedEventArgs e)
    {
        var t = RowTask(sender); if (t is null) return;
        try { await _downloads.CancelAsync(t); } catch (Exception ex) { Log("[下载] 取消失败：" + ex.Message); }
    }

    private async void OnRowRetryClick(object sender, RoutedEventArgs e)
    {
        var t = RowTask(sender); if (t is null) return;
        try { await _downloads.RetryAsync(t); } catch (Exception ex) { Log("[下载] 重试失败：" + ex.Message); }
    }

    private void OnRowOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var t = RowTask(sender); if (t is null) return;
        try { _downloads.OpenContainingFolder(t); } catch (Exception ex) { Log("[下载] 打开目录失败：" + ex.Message); }
    }

    // ================= 状态刷新 / 完成通知 =================

    private void OnDownloadStateChanged() =>
        Dispatcher.BeginInvoke(new Action(RefreshDownloadStatus));

    private void RefreshDownloadStatus()
    {
        try
        {
            int active = 0;
            double totalBps = 0;
            foreach (var t in _downloads.Tasks)
            {
                if (t.Status == DownloadStatus.Downloading)
                {
                    active++;
                    totalBps += t.SpeedBps;
                }
            }
            DownloadStatusText.Text =
                $"{_downloads.Aria2StatusText} · 活跃 {active} · 总速度 {FormatSpeed(totalBps)}";
            DownloadEmptyText.Visibility = _downloads.Tasks.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
    }

    private void OnDownloadTaskCompleted(DownloadTask task)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (NotifyCheck.IsChecked == true)
                    ((App)Application.Current).Tray?.ShowNotification("下载完成", task.FileName);
                Log("[下载] 完成 " + task.FileName);
                RefreshDownloadStatus();
            }
            catch { }
        }));
    }

    private static string FormatSpeed(double bps)
    {
        if (bps <= 0) return "0 B/s";
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        int u = 0;
        while (bps >= 1024 && u < units.Length - 1) { bps /= 1024; u++; }
        return $"{bps:0.0} {units[u]}";
    }

    // ================= 剪贴板链接监听 =================

    private void StartClipWatcher()
    {
        if (_clipTimer != null) return;
        _clipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clipTimer.Tick += (_, _) =>
        {
            try
            {
                string text = System.Windows.Clipboard.GetText();
                ClipboardLinkWatcher.OfferText(text);
            }
            catch { /* 剪贴板被占用时忽略本轮 */ }
        };
        _clipTimer.Start();
    }

    private void StopClipWatcher()
    {
        _clipTimer?.Stop();
        _clipTimer = null;
    }

    private void OnClipboardLinkDetected(string url)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                // 已在展示同一链接则不重复弹出
                if (ClipBanner.Visibility == Visibility.Visible && ClipUrlText.Text == url) return;
                _pendingClipUrl = url;
                ClipUrlText.Text = url;
                ClipBanner.Visibility = Visibility.Visible;
            }
            catch { }
        }));
    }

    private async void OnClipAddClick(object sender, RoutedEventArgs e)
    {
        string url = _pendingClipUrl ?? "";
        ClipBanner.Visibility = Visibility.Collapsed;
        _pendingClipUrl = null;
        if (url.Length == 0) return;
        try
        {
            await _downloads.AddTaskAsync(url, null, null);
            Log("[下载] 已从剪贴板添加：" + url);
        }
        catch (Exception ex)
        {
            MessageBox.Show("添加下载失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Log("[下载] 剪贴板添加失败：" + ex.Message);
        }
    }

    private void OnClipIgnoreClick(object sender, RoutedEventArgs e)
    {
        ClipBanner.Visibility = Visibility.Collapsed;
        _pendingClipUrl = null;
    }

    // ================= 小工具 =================

    private static int SelectedTagInt(ComboBox c) =>
        c.SelectedItem is ComboBoxItem it && it.Tag is string s && int.TryParse(s, out var v) ? v : 0;

    private static long SelectedTagLong(ComboBox c) =>
        c.SelectedItem is ComboBoxItem it && it.Tag is string s && long.TryParse(s, out var v) ? v : 0;

    private static void SelectComboByTag(ComboBox c, string tag)
    {
        foreach (var item in c.Items)
            if (item is ComboBoxItem ci && string.Equals(ci.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                c.SelectedItem = ci;
                return;
            }
        if (c.Items.Count > 0) c.SelectedIndex = 0;
    }
}
