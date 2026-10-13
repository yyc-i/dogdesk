using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DogDesk.Interop;
using DogDesk.Models;
using DogDesk.Services;
using DogDesk.Services.Download;
using DogDesk.Services.Equalizer;
using DogDesk.Services.Effect;
using DogDesk.Services.DeskBox;
using DogDesk.Services.MusicSource;
using DogDesk.Services.Optimizer;
using DogDesk.Services.Playback;
using DogDesk.Services.Playlists;
using DogDesk.Views;

namespace DogDesk;

public partial class MainWindow : Window
{
    private readonly IMediaSession _session;
    private readonly SmtcMediaSession _system;
    private readonly MpvAudioPlayer _builtin;
    private readonly PlaybackQueue _queue;
    private readonly NeteaseMusicSource _netease;
    private readonly NavidromeClient _navidrome;
    private readonly WallpaperEngine _wallpaper;
    private readonly AudioSpectrumService _spectrum;
    private readonly LyricsService _lyrics;
    private readonly SettingsService _settingsService;
    private readonly DownloadManager _downloads;

    private AppSettings _settings;
    private bool _loadingUi;
    private MediaBarWindow? _mediaBar;
    private LyricsWindow? _lyricsWindow;
    private FullscreenLyricsWindow? _fullscreenLyrics;
    private IdleMemoryManager? _idle;

    private Slider[] _eqSliders = Array.Empty<Slider>();
    private TextBlock[] _eqValueLabels = Array.Empty<TextBlock>();
    private bool _eqLoading;
    private bool _spectrumAutoStopped;

    // DeskBox 桌面整理
    private DeskBoxData _deskData = new();
    private readonly List<DeskBoxOverlayWindow> _deskOverlays = new();

    // 系统优化
    private readonly List<CheckBox> _opChecks = new();
    private readonly Dictionary<string, OptItem> _opById = new();

    /// <summary>设为 true 后关闭窗口不再最小化到托盘（App 退出时置位）。</summary>
    public bool ForceClose { get; set; }

    public MainWindow(IMediaSession session, SmtcMediaSession system, MpvAudioPlayer builtin,
        PlaybackQueue queue, NeteaseMusicSource netease, NavidromeClient navidrome,
        WallpaperEngine wallpaper, AudioSpectrumService spectrum, LyricsService lyrics,
        SettingsService settingsService, DownloadManager downloads)
    {
        InitializeComponent();
        _session = session;
        _system = system;
        _builtin = builtin;
        _queue = queue;
        _netease = netease;
        _navidrome = navidrome;
        _wallpaper = wallpaper;
        _spectrum = spectrum;
        _lyrics = lyrics;
        _settingsService = settingsService;
        _downloads = downloads;
        _settings = settingsService.Current;

        _wallpaper.Log += Log;
        _lyrics.Log += Log;
        _builtin.Log += Log;
        _session.PropertyChanged += OnSessionChanged;

        BuildEqPanel();
        BuildOpsPanel();
        LoadSettingsToUi();
        Log("DogDesk v0.5.0 已启动。");
        Log($"视频内核：{Services.Mpv.MpvNativeLoader.Status}");
        Log("曲库/在线/Navidrome 用「曲库」「在线音源」页；均衡器见「均衡器」页。");
    }

    /// <summary>App 构造完成后由 App 调用：接线播放队列与空闲内存管理。</summary>
    public void OnAppStarted()
    {
        // —— 播放队列 → 内置播放器 接线 ——
        _queue.CurrentChanged += OnQueueCurrentChanged;
        _builtin.PlaybackEnded += OnBuiltinEnded;
        _builtin.NextRequested += () => PlayQueueMove(1);
        _builtin.PreviousRequested += () => PlayQueueMove(-1);

        // —— 空闲内存/频谱按需管理（单一低频定时器，B1/B2/B4）——
        _idle = new IdleMemoryManager(IsReallyIdle);
        _idle.IdleTick += OnIdleTick;
        _idle.ActiveTick += OnActiveTick;
        _idle.Start();

        InitV06();
        InitDownloadTab();
    }

