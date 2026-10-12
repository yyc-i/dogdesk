using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MediaBarWallpaper.Interop;
using MediaBarWallpaper.Models;
using MediaBarWallpaper.Services;
using MediaBarWallpaper.Views;

namespace MediaBarWallpaper;

public partial class MainWindow : Window
{
    private readonly IMediaSession _session;
    private readonly WallpaperEngine _wallpaper;
    private readonly AudioSpectrumService _spectrum;
    private readonly LyricsService _lyrics;
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private MediaBarWindow? _mediaBar;
    private LyricsWindow? _lyricsWindow;
    private bool _loadingUi;

    /// <summary>设为 true 后关闭窗口不再最小化到托盘（App 退出时置位）。</summary>
    public bool ForceClose { get; set; }

    public MainWindow(IMediaSession session, WallpaperEngine wallpaper,
        AudioSpectrumService spectrum, LyricsService lyrics, SettingsService settingsService)
    {
        InitializeComponent();
        _session = session;
        _wallpaper = wallpaper;
        _spectrum = spectrum;
        _lyrics = lyrics;
        _settingsService = settingsService;
        _settings = settingsService.Current;

        _wallpaper.Log += Log;
        _lyrics.Log += Log;
        _session.PropertyChanged += OnSessionChanged;

        LoadSettingsToUi();
        Log("MediaBarWallpaper v0.4.0 已启动。");
        Log($"视频内核：{Services.Mpv.MpvNativeLoader.Status}");
        Log("建议：壁纸页选内容 → 应用壁纸 → 频谱页开律动 → 媒体条/歌词页开开关。");
    }

    // ================= 设置面板 =================

    private void LoadSettingsToUi()
    {
        _loadingUi = true;

        switch (_settings.WallpaperKind)
        {
            case WallpaperKind.Image: KindImage.IsChecked = true; break;
            case WallpaperKind.Video: KindVideo.IsChecked = true; break;
            default: KindAnimated.IsChecked = true; break;
        }
        AnimAurora.IsChecked = _settings.AnimationStyle == AnimationStyle.Aurora;
        AnimParticles.IsChecked = _settings.AnimationStyle == AnimationStyle.Particles;
        WallpaperPathBox.Text = _settings.WallpaperPath;
        WallpaperFolderBox.Text = _settings.WallpaperFolder;
        StretchCombo.SelectedIndex = (int)_settings.StretchMode;
        BrightnessSlider.Value = _settings.Brightness;
        SlideshowCheck.IsChecked = _settings.SlideshowEnabled;
        SlideshowIntervalSlider.Value = _settings.SlideshowIntervalSec;

        MediaBarCheck.IsChecked = _settings.MediaBarEnabled;
        DockCombo.SelectedIndex = _settings.MediaBarDock;
        WidthSlider.Value = _settings.MediaBarWidth;
        AutoHideCheck.IsChecked = _settings.MediaBarAutoHide;
        GradientCheck.IsChecked = _settings.MediaBarCoverGradient;

        LyricsCheck.IsChecked = _settings.LyricsEnabled;
        FontSlider.Value = _settings.LyricsFontSize;
        OpacitySlider.Value = _settings.LyricsOpacity;
        KaraokeCheck.IsChecked = _settings.LyricsKaraoke;

        SpectrumCheck.IsChecked = _settings.SpectrumEnabled;
        SpectrumStyleCombo.SelectedIndex = (int)_settings.SpectrumStyle;
        SensitivitySlider.Value = _settings.SpectrumSensitivity;
        SmoothingSlider.Value = _settings.SpectrumSmoothing;

        AutoStartCheck.IsChecked = _settings.AutoStart;
        TrayCheck.IsChecked = _settings.MinimizeToTray;
        IdleFpsCheck.IsChecked = _settings.IdleReduceFps;

        UpdateWallpaperPathUi();
        UpdateSliderLabels();
        RefreshSessionUi();
        _loadingUi = false;
    }

