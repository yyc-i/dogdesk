using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DogDesk.Mac.Services;
using DogDesk.Mac.Views;
using DogDesk.Models;
using DogDesk.Services.Effect;
using DogDesk.Services.Equalizer;
using DogDesk.Services.MusicSource;
using DogDesk.Services.Playback;
using DogDesk.Services.Playlists;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;
using Orient = Avalonia.Layout.Orientation;

namespace DogDesk.Mac.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _app;
    private readonly ObservableCollection<MediaItem> _queue = new();
    private readonly ObservableCollection<MediaItem> _library = new();
    private readonly ObservableCollection<MediaItem> _online = new();
    private readonly ObservableCollection<MediaItem> _nav = new();
    private readonly ObservableCollection<DogDeskPlaylist> _playlists = new();
    private readonly ObservableCollection<MediaItem> _playlistItems = new();

    private readonly DispatcherTimer _uiTimer;
    private readonly LyricsOverlayWindow _lyricsWindow;
    private readonly FullscreenLyricsWindow _fsLyricsWindow;

    private readonly Slider[] _eqSliders = new Slider[EqualizerPreset.BandCount];
    private bool _seeking;
    private bool _suppressEvents;
    private Bitmap? _coverBmp;

    public MainWindow(AppServices app)
    {
        _app = app;
        InitializeComponent();

        QueueList.ItemsSource = _queue;
        LibraryList.ItemsSource = _library;
        OnlineList.ItemsSource = _online;
        NavList.ItemsSource = _nav;
        PlaylistList.ItemsSource = _playlists;
        PlaylistItemsList.ItemsSource = _playlistItems;

        BuildRadioAndChartButtons();
        BuildEqualizerSliders();
        LoadSettingsIntoUi();
        RefreshPlaylistsUi();

        _app.StateChanged += OnStateChanged;
        _app.Log += msg => EnvStatusLabel.Text = msg;

        _lyricsWindow = new LyricsOverlayWindow(_app);
        _fsLyricsWindow = new FullscreenLyricsWindow(_app);

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _uiTimer.Tick += (_, _) => RefreshPlaybackUi();
        _uiTimer.Start();

        // 记忆播放：启动恢复
        if (_app.RestoreOnStartup())
            ResumeHint.Text = "已恢复上次播放队列";

        RefreshPlaybackUi();
    }

    // ============ 电台 / 榜单 ============
    private void BuildRadioAndChartButtons()
    {
        foreach (var (genre, _) in RadioService.Genres)
        {
            string g = genre;
            RadioPanel.Children.Add(new Button { Content = g, Padding = new Thickness(8, 3), Margin = new Thickness(2) });
            ((Button)RadioPanel.Children[^1]).Click += async (_, _) => await OnRadioSelected(g);
        }

        foreach (var (id, name) in ChartsCatalog.NeteaseCharts)
        {
            string cid = id; string cname = name;
            ChartPanel.Children.Add(new Button { Content = cname, Padding = new Thickness(8, 3), Margin = new Thickness(2) });
            ((Button)ChartPanel.Children[^1]).Click += async (_, _) => await OnChartSelected(cid, cname);
        }
    }

    private async Task OnRadioSelected(string genre)
    {
        ResumeHint.Text = $"正在聚合「{genre}」电台…";
        var items = await _app.Radio.BuildQueueAsync(genre, 60);
        _online.Clear();
        foreach (var it in items) _online.Add(it);
        ResumeHint.Text = items.Count > 0 ? $"电台「{genre}」：{items.Count} 首（双击播放）" : "电台暂无结果";
    }

    private async Task OnChartSelected(string chartId, string name)
    {
        ResumeHint.Text = $"正在加载榜单「{name}」…";
        var items = await _app.Netease.GetChartAsync(chartId, 30);
        _online.Clear();
        foreach (var it in items) _online.Add(it);
        ResumeHint.Text = items.Count > 0 ? $"榜单「{name}」：{items.Count} 首" : "榜单暂无结果";
    }

    // ============ 播放 ============
    private void Play_Click(object? sender, RoutedEventArgs e) => _app.TogglePlayPause();

    private void Next_Click(object? sender, RoutedEventArgs e) => _app.Next();

    private void Prev_Click(object? sender, RoutedEventArgs e) => _app.Previous();

    private void Repeat_Click(object? sender, RoutedEventArgs e)
    {
        _app.ToggleRepeat();
        RepeatBtn.Content = _app.Queue.Repeat switch
        {
            RepeatMode.All => "循环: 列表",
            RepeatMode.One => "循环: 单曲",
            _ => "循环: 关",
        };
    }

    private void Progress_PointerPressed(object? sender, PointerPressedEventArgs e) => _seeking = true;
    private void Progress_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _seeking = false;
        _app.Seek(ProgressSlider.Value / 1000.0);
    }

    private void Progress_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_seeking) TimeLabel.Text = FormatTime(e.NewValue / 1000.0);
    }

    private void QueueList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (QueueList.SelectedItem is MediaItem item)
        {
            int idx = _app.Queue.Items.ToList().IndexOf(item);
            if (idx >= 0) _app.PlayAt(idx);
        }
    }

    private void OnStateChanged()
    {
        Dispatcher.UIThread.Post(RefreshPlaybackUi);
    }

    private void RefreshPlaybackUi()
    {
        MediaItem? cur = _app.Queue.Current;
        if (cur is null)
        {
            NowPlayingTitle.Text = "未在播放";
            NowPlayingArtist.Text = "—";
        }
        else
        {
            NowPlayingTitle.Text = cur.Title;
            NowPlayingArtist.Text = string.IsNullOrWhiteSpace(cur.Artist) ? cur.Provider : $"{cur.Artist} · {cur.Provider}";
        }

        PlayBtn.Content = _app.Player.IsPlaying ? "⏸ 暂停" : "▶ 播放";
        MpvStatus.Text = _app.MpvStatus;

        if (!_seeking)
            ProgressSlider.Value = _app.Player.Progress01 * 1000.0;

        // 记忆播放：曲目 duration 就绪后应用一次恢复位置
        double pendingSeek = _app.ConsumePendingSeek();
        if (pendingSeek > 1 && _app.Player.DurationSec > 5)
            _app.Seek(pendingSeek / _app.Player.DurationSec);

        TimeLabel.Text = $"{FormatTime(_app.Player.PositionSec)} / {FormatTime(_app.Player.DurationSec)}";

        // 封面
        byte[]? thumb = _app.Player.ThumbnailBytes;
        if (thumb is not null)
        {
            try
            {
                _coverBmp?.Dispose();
                using var ms = new MemoryStream(thumb);
                _coverBmp = new Bitmap(ms);
                CoverImage.Source = _coverBmp;
                if (_app.Settings.Current.DynamicThemeEnabled)
                    ApplyDynamicTheme(DominantColor(thumb));
            }
            catch { /* 封面解码失败忽略 */ }
        }

        // 高亮当前曲
        if (cur is not null && QueueList.SelectedItem != cur)
            QueueList.SelectedItem = cur;
    }

    private static string FormatTime(double sec)
    {
        if (sec <= 0 || double.IsNaN(sec)) return "0:00";
        int m = (int)(sec / 60);
        int s = (int)(sec % 60);
        return $"{m}:{s:00}";
    }

    /// <summary>
    /// 从封面字节确定性派生主色（无像素解码依赖；为动态配色主题降级实现）。
    /// </summary>
    private static Color DominantColor(byte[]? cover)
    {
        int seed = 0x818CF8;
        if (cover is { Length: > 0 })
        {
            int sum = 0;
            for (int i = 0; i < cover.Length; i += 997) sum += cover[i];
            seed = sum;
        }
        byte r = (byte)((seed * 53 + 40) % 190 + 45);
        byte g = (byte)((seed * 97 + 80) % 170 + 45);
        byte b = (byte)((seed * 31 + 120) % 215 + 45);
        return Color.FromRgb(r, g, b);
    }

    private void ApplyDynamicTheme(Color c)
    {
        Color darker = Color.FromRgb((byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3));
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        brush.GradientStops.Add(new GradientStop(c, 0));
        brush.GradientStops.Add(new GradientStop(darker, 1));
        RootTabs.Background = brush;
    }

    // ============ 曲库 ============
    private async void PickLibrary_Click(object? sender, RoutedEventArgs e)
    {
        var dir = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择本地音乐文件夹",
            AllowMultiple = false,
        });
        if (dir.Count > 0)
        {
            string path = dir[0].Path.LocalPath;
            LibraryFolderBox.Text = path;
            _app.Settings.Current.MusicLibraryFolder = path;
            _app.Settings.Save();
            ScanLibrary_Click(sender, e);
        }
    }

    private void ScanLibrary_Click(object? sender, RoutedEventArgs e)
    {
        string folder = LibraryFolderBox.Text ?? "";
        var items = MusicLibraryScanner.Scan(folder);
        _library.Clear();
        foreach (var it in items) _library.Add(it);
        LibrarySummary.Text = $"共扫描到 {items.Count} 首本地曲目（双击加入播放队列并播放）";
    }

    private void LibraryList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (LibraryList.SelectedItem is MediaItem item)
        {
            _app.PlayList(_library, _library.IndexOf(item));
        }
    }

    // ============ 在线音源 ============
    private async void NeteaseSearch_Click(object? sender, RoutedEventArgs e)
    {
        string kw = NeteaseSearchBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(kw)) return;
        _online.Clear();
        var results = await _app.Netease.SearchAsync(kw);
        foreach (var r in results) _online.Add(r);
        if (results.Count == 0)
            _online.Add(new MediaItem { Title = "（无结果或在线接口暂不可用）", Artist = "", Uri = "" });
    }

    private void NeteaseSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) NeteaseSearch_Click(sender, new RoutedEventArgs());
    }

    private void OnlineList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (OnlineList.SelectedItem is MediaItem item && item.IsValid)
            _app.PlayList(_online.Where(x => x.IsValid).ToList(), _online.IndexOf(item));
    }

    private async void NavTest_Click(object? sender, RoutedEventArgs e)
    {
        _app.Navidrome.Configure(NavServerBox.Text ?? "", NavUserBox.Text ?? "", NavPassBox.Text ?? "");
        bool ok = await _app.Navidrome.TestAsync();
        NavStatus.Text = ok ? "✓ 连接成功" : "✗ 连接失败（检查服务器/用户名/密码）";
    }

    private async void NavLoad_Click(object? sender, RoutedEventArgs e)
    {
        _app.Navidrome.Configure(NavServerBox.Text ?? "", NavUserBox.Text ?? "", NavPassBox.Text ?? "");
        _app.Settings.Current.NavidromeServer = NavServerBox.Text ?? "";
        _app.Settings.Current.NavidromeUser = NavUserBox.Text ?? "";
        _app.Settings.Current.NavidromePassword = NavPassBox.Text ?? "";
        _app.Settings.Save();

        _nav.Clear();
        var albums = await _app.Navidrome.GetAlbumSongsAsync();
        foreach (var s in albums) _nav.Add(s);
        NavStatus.Text = albums.Count > 0 ? $"已载入 {albums.Count} 首（来自最新专辑）" : "未获取到歌曲（检查连接）";
    }

    private void NavList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (NavList.SelectedItem is MediaItem item && item.IsValid)
            _app.PlayList(_nav.Where(x => x.IsValid).ToList(), _nav.IndexOf(item));
    }

    // ============ 均衡器 ============
    private void BuildEqualizerSliders()
    {
        for (int i = 0; i < EqualizerPreset.BandCount; i++)
        {
            int band = i;
            var panel = new StackPanel { Spacing = 4, Width = 64 };
            var slider = new Slider
            {
                Orientation = Orient.Vertical,
                Minimum = -12,
                Maximum = 12,
                Height = 160,
                VerticalAlignment = VAlign.Stretch,
            };
            slider.ValueChanged += (_, _) => OnEqSliderChanged(band);
            _eqSliders[i] = slider;

            string freq = EqualizerPreset.Frequencies[i] >= 1000
                ? $"{EqualizerPreset.Frequencies[i] / 1000.0:0.#}k"
                : $"{EqualizerPreset.Frequencies[i]:0}";
            panel.Children.Add(new Border { Child = slider, Height = 160 });
            panel.Children.Add(new TextBlock { Text = freq, HorizontalAlignment = HAlign.Center, FontSize = 11 });
            EqPanel.Children.Add(panel);
        }
    }

    private void OnEqSliderChanged(int band)
    {
        if (_suppressEvents) return;
        double v = _eqSliders[band].Value;
        _app.Settings.Current.EqualizerGains[band] = Math.Round(v, 1);
        EqValueLabel.Text = $"频段 {band}: {v:+0.0;-0.0;0.0} dB";
        _app.Settings.Save();
        _app.ApplyEqualizer();
    }

    private void EqEnabled_Toggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.EqualizerEnabled = EqEnabledSwitch.IsChecked == true;
        _app.Settings.Save();
        _app.ApplyEqualizer();
    }

    private void EqReset_Click(object? sender, RoutedEventArgs e)
    {
        _suppressEvents = true;
        for (int i = 0; i < EqualizerPreset.BandCount; i++)
        {
            _eqSliders[i].Value = 0;
            _app.Settings.Current.EqualizerGains[i] = 0;
        }
        _suppressEvents = false;
        _app.Settings.Save();
        _app.ApplyEqualizer();
    }

    // ============ 设置 ============
    private void LoadSettingsIntoUi()
    {
        _suppressEvents = true;
        var s = _app.Settings.Current;
        LibraryFolderBox.Text = s.MusicLibraryFolder;
        LyricsEnabledSwitch.IsChecked = s.LyricsEnabled;
        LyricsOpacitySlider.Value = Math.Clamp(s.LyricsOpacity, 0.3, 1);
        FsLyricsEnabledSwitch.IsChecked = s.FullscreenLyricsEnabled;
        FsStyleBox.SelectedIndex = (int)s.LyricsAnimationStyle;
        FsOpacitySlider.Value = Math.Clamp(s.FullscreenLyricsOpacity, 0.4, 1);
        DynamicThemeSwitch.IsChecked = s.DynamicThemeEnabled;
        EqEnabledSwitch.IsChecked = s.EqualizerEnabled;

        // v0.6 音效
        SpeedSlider.Value = Math.Clamp(s.PlaybackSpeed, 0.5, 2.0);
        SpeedLabel.Text = $"{s.PlaybackSpeed:0.00}x";
        QualityBox.SelectedIndex = (int)s.QualityTier;
        SurroundSwitch.IsChecked = s.SurroundEnabled;
        SurroundWidthSlider.Value = Math.Clamp(s.SurroundWidth, 0, 6);
        SurroundSpeedSlider.Value = Math.Clamp(s.SurroundSpeed, 0.1, 2);

        // v0.6 歌词增强 / 外观 / 记忆
        LyricsDualLineSwitch.IsChecked = s.LyricsDualLine;
        LyricsVerticalSwitch.IsChecked = s.LyricsVertical;
        LyricsOffsetSlider.Value = Math.Clamp(s.LyricsOffsetPercent, 0, 100);
        GlassSwitch.IsChecked = s.GlassEffectEnabled;
        ResumeSwitch.IsChecked = s.ResumePlayback;
        PanelOpacitySlider.Value = Math.Clamp(s.MainPanelOpacity, 0.3, 1);

        for (int i = 0; i < EqualizerPreset.BandCount; i++)
            _eqSliders[i].Value = EqualizerPreset.From(s.EqualizerGains).GetBand(i);

        NavServerBox.Text = s.NavidromeServer;
        NavUserBox.Text = s.NavidromeUser;
        NavPassBox.Text = s.NavidromePassword;

        EnvStatusLabel.Text = _app.MpvAvailable
            ? $"libmpv：{_app.MpvStatus}"
            : $"libmpv 未就绪：{_app.MpvStatus}。请在终端执行 brew install mpv 后重启应用。";
        _suppressEvents = false;

        ApplyLyricsWindowsVisibility();

        // v0.6 歌词增强与外观应用
        var st = _app.Settings.Current;
        _lyricsWindow.ApplyDualLine(st.LyricsDualLine);
        _lyricsWindow.ApplyVertical(st.LyricsVertical);
        _lyricsWindow.ApplyOffset(st.LyricsOffsetPercent);
        ApplyGlassAndOpacity();
    }

    private void LyricsEnabled_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.LyricsEnabled = LyricsEnabledSwitch.IsChecked == true;
        _app.Settings.Save();
        ApplyLyricsWindowsVisibility();
    }

    private void LyricsOpacity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.LyricsOpacity = e.NewValue;
        _app.Settings.Save();
        _lyricsWindow.ApplyOpacity(e.NewValue);
    }

    private void FsLyricsEnabled_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.FullscreenLyricsEnabled = FsLyricsEnabledSwitch.IsChecked == true;
        _app.Settings.Save();
        ApplyLyricsWindowsVisibility();
    }

    private void FsStyle_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || FsStyleBox.SelectedIndex < 0) return;
        _app.Settings.Current.LyricsAnimationStyle = (LyricsAnimationStyle)FsStyleBox.SelectedIndex;
        _app.Settings.Save();
        _fsLyricsWindow.ApplyStyle(_app.Settings.Current.LyricsAnimationStyle);
    }

    private void FsOpacity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.FullscreenLyricsOpacity = e.NewValue;
        _app.Settings.Save();
        _fsLyricsWindow.ApplyOpacity(e.NewValue);
    }

    private void DynamicTheme_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.DynamicThemeEnabled = DynamicThemeSwitch.IsChecked == true;
        _app.Settings.Save();
    }

    private void ApplyLyricsWindowsVisibility()
    {
        bool desktop = _app.Settings.Current.LyricsEnabled;
        bool full = _app.Settings.Current.FullscreenLyricsEnabled;
        if (desktop) _lyricsWindow.EnsureShown(); else _lyricsWindow.Hide();
        if (full) _fsLyricsWindow.EnsureShown(); else _fsLyricsWindow.Hide();
    }

    // ============ 歌单 / 收藏 ============
    private void RefreshPlaylistsUi()
    {
        _playlists.Clear();
        foreach (var p in _app.Playlists) _playlists.Add(p);

        // 歌单架：横向封面卡片（透视感 ScaleTransform）
        PlaylistShelfPanel.Children.Clear();
        foreach (var p in _app.Playlists)
        {
            var card = new Border
            {
                Width = 120,
                Height = 120,
                Margin = new Thickness(6),
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0x3a, 0x3f, 0x6a), 0),
                        new GradientStop(Color.FromRgb(0x1c, 0x1e, 0x2c), 1),
                    },
                },
                Child = new TextBlock
                {
                    Text = $"{p.Name}\n({p.Items.Count} 首)",
                    Foreground = Brushes.White,
                    Margin = new Thickness(8),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VAlign.Center,
                },
                RenderTransform = new ScaleTransform(1, 1),
            };
            PlaylistShelfPanel.Children.Add(card);
        }
    }

    private DogDeskPlaylist? _currentPlaylist;

    private void PlaylistList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PlaylistList.SelectedItem is DogDeskPlaylist pl)
        {
            _currentPlaylist = pl;
            PlaylistNameLabel.Text = $"{pl.Name}（{pl.Items.Count} 首）";
            _playlistItems.Clear();
            foreach (var it in pl.Items) _playlistItems.Add(it);
        }
    }

    private void PlaylistItems_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_currentPlaylist is null) return;
        if (PlaylistItemsList.SelectedItem is MediaItem item && item.IsValid)
            _app.PlayList(_currentPlaylist.Items.Where(x => x.IsValid).ToList(),
                          _currentPlaylist.Items.IndexOf(item));
    }

    private async void NewPlaylist_Click(object? sender, RoutedEventArgs e)
    {
        var name = await AskTextAsync("新建歌单", "歌单名称：", "我的歌单");
        if (!string.IsNullOrWhiteSpace(name))
        {
            _app.CreatePlaylist(name.Trim());
            RefreshPlaylistsUi();
        }
    }

    private void RemovePlaylistItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPlaylist is null) return;
        if (PlaylistItemsList.SelectedItem is MediaItem item)
        {
            _app.RemoveFromPlaylist(_currentPlaylist.Name, item);
            PlaylistList_SelectionChanged(null, null!);
            RefreshPlaylistsUi();
        }
    }

    private void Fav_Click(object? sender, RoutedEventArgs e)
    {
        bool favorited = _app.ToggleFavoriteCurrent();
        FavBtn.Content = favorited ? "♥ 已收藏" : "♡ 收藏";
        RefreshPlaylistsUi();
    }

    private async void AddToPlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (_app.Queue.Current is null) return;
        var names = _app.Playlists.Select(p => p.Name).ToList();
        string? pick = await AskChoiceAsync("加入歌单", names);
        if (!string.IsNullOrWhiteSpace(pick))
        {
            _app.AddToPlaylist(pick, _app.Queue.Current);
            RefreshPlaylistsUi();
        }
    }

    // ============ 音效：倍速 / 音质 / 环绕 / 预设 ============
    private void Speed_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.PlaybackSpeed = Math.Round(e.NewValue, 2);
        _app.Settings.Save();
        SpeedLabel.Text = $"{e.NewValue:0.00}x";
        _app.ApplySpeed();
    }

    private void Quality_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || QualityBox.SelectedIndex < 0) return;
        _app.Settings.Current.QualityTier = (AudioQuality)QualityBox.SelectedIndex;
        _app.Settings.Save();
    }

    private void Surround_Toggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.SurroundEnabled = SurroundSwitch.IsChecked == true;
        _app.Settings.Save();
        _app.ApplyAudioChain();
    }

    private void SurroundWidth_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.SurroundWidth = Math.Round(e.NewValue, 2);
        _app.Settings.Save();
        _app.ApplyAudioChain();
    }

    private void SurroundSpeed_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.SurroundSpeed = Math.Round(e.NewValue, 2);
        _app.Settings.Save();
        _app.ApplyAudioChain();
    }

    private async void PresetExport_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出音效预设",
            SuggestedFileName = "dogdesk-preset.json",
        });
        if (file is null) return;
        var s = _app.Settings.Current;
        var preset = new EffectPreset
        {
            Name = "DogDesk 预设",
            SurroundEnabled = s.SurroundEnabled,
            SurroundWidth = s.SurroundWidth,
            SurroundSpeed = s.SurroundSpeed,
            EqualizerGains = (double[])s.EqualizerGains.Clone(),
        };
        preset.Export(file.Path.LocalPath);
        EnvStatusLabel.Text = $"预设已导出：{file.Path.LocalPath}";
    }

    private async void PresetImport_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入音效预设",
            AllowMultiple = false,
        });
        if (files.Count == 0) return;
        var preset = EffectPreset.Import(files[0].Path.LocalPath);
        if (preset is null) { EnvStatusLabel.Text = "预设导入失败"; return; }

        _suppressEvents = true;
        var eq = preset.ToEqualizer();
        for (int i = 0; i < EqualizerPreset.BandCount; i++)
        {
            _app.Settings.Current.EqualizerGains[i] = eq.GetBand(i);
            _eqSliders[i].Value = eq.GetBand(i);
        }
        var sur = preset.ToSurround();
        _app.Settings.Current.SurroundEnabled = sur.Enabled;
        _app.Settings.Current.SurroundWidth = sur.Width;
        _app.Settings.Current.SurroundSpeed = sur.Speed;
        SurroundSwitch.IsChecked = sur.Enabled;
        SurroundWidthSlider.Value = sur.Width;
        SurroundSpeedSlider.Value = sur.Speed;
        _suppressEvents = false;

        _app.Settings.Save();
        _app.ApplyAudioChain();
        EnvStatusLabel.Text = $"已导入预设「{preset.Name}」";
    }

    // ============ 设置：歌词增强 / 外观 / 记忆 ============
    private void LyricsDualLine_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.LyricsDualLine = LyricsDualLineSwitch.IsChecked == true;
        _app.Settings.Save();
        _lyricsWindow.ApplyDualLine(_app.Settings.Current.LyricsDualLine);
    }

    private void LyricsVertical_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.LyricsVertical = LyricsVerticalSwitch.IsChecked == true;
        _app.Settings.Save();
        _lyricsWindow.ApplyVertical(_app.Settings.Current.LyricsVertical);
    }

    private void LyricsOffset_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.LyricsOffsetPercent = e.NewValue;
        _app.Settings.Save();
        _lyricsWindow.ApplyOffset(e.NewValue);
    }

    private void Glass_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.GlassEffectEnabled = GlassSwitch.IsChecked == true;
        _app.Settings.Save();
        ApplyGlassAndOpacity();
    }

    private void Resume_Toggled(object? sender, RoutedEventArgs e)
    {
        _app.Settings.Current.ResumePlayback = ResumeSwitch.IsChecked == true;
        _app.Settings.Save();
    }

    private void PanelOpacity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _app.Settings.Current.MainPanelOpacity = e.NewValue;
        _app.Settings.Save();
        ApplyGlassAndOpacity();
    }

    private void ApplyGlassAndOpacity()
    {
        Opacity = Math.Clamp(_app.Settings.Current.MainPanelOpacity, 0.3, 1.0);
        if (_app.Settings.Current.GlassEffectEnabled)
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent };
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x14, 0x16, 0x22));
        }
        else
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x18, 0x24));
        }
    }

    // ============ 简易文本输入对话框 ============
    private async Task<string?> AskTextAsync(string title, string label, string initial)
    {
        var box = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var input = new TextBox { Text = initial, Margin = new Thickness(12) };
        string? result = null;
        var ok = new Button { Content = "确定", HorizontalAlignment = HAlign.Right, Margin = new Thickness(12) };
        ok.Click += (_, _) => { result = input.Text; box.Close(); };
        box.Content = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = label, Margin = new Thickness(12, 12, 12, 0) },
                input,
                ok,
            },
        };
        await box.ShowDialog(this);
        return result;
    }

    private async Task<string?> AskChoiceAsync(string title, System.Collections.Generic.List<string> choices)
    {
        var box = new Window
        {
            Title = title,
            Width = 320,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var list = new ListBox { ItemsSource = choices };
        string? result = null;
        var ok = new Button { Content = "加入", Margin = new Thickness(12) };
        ok.Click += (_, _) => { result = list.SelectedItem as string; box.Close(); };
        box.Content = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, Margin = new Thickness(12) },
                list,
                ok,
            },
        };
        await box.ShowDialog(this);
        return result;
    }

    protected override void OnClosed(EventArgs e)
    {
        _uiTimer.Stop();
        _lyricsWindow?.Close();
        _fsLyricsWindow?.Close();
        base.OnClosed(e);
    }
}