    /// <summary>F8：窗口关闭时保存播放位置快照（失败静默）。</summary>
    private void SaveResumeIfNeeded()
    {
        try
        {
            if (!_settings.ResumePlayback) return;
            if (_queue.Count == 0) return;
            int idx = 0;
            var items = _queue.Items;
            for (int i = 0; i < items.Count; i++)
                if (ReferenceEquals(items[i], _queue.Current)) { idx = i; break; }
            ResumeStore.Save(new ResumeData
            {
                Queue = items.ToList(),
                CurrentIndex = idx,
                PositionSec = _builtin.PositionSec,
            });
        }
        catch { }
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

        // v0.5 全屏歌词
        FullscreenLyricsCheck.IsChecked = _settings.FullscreenLyricsEnabled;
        FullscreenStyleCombo.SelectedIndex = (int)_settings.LyricsAnimationStyle;
        FullscreenOpacitySlider.Value = _settings.FullscreenLyricsOpacity;
        DynamicThemeCheck.IsChecked = _settings.DynamicThemeEnabled;

        // v0.5 曲库 / 循环
        LibraryFolderBox.Text = _settings.MusicLibraryFolder;
        RepeatCombo.SelectedIndex = (int)_queue.Repeat;

        // v0.5 Navidrome
        NaviServerBox.Text = _settings.NavidromeServer;
        NaviUserBox.Text = _settings.NavidromeUser;
        NaviPassBox.Text = _settings.NavidromePassword;
        _navidrome.Configure(_settings.NavidromeServer, _settings.NavidromeUser, _settings.NavidromePassword);

        // v0.5 均衡器
        EqualizerEnabledCheck.IsChecked = _settings.EqualizerEnabled;
        LoadEqToUi();

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

        // v0.5 全屏歌词 / 动态主题
        _settings.FullscreenLyricsEnabled = FullscreenLyricsCheck.IsChecked == true;
        _settings.LyricsAnimationStyle = (LyricsAnimationStyle)Math.Clamp(FullscreenStyleCombo.SelectedIndex, 0, 2);
        _settings.FullscreenLyricsOpacity = FullscreenOpacitySlider.Value;
        _settings.DynamicThemeEnabled = DynamicThemeCheck.IsChecked == true;

        // v0.5 Navidrome
        _settings.NavidromeServer = NaviServerBox.Text?.Trim() ?? "";
        _settings.NavidromeUser = NaviUserBox.Text?.Trim() ?? "";
        _settings.NavidromePassword = NaviPassBox.Text ?? "";

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
        FullscreenOpacityValue.Text = $"{Math.Round(FullscreenOpacitySlider.Value * 100)}%";
        SensitivityValue.Text = $"{SensitivitySlider.Value:0.0}×";
        SmoothingValue.Text = $"{SmoothingSlider.Value:0.00}";
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        UpdateSliderLabels();
        SaveSettingsFromUi();
        ApplyLiveSettings();
    }