    /// <summary>从控件回写设置并保存（所有设置变更统一入口）。</summary>
    private void SaveSettingsFromUi()
    {
        if (_loadingUi) return;

        _settings.WallpaperKind = KindImage.IsChecked == true ? WallpaperKind.Image
            : KindVideo.IsChecked == true ? WallpaperKind.Video : WallpaperKind.Animated;
        _settings.AnimationStyle = AnimParticles.IsChecked == true ? AnimationStyle.Particles : AnimationStyle.Aurora;
        _settings.WallpaperPath = WallpaperPathBox.Text?.Trim() ?? "";
        _settings.WallpaperFolder = WallpaperFolderBox.Text?.Trim() ?? "";
        _settings.StretchMode = (StretchMode)Math.Clamp(StretchCombo.SelectedIndex, 0, 2);
        _settings.Brightness = BrightnessSlider.Value;
        _settings.SlideshowEnabled = SlideshowCheck.IsChecked == true;
        _settings.SlideshowIntervalSec = SlideshowIntervalSlider.Value;

        _settings.MediaBarEnabled = MediaBarCheck.IsChecked == true;
        _settings.MediaBarDock = Math.Clamp(DockCombo.SelectedIndex, 0, 2);
        _settings.MediaBarWidth = WidthSlider.Value;
        _settings.MediaBarAutoHide = AutoHideCheck.IsChecked == true;
        _settings.MediaBarCoverGradient = GradientCheck.IsChecked == true;

        _settings.LyricsEnabled = LyricsCheck.IsChecked == true;
        _settings.LyricsFontSize = FontSlider.Value;
        _settings.LyricsOpacity = OpacitySlider.Value;
        _settings.LyricsKaraoke = KaraokeCheck.IsChecked == true;

        _settings.SpectrumEnabled = SpectrumCheck.IsChecked == true;
        _settings.SpectrumStyle = (SpectrumStyle)Math.Clamp(SpectrumStyleCombo.SelectedIndex, 0, 2);
        _settings.SpectrumSensitivity = SensitivitySlider.Value;
        _settings.SpectrumSmoothing = SmoothingSlider.Value;

        _settings.AutoStart = AutoStartCheck.IsChecked == true;
        _settings.MinimizeToTray = TrayCheck.IsChecked == true;
        _settings.IdleReduceFps = IdleFpsCheck.IsChecked == true;

        _settingsService.Save();
    }

    private void UpdateSliderLabels()
    {
        BrightnessValue.Text = $"{Math.Round(BrightnessSlider.Value * 100)}%";
        SlideshowIntervalValue.Text = $"{Math.Round(SlideshowIntervalSlider.Value)}s";
        WidthValue.Text = $"{Math.Round(WidthSlider.Value)}px";
        FontValue.Text = $"{Math.Round(FontSlider.Value)}px";
        OpacityValue.Text = $"{Math.Round(OpacitySlider.Value * 100)}%";
        SensitivityValue.Text = $"{SensitivitySlider.Value:0.0}×";
        SmoothingValue.Text = $"{SmoothingSlider.Value:0.00}";
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        UpdateSliderLabels();
        SaveSettingsFromUi();
        ApplyLiveSettings();
    }

    /// <summary>即时生效的设置（媒体条/歌词外观），不重启窗口。</summary>
    private void ApplyLiveSettings()
    {
        if (_mediaBar is not null)
        {
            _mediaBar.Dock = (TaskbarService.DockPosition)_settings.MediaBarDock;
            if (Math.Abs(_mediaBar.Width - _settings.MediaBarWidth) > 1)
                _mediaBar.Width = _settings.MediaBarWidth;
            _mediaBar.ForceReposition();
            _mediaBar.RefreshFromSettings();
        }
        _lyricsWindow?.ApplyStyleSettings();
    }

    // ================= 壁纸 =================

    private void OnWallpaperKindChanged(object sender, RoutedEventArgs e)
    {
        UpdateWallpaperPathUi();
        SaveSettingsFromUi();
    }

    private void UpdateWallpaperPathUi()
    {
        bool isImage = KindImage.IsChecked == true;
        bool isVideo = KindVideo.IsChecked == true;

        AnimStylePanel.Visibility = (!isImage && !isVideo) ? Visibility.Visible : Visibility.Collapsed;
        WallpaperPathBox.IsEnabled = isImage || isVideo;
        BrowseButton.IsEnabled = isImage || isVideo;
        WallpaperFolderBox.IsEnabled = isImage;
        FolderBrowseButton.IsEnabled = isImage;
        SlideshowCheck.IsEnabled = isImage;
        SlideshowIntervalSlider.IsEnabled = isImage && SlideshowCheck.IsChecked == true;
    }

