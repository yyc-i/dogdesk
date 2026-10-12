using System.ComponentModel;
using System.IO;
using System.Windows;
using MediaBarWallpaper.Interop;
using MediaBarWallpaper.Services;
using MediaBarWallpaper.Views;

namespace MediaBarWallpaper;

public partial class MainWindow : Window
{
    private readonly IMediaSession _session;
    private readonly WallpaperEngine _wallpaper;
    private readonly AudioSpectrumService _spectrum;
    private readonly LyricsService _lyrics;
    private MediaBarWindow? _mediaBar;
    private LyricsWindow? _lyricsWindow;

    public MainWindow(IMediaSession session, WallpaperEngine wallpaper,
        AudioSpectrumService spectrum, LyricsService lyrics)
    {
        InitializeComponent();
        _session = session;
        _wallpaper = wallpaper;
        _spectrum = spectrum;
        _lyrics = lyrics;
        _wallpaper.Log += Log;
        _lyrics.Log += Log;
        _session.PropertyChanged += OnSessionChanged;

        UpdateWallpaperPathUi();
        RefreshSessionUi();
        Log("MediaBarWallpaper v0.3.0 已启动。");
        Log($"视频内核：{Services.Mpv.MpvNativeLoader.Status}（libmpv 缺失时视频自动回退 MediaElement）。");
        Log("建议：先点「应用壁纸」使用内置动画，再依次开启「频谱律动 / 媒体条 / 桌面歌词」。");
    }

    // ---- 壁纸 ----

    private void OnApplyWallpaperClick(object sender, RoutedEventArgs e)
    {
        WallpaperKind kind = KindVideo.IsChecked == true ? WallpaperKind.Video
            : KindImage.IsChecked == true ? WallpaperKind.Image
            : WallpaperKind.Animated;

        string? path = null;
        if (kind != WallpaperKind.Animated)
        {
            path = WallpaperPathBox.Text?.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Log($"[壁纸] 未选择有效文件（{path}），请先「浏览…」选择文件");
                WallpaperStatusText.Text = "失败：未选择文件";
                return;
            }
        }

        AudioSpectrumService? spectrum = null;
        if (SpectrumEnabled.IsChecked == true)
        {
            if (_spectrum.Start())
                spectrum = _spectrum;
            else
                Log("[频谱] 系统声音捕获启动失败（无可用的默认输出设备），律动已关闭");
        }

        bool ok = _wallpaper.Start(kind, path, spectrum);
        WallpaperStatusText.Text = ok ? "运行中" : "启动失败";
    }

    private void OnStopWallpaperClick(object sender, RoutedEventArgs e)
    {
        _wallpaper.Stop();
        _spectrum.Stop();
        WallpaperStatusText.Text = "未运行";
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        bool isVideo = KindVideo.IsChecked == true;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = isVideo
                ? "视频文件|*.mp4;*.wmv;*.mkv;*.avi;*.mov|所有文件|*.*"
                : "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*",
            Title = isVideo ? "选择视频壁纸" : "选择图片壁纸",
        };

        if (dlg.ShowDialog(this) == true)
            WallpaperPathBox.Text = dlg.FileName;
    }

    private void OnWallpaperKindChanged(object sender, RoutedEventArgs e) => UpdateWallpaperPathUi();

    private void UpdateWallpaperPathUi()
    {
        bool needFile = KindImage.IsChecked == true || KindVideo.IsChecked == true;
        WallpaperPathBox.IsEnabled = needFile;
        BrowseButton.IsEnabled = needFile;
        if (!needFile)
            WallpaperPathBox.Text = "（可留空：仅内置动画不需要文件）";
    }

    // ---- 媒体条 ----

    private void OnMediaBarToggled(object sender, RoutedEventArgs e)
    {
        if (MediaBarEnabled.IsChecked == true)
        {
            if (_mediaBar is null)
            {
                _mediaBar = new MediaBarWindow(_session) { Dock = (TaskbarService.DockPosition)DockBox.SelectedIndex };
                _mediaBar.Closed += (_, _) => _mediaBar = null;
                _mediaBar.Show();
                Log("[媒体条] 已显示，吸附位置=" + DockBox.Text);
            }
        }
        else
        {
            _mediaBar?.Close();
            Log("[媒体条] 已隐藏");
        }
    }

    private void OnDockChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_mediaBar is not null)
        {
            _mediaBar.Dock = (TaskbarService.DockPosition)DockBox.SelectedIndex;
            Log("[媒体条] 吸附位置更新为 " + DockBox.Text);
        }
    }

    // ---- 桌面歌词 ----

    private void OnLyricsToggled(object sender, RoutedEventArgs e)
    {
        if (LyricsEnabled.IsChecked == true)
        {
            _lyricsWindow ??= new LyricsWindow(_session, _lyrics);
            _lyricsWindow.Start();
            Log("[歌词] 桌面歌词已显示（网易云 / QQ音乐 / LRCLIB 自动回退）");
        }
        else
        {
            _lyricsWindow?.Stop();
            Log("[歌词] 桌面歌词已隐藏");
        }
    }

    // ---- 媒体会话 ----

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e) => RefreshSessionUi();

    private void RefreshSessionUi()
    {
        TrackText.Text = $"{_session.Title} — {_session.Artist}";
        PlayPanelButton.Content = _session.IsPlaying ? "暂停" : "播放";
        SessionStateText.Text = _session.IsReal ? "会话：SMTC 真实会话" : "会话：演示模式";
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => _session.Previous();
    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _session.TogglePlayPause();
    private void OnNextClick(object sender, RoutedEventArgs e) => _session.Next();

    // ---- 日志 ----

    private void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (LogBox.Dispatcher.CheckAccess())
            AppendLog(line);
        else
            LogBox.Dispatcher.BeginInvoke(() => AppendLog(line));
    }

    private void AppendLog(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        _lyricsWindow?.Stop();
        _mediaBar?.Close();
        _spectrum.Stop();
        _wallpaper.Dispose();
        _session.PropertyChanged -= OnSessionChanged;
        base.OnClosed(e);
    }
}