    /// <summary>即时生效的设置（媒体条/歌词外观/全屏歌词），不重启窗口。</summary>
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
        _fullscreenLyrics?.ApplyStyleSettings();
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
            Log("[媒体条] 已显示");
        }
        else
        {
            _mediaBar?.Close();
            _mediaBar = null;   // 懒创建：关闭即清引用（B8）
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
            Log("[歌词] 桌面歌词已显示");
        }
        else
        {
            _lyricsWindow?.Stop();
            _lyricsWindow = null;   // 懒创建：关闭即清引用（B8）
            Log("[歌词] 桌面歌词已隐藏");
        }
    }

    private void OnFullscreenLyricsToggled(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        if (FullscreenLyricsCheck.IsChecked == true)
        {
            _fullscreenLyrics ??= new FullscreenLyricsWindow(_session, _lyrics, _settings);
            _fullscreenLyrics.ApplyStyleSettings();
            _fullscreenLyrics.Start();
            Log($"[全屏歌词] 已显示（样式={FullscreenStyleCombo.SelectionBoxItem}）");
        }
        else
        {
            _fullscreenLyrics?.Stop();
            _fullscreenLyrics = null;   // 懒创建（B8）
            Log("[全屏歌词] 已关闭");
        }
    }

    // ================= 本地曲库 =================

    private void OnLibraryFolderChanged(object sender, TextChangedEventArgs e)
    {
        _settings.MusicLibraryFolder = LibraryFolderBox.Text?.Trim() ?? "";
        SaveSettingsFromUi();
    }

    private void OnBrowseLibraryClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择音乐文件夹（递归扫描）",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            LibraryFolderBox.Text = dlg.SelectedPath;
    }

    private async void OnScanLibraryClick(object sender, RoutedEventArgs e)
    {
        string folder = _settings.MusicLibraryFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            LibraryStatusText.Text = "请先选择文件夹";
            return;
        }
        LibraryStatusText.Text = "扫描中…";
        LibraryList.Items.Clear();
        var list = await Task.Run(() => MusicLibraryScanner.Scan(folder, Log));
        foreach (var item in list)
            LibraryList.Items.Add(new TrackRow(item));
        LibraryStatusText.Text = $"{list.Count} 首";
    }

    private void OnLibraryDoubleClick(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is TrackRow row)
        {
            // 双击：以当前列表整体入队（保证上一首/下一首可连续）
            var all = LibraryList.Items.OfType<TrackRow>().Select(r => r.Item).ToList();
            int idx = Math.Max(0, LibraryList.Items.IndexOf(row));
            PlayList(all, idx);
        }
    }

    private void OnRepeatChanged(object sender, SelectionChangedEventArgs e)
    {
        _queue.Repeat = (RepeatMode)Math.Clamp(RepeatCombo.SelectedIndex, 0, 2);
        SaveSettingsFromUi();
    }

    // ================= 在线 / Navidrome =================

    private async void OnOnlineSearchClick(object sender, RoutedEventArgs e)
    {
        string kw = OnlineSearchBox.Text?.Trim() ?? "";
        if (kw.Length == 0) return;
        OnlineList.Items.Clear();
        OnlineList.Items.Add(new TrackRow(new MediaItem { Title = "搜索中…", Artist = "", Kind = MediaSourceKind.Online, Uri = "" }));
        var results = await _netease.SearchAsync(kw);
        OnlineList.Items.Clear();
        foreach (var it in results)
            OnlineList.Items.Add(new TrackRow(it));
        if (results.Count == 0)
            Log("[在线音源] 无结果或接口暂不可用（第三方非官方）");
    }

    private void OnOnlineSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            OnOnlineSearchClick(sender, e);
    }

    private async void OnNaviTestClick(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        _navidrome.Configure(_settings.NavidromeServer, _settings.NavidromeUser, _settings.NavidromePassword);
        NaviStatusText.Text = "测试中…";
        bool ok = await _navidrome.TestAsync();
        NaviStatusText.Text = ok ? "连接成功" : "连接失败";
        Log(ok ? "[Navidrome] 测试连接成功" : "[Navidrome] 测试连接失败（检查地址/用户名/密码）");
    }

    private async void OnNaviSearchClick(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        _navidrome.Configure(_settings.NavidromeServer, _settings.NavidromeUser, _settings.NavidromePassword);
        string kw = OnlineSearchBox.Text?.Trim() ?? "";
        if (kw.Length == 0) { NaviStatusText.Text = "请在搜索框输入关键词"; return; }
        OnlineList.Items.Clear();
        var results = await _navidrome.SearchSongsAsync(kw);
        foreach (var it in results)
            OnlineList.Items.Add(new TrackRow(it));
        NaviStatusText.Text = $"{results.Count} 条";
    }

    private async void OnNaviAlbumClick(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        _navidrome.Configure(_settings.NavidromeServer, _settings.NavidromeUser, _settings.NavidromePassword);
        OnlineList.Items.Clear();
        var results = await _navidrome.GetAlbumSongsAsync();
        foreach (var it in results)
            OnlineList.Items.Add(new TrackRow(it));
        NaviStatusText.Text = $"{results.Count} 条（最新专辑）";
    }

    private void OnOnlineDoubleClick(object sender, RoutedEventArgs e)
    {
        if (OnlineList.SelectedItem is TrackRow row && row.Item.IsValid)
        {
            var all = OnlineList.Items.OfType<TrackRow>().Where(r => r.Item.IsValid).Select(r => r.Item).ToList();
            int idx = Math.Max(0, OnlineList.Items.IndexOf(row));
            PlayList(all, idx);
        }
    }

    // ================= 播放队列驱动 =================

    /// <summary>用一组曲目入队并从 startIndex 开始播放。</summary>
    private void PlayList(System.Collections.Generic.List<MediaItem> items, int startIndex)
    {
        if (items.Count == 0) return;
        _queue.Load(items, startIndex);
    }

    private void OnQueueCurrentChanged(MediaItem? item) => StartCurrent();

    /// <summary>把队列当前曲目交给内置播放器并激活内置路由（统一入口）。</summary>
    private void StartCurrent()
    {
        var item = _queue.Current;
        if (item is null)
        {
            // 队列空：停止内置并切回系统 SMTC
            _builtin.Stop();
            (_session as MediaSessionRouter)?.ReleaseToSystem();
            return;
        }

        // 切歌：先应用均衡器 af 链，再加载
        ApplyEqToPlayer();
        _builtin.Play(item);
        (_session as MediaSessionRouter)?.ActivateBuiltin();

        HighlightCurrent(item);
    }

    private void OnBuiltinEnded()
    {
        if (_queue.TryAutoAdvance(out var next) && next is not null)
        {
            StartCurrent(); // TryAutoAdvance 已把 current 移到下一首
        }
        else
        {
            // 播完停止：回退系统会话
            _builtin.Stop();
            (_session as MediaSessionRouter)?.ReleaseToSystem();
            Log("[内置播放] 队列已播完");
        }
    }

    private void PlayQueueMove(int delta)
    {
        var next = delta > 0 ? _queue.MoveNext() : _queue.MovePrevious();
        if (next is not null) StartCurrent();
        else
        {
            _builtin.Stop();
            (_session as MediaSessionRouter)?.ReleaseToSystem();
        }
    }

    private void HighlightCurrent(MediaItem item)
    {
        foreach (var list in new[] { LibraryList, OnlineList })
        {
            foreach (var o in list.Items)
                if (o is TrackRow row2 && ReferenceEquals(row2.Item, item))
                {
                    list.SelectedItem = o;
                    break;
                }
        }
    }

    // ================= 均衡器 =================

    private void BuildEqPanel()
    {
        var freqs = EqualizerPreset.Frequencies;
        _eqSliders = new Slider[freqs.Length];
        _eqValueLabels = new TextBlock[freqs.Length];

        for (int i = 0; i < freqs.Length; i++)
        {
            int band = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var label = new TextBlock
            {
                Text = FreqLabel(freqs[i]),
                Width = 64,
                Foreground = (Brush)FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            var slider = new Slider
            {
                Minimum = -12, Maximum = 12, Width = 360, VerticalAlignment = VerticalAlignment.Center,
                Value = 0,
            };
            var value = new TextBlock
            {
                Width = 48, Margin = new Thickness(10, 0, 0, 0), FontSize = 12,
                Foreground = (Brush)FindResource("TextSecondary"), VerticalAlignment = VerticalAlignment.Center,
            };
            slider.ValueChanged += (_, _) => OnEqBandChanged(band, slider.Value, value);

            row.Children.Add(label);
            row.Children.Add(slider);
            row.Children.Add(value);
            EqPanel.Items.Add(row);

            _eqSliders[i] = slider;
            _eqValueLabels[i] = value;
        }
    }

    private static string FreqLabel(double hz) =>
        hz >= 1000 ? $"{hz / 1000:0}kHz" : $"{hz:0}Hz";

    private void LoadEqToUi()
    {
        _eqLoading = true;
        for (int i = 0; i < _eqSliders.Length && i < _settings.EqualizerGains.Length; i++)
        {
            _eqSliders[i].Value = Math.Clamp(_settings.EqualizerGains[i], -12, 12);
            _eqValueLabels[i].Text = $"{_settings.EqualizerGains[i]:0.#} dB";
        }
        _eqLoading = false;
    }

    private void OnEqBandChanged(int band, double value, TextBlock label)
    {
        if (_eqLoading) return;
        value = Math.Round(value * 2) / 2.0; // 0.5dB 步进
        label.Text = $"{value:0.#} dB";
        _settings.EqualizerGains[band] = value;
        SaveSettingsFromUi();
        ApplyEqToPlayer();
    }

    private void OnEqualizerEnabledChanged(object sender, RoutedEventArgs e)
    {
        _settings.EqualizerEnabled = EqualizerEnabledCheck.IsChecked == true;
        SaveSettingsFromUi();
        ApplyEqToPlayer();
        Log(_settings.EqualizerEnabled ? "[均衡器] 已启用" : "[均衡器] 已关闭（旁路）");
    }

    private void OnEqResetClick(object sender, RoutedEventArgs e)
    {
        Array.Clear(_settings.EqualizerGains);
        LoadEqToUi();
        SaveSettingsFromUi();
        ApplyEqToPlayer();
    }

    /// <summary>按当前开关与增益构建 af 链并应用到内置播放器。</summary>
    private void ApplyEqToPlayer()
    {
        var eq = _settings.EqualizerEnabled ? EqualizerPreset.From(_settings.EqualizerGains) : EqualizerPreset.From(null);
        var surround = SurroundEffect.FromSettings(
            _settings.SurroundEnabled, _settings.SurroundWidth, _settings.SurroundSpeed);
        string chain = EffectAf.BuildChain(eq, surround);
        _builtin.ApplyEqualizer(chain);
        if (string.IsNullOrEmpty(chain))
            Log("[音效] 当前均衡器平直且未启用环绕（已旁路）");
    }

    // ================= DeskBox 桌面整理 =================

    private void OnDeskBoxToggled(object sender, RoutedEventArgs e)
    {
        if (DeskBoxEnabledCheck.IsChecked == true)
        {
            _deskData = DeskBoxStore.Load();
            _deskData.ShowTodo = DeskBoxTodoCheck.IsChecked == true;
            _deskData.ShowNote = DeskBoxNoteCheck.IsChecked == true;
            _deskData.ShowMusic = DeskBoxMusicCheck.IsChecked == true;
            StartDeskBoxOverlays();
            DeskBoxStatusText.Text = "格子层已开启";
        }
        else
        {
            StopDeskBoxOverlays();
            DeskBoxStatusText.Text = "格子层已关闭";
        }
    }

    private void StartDeskBoxOverlays()
    {
        StopDeskBoxOverlays();
        IntPtr workerW = WallpaperHost.FindOrCreateWorkerW();
        if (workerW == IntPtr.Zero)
        {
            DeskBoxStatusText.Text = "未找到桌面宿主（请先应用壁纸）";
            return;
        }

        foreach (var screen in GetScreens())
        {
            string key = $"{(int)screen.X},{(int)screen.Y},{(int)screen.Width},{(int)screen.Height}";
            var overlay = new DeskBoxOverlayWindow(screen, _deskData, key, _session, () => DeskBoxStore.Save(_deskData));
            overlay.Show();
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(overlay).EnsureHandle();

            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
                new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | 0x00000020));
            NativeMethods.SetParent(hwnd, workerW);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                (int)screen.X, (int)screen.Y, (int)screen.Width, (int)screen.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            _deskOverlays.Add(overlay);
        }
        DeskBoxStatusText.Text = $"已在 {_deskOverlays.Count} 个屏幕开启格子层";
    }

    private void StopDeskBoxOverlays()
    {
        foreach (var o in _deskOverlays)
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(o).Handle;
                if (hwnd != IntPtr.Zero) NativeMethods.SetParent(hwnd, IntPtr.Zero);
                o.Close();
            }
            catch { }
        }
        _deskOverlays.Clear();
    }

    private void OnDeskAddFileClick(object sender, RoutedEventArgs e)
    {
        if (_deskOverlays.Count == 0) { DeskBoxStatusText.Text = "请先开启格子层"; return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "选择要收纳为格子的文件/文件夹" };
        // 文件夹用 FolderBrowser 更合适；这里文件即可
        if (dlg.ShowDialog(this) == true)
            foreach (var f in dlg.FileNames)
                _deskOverlays[0].AddFileTile(f);
        DeskBoxStore.Save(_deskData);
    }

    private void OnDeskResetClick(object sender, RoutedEventArgs e)
    {
        DeskBoxStore.Reset();
        _deskData = new DeskBoxData();
        StartDeskBoxOverlays();
        DeskBoxStatusText.Text = "布局已重置";
    }

    private List<Rect> GetScreens()
    {
        var list = new List<Rect>();
        try
        {
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                list.Add(new Rect(b.X, b.Y, b.Width, b.Height));
            }
        }
        catch
        {
            list.Add(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight));
        }
        return list;
    }

    // ================= 系统优化 =================

    private void BuildOpsPanel()
    {
        _opChecks.Clear();
        _opById.Clear();
        OpsPanel.Items.Clear();

        string? lastCat = null;
        foreach (var item in OpsCatalog.All)
        {
            _opById[item.Id] = item;
            if (item.Category != lastCat)
            {
                lastCat = item.Category;
                OpsPanel.Items.Add(new TextBlock
                {
                    Text = "■ " + item.Category,
                    Foreground = (Brush)FindResource("TextSecondary"),
                    Margin = new Thickness(0, 10, 0, 4),
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                });
            }

            var chk = new CheckBox { Content = item.Name, Margin = new Thickness(0, 2, 0, 0) };
            var badge = new TextBlock
            {
                Text = item.RiskText,
                Foreground = item.Risk switch
                {
                    RiskLevel.Green => Brushes.LightGreen,
                    RiskLevel.Yellow => Brushes.Khaki,
                    _ => Brushes.LightCoral,
                },
                FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
            };
            var desc = new TextBlock
            {
                Text = item.Desc,
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11, Margin = new Thickness(24, 1, 0, 6), TextWrapping = TextWrapping.Wrap,
            };
            var host = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(chk);
            head.Children.Add(badge);
            host.Children.Add(head);
            host.Children.Add(desc);
            OpsPanel.Items.Add(host);

            chk.Tag = item.Id;
            _opChecks.Add(chk);   // 注意：默认全不勾选
        }
    }

    private IEnumerable<OptItem> SelectedOps()
    {
        foreach (var chk in _opChecks)
            if (chk.IsChecked == true && chk.Tag is string id && _opById.TryGetValue(id, out var item))
                yield return item;
    }

    private async void OnOpsExecuteClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedOps().ToList();
        if (selected.Count == 0) { OptLog("请先勾选要执行的优化项"); return; }

        var confirm = MessageBox.Show(
            $"即将以管理员权限执行 {selected.Count} 项系统修改。\n建议先创建系统还原点。是否继续？",
            "确认系统修改", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) { OptLog("已取消执行"); return; }

        if (!ElevationRunner.IsAdmin())
            OptLog("提示：当前可能非管理员，后续每项都会弹 UAC 提权。");

        foreach (var item in selected)
        {
            OptLog($"▶ [{item.Id}] {item.Name} ...");
            var (ok, output) = await Task.Run(() => ElevationRunner.Run(item.Apply, elevated: true, logTag: item.Id));
            OptLog(ok ? $"  ✔ 成功：{Trim(output)}" : $"  ✗ 失败：{Trim(output)}");
        }
        OptLog("批量执行结束。");
    }

    private async void OnOpsRevertClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedOps().ToList();
        if (selected.Count == 0) { OptLog("请先勾选要撤销的优化项"); return; }

        OptLog($"撤销 {selected.Count} 项 ...");
        foreach (var item in selected)
        {
            var (ok, output) = await Task.Run(() => ElevationRunner.Run(item.Revert, elevated: true, logTag: item.Id));
            OptLog(ok ? $"  ↩ 已撤销 [{item.Id}]" : $"  ↩ 撤销失败 [{item.Id}]：{Trim(output)}");
        }
    }

    private async void OnAppxLoadClick(object sender, RoutedEventArgs e)
    {
        OptLog("正在读取已安装应用...");
        var list = await SystemAppsService.ListAppxAsync();
        AppxList.Items.Clear();
        foreach (var p in list) AppxList.Items.Add(p);
        AppxList.DisplayMemberPath = nameof(AppxPackage.Display);
        OptLog($"已列出 {list.Count} 个应用（黄色为建议保留项）。");
    }

    private async void OnAppxUninstallClick(object sender, RoutedEventArgs e)
    {
        if (AppxList.SelectedItem is not AppxPackage pkg) { OptLog("请先在列表选中应用"); return; }
        if (pkg.SystemComponent)
        {
            var c = MessageBox.Show($"{pkg.Name} 标记为建议保留，确定要卸载吗？", "谨慎卸载",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (c != MessageBoxResult.Yes) return;
        }
        OptLog($"卸载 {pkg.Name} ...");
        var (ok, outp) = await SystemAppsService.UninstallAppxAsync(pkg.FullName);
        OptLog(ok ? "✔ 卸载完成" : "失败：" + Trim(outp));
    }

    private async void OnWingetInstallClick(object sender, RoutedEventArgs e)
    {
        string id = WingetSearchBox.Text?.Trim() ?? "";
        if (id.Length == 0) { OptLog("请在框内输入要安装的 winget 包 ID"); return; }
        OptLog($"winget 安装 {id} ...");
        var (ok, outp) = await SystemAppsService.WingetInstallAsync(id);
        OptLog(ok ? "✔ 安装完成" : "失败：" + Trim(outp));
    }

    private void OptLog(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        if (OptLogBox.Dispatcher.CheckAccess())
        {
            OptLogBox.AppendText(line + Environment.NewLine);
            OptLogBox.ScrollToEnd();
        }
        else OptLogBox.Dispatcher.BeginInvoke(() => { OptLogBox.AppendText(line + Environment.NewLine); OptLogBox.ScrollToEnd(); });
    }

    private static string Trim(string s) =>
        s.Length <= 300 ? s : s[..300] + "…";

    // ================= 空闲内存 / 频谱按需（B1/B2/B3/B4） =================

    /// <summary>真正空闲：降帧开启 且 无播放 且 媒体条/桌面歌词/全屏歌词均未显示。</summary>
    private bool IsReallyIdle() =>
        _settings.IdleReduceFps
        && !_session.IsPlaying
        && _mediaBar is null
        && _lyricsWindow is null
        && _fullscreenLyrics is null;

    private void OnIdleTick()
    {
        // B2：未播放时暂停频谱 FFT（若壁纸+频谱原本开启）
        if (_settings.SpectrumEnabled && _wallpaper.IsRunning && _spectrum.IsRunning)
        {
            _spectrum.Stop();
            _spectrumAutoStopped = true;
        }
    }

    private void OnActiveTick()
    {
        // 恢复播放时按需重启频谱
        if (_spectrumAutoStopped && _settings.SpectrumEnabled && _wallpaper.IsRunning)
        {
            _spectrum.Start();
            _spectrumAutoStopped = false;
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

        // 动态配色：封面主色联动桌面歌词/全屏歌词（媒体条自带封面渐变）
        if (e.PropertyName == nameof(IMediaSession.Thumbnail) && _settings.DynamicThemeEnabled)
            ApplyDynamicTheme();
    }

    private void ApplyDynamicTheme()
    {
        if (_session.Thumbnail is not System.Windows.Media.Imaging.BitmapSource bmp) return;
        Color c = Views.CoverPalette.Extract(bmp);
        _lyricsWindow?.ApplyStyleSettings(); // 桌面歌词用 AccentColor；这里联动全屏
        _fullscreenLyrics?.ApplyAccent(c);
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => _session.Previous();
    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _session.TogglePlayPause();
    private void OnNextClick(object sender, RoutedEventArgs e) => _session.Next();

    private void RefreshSessionUi()
    {
        TitleBarText.Text = _session.Title == "--" ? "未在播放" : $"{_session.Title} — {_session.Artist}";
        bool builtinActive = (_session as MediaSessionRouter)?.BuiltinActive ?? false;
        SessionBadgeText.Text = builtinActive ? "内置播放器" : (_session.IsReal ? "SMTC 真实会话" : "演示模式");
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
        _idle?.Stop();
        StopDeskBoxOverlays();
        _queue.CurrentChanged -= OnQueueCurrentChanged;
        _lyricsWindow?.Stop();
        _fullscreenLyrics?.Stop();
        _mediaBar?.Close();
        _spectrum.Stop();
        _wallpaper.Dispose();
        _session.PropertyChanged -= OnSessionChanged;
        SaveResumeIfNeeded();
        SaveSettingsFromUi();
        base.OnClosed(e);
    }

    /// <summary>列表行包装：给 ListView 提供显示字段并持有底层 MediaItem。</summary>
    private sealed class TrackRow
    {
        public MediaItem Item { get; }
        public TrackRow(MediaItem item) => Item = item;

        public string Title => Item.Title;
        public string Artist => Item.Artist;
        public string? Album => Item.Album;
        public string Provider => Item.Provider;
        public string DurationText => Item.DurationSec > 1
            ? TimeSpan.FromSeconds(Item.DurationSec).ToString(@"m\:ss")
            : "--:--";
    }
}