    private void OnPathBoxChanged(object sender, TextChangedEventArgs e) => SaveSettingsFromUi();
    private void OnFolderBoxChanged(object sender, TextChangedEventArgs e) => SaveSettingsFromUi();

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        bool isVideo = KindVideo.IsChecked == true;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = isVideo
                ? "视频文件|*.mp4;*.wmv;*.mkv;*.avi;*.mov;*.flv;*.webm|所有文件|*.*"
                : "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*",
            Title = isVideo ? "选择视频壁纸" : "选择图片壁纸",
        };
        if (dlg.ShowDialog(this) == true)
            WallpaperPathBox.Text = dlg.FileName;
    }

    private void OnFolderBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择图片文件夹（用于轮播）",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            WallpaperFolderBox.Text = dlg.SelectedPath;
    }

    private void OnApplyWallpaperClick(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        ApplyCurrentSettings();
    }

    private void OnStopWallpaperClick(object sender, RoutedEventArgs e)
    {
        _wallpaper.Stop();
        _spectrum.Stop();
        WallpaperStatusText.Text = "未运行";
    }

    /// <summary>托盘「应用壁纸」入口。</summary>
    public void ApplyCurrentSettings()
    {
        SaveSettingsFromUi();

        AudioSpectrumService? spectrum = null;
        if (_settings.SpectrumEnabled)
        {
            if (_spectrum.Start())
                spectrum = _spectrum;
            else
                Log("[频谱] 系统声音捕获启动失败（无可用的默认输出设备），律动已关闭");
        }
        else
        {
            _spectrum.Stop();
        }

        bool ok = _wallpaper.Start(_settings, spectrum);
        WallpaperStatusText.Text = ok ? "运行中" : "启动失败";
        if (ok) Log("[壁纸] 应用完成，请在桌面查看效果");
    }

    /// <summary>托盘「停止壁纸」入口。</summary>
    public void StopWallpaperNow()
    {
        _wallpaper.Stop();
        _spectrum.Stop();
        WallpaperStatusText.Text = "未运行";
    }

    // ================= 媒体条 =================

    private void OnMediaBarToggled(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        if (MediaBarCheck.IsChecked == true)
        {
            _mediaBar ??= new MediaBarWindow(_session, _settings);
            _mediaBar.Dock = (TaskbarService.DockPosition)_settings.MediaBarDock;
            _mediaBar.Width = _settings.MediaBarWidth;
            _mediaBar.Show();
            Log("[媒体条] 已显示，吸附位置=" + DockCombo.Text);
        }
        else
        {
            _mediaBar?.Close();
            Log("[媒体条] 已隐藏");
        }
    }

    private void OnDockChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveSettingsFromUi();
        if (_mediaBar is not null)
        {
            _mediaBar.Dock = (TaskbarService.DockPosition)_settings.MediaBarDock;
            _mediaBar.ForceReposition();
            Log("[媒体条] 吸附位置更新为 " + DockCombo.Text);
        }
    }

    // ================= 桌面歌词 =================

    private void OnLyricsToggled(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        if (LyricsCheck.IsChecked == true)
        {
            _lyricsWindow ??= new LyricsWindow(_session, _lyrics, _settings);
            _lyricsWindow.ApplyStyleSettings();
            _lyricsWindow.Start();
            Log("[歌词] 桌面歌词已显示（网易云 / QQ音乐 / LRCLIB 自动回退）");
        }
        else
        {
            _lyricsWindow?.Stop();
            Log("[歌词] 桌面歌词已隐藏");
        }
    }

    // ================= 设置页 =================

    private void OnAccentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string color)
        {
            _settings.AccentColor = color;
            _settingsService.Save();
            ApplyLiveSettings();
            Log($"[设置] 强调色更新为 {color}");
        }
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        bool enabled = AutoStartCheck.IsChecked == true;
        _settings.AutoStart = enabled;
        _settingsService.Save();
        AutoStartService.SetEnabled(enabled);
        Log(enabled ? "[设置] 已启用开机自启（启动文件夹快捷方式）" : "[设置] 已关闭开机自启");
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _settingsService.Reset();
        _settings = _settingsService.Current;
        LoadSettingsToUi();
        ApplyLiveSettings();
        Log("[设置] 已恢复默认设置");
    }

    // ================= 会话/日志 =================

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IMediaSession.Title)
            or nameof(IMediaSession.Artist)
            or nameof(IMediaSession.IsPlaying))
            RefreshSessionUi();
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => _session.Previous();
    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _session.TogglePlayPause();
    private void OnNextClick(object sender, RoutedEventArgs e) => _session.Next();

    private void RefreshSessionUi()
    {
        TitleBarText.Text = _session.Title == "--" ? "未在播放" : $"{_session.Title} — {_session.Artist}";
        SessionBadgeText.Text = _session.IsReal ? "SMTC 真实会话" : "演示模式";
        PlayButton2.Content = _session.IsPlaying ? "暂停" : "播放";
    }

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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_settings.MinimizeToTray && !ForceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lyricsWindow?.Stop();
        _mediaBar?.Close();
        _spectrum.Stop();
        _wallpaper.Dispose();
        _session.PropertyChanged -= OnSessionChanged;
        SaveSettingsFromUi();
        base.OnClosed(e);
    }
}
