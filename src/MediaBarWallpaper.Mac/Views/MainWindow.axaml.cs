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
using MediaBarWallpaper.Mac.Services;
using MediaBarWallpaper.Mac.Views;
using MediaBarWallpaper.Models;
using MediaBarWallpaper.Services.Equalizer;
using MediaBarWallpaper.Services.Playback;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;
using Orient = Avalonia.Layout.Orientation;

namespace MediaBarWallpaper.Mac.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _app;
    private readonly ObservableCollection<MediaItem> _queue = new();
    private readonly ObservableCollection<MediaItem> _library = new();
    private readonly ObservableCollection<MediaItem> _online = new();
    private readonly ObservableCollection<MediaItem> _nav = new();

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

        BuildEqualizerSliders();
        LoadSettingsIntoUi();

        _app.StateChanged += OnStateChanged;
        _app.Log += msg => EnvStatusLabel.Text = msg;

        _lyricsWindow = new LyricsOverlayWindow(_app);
        _fsLyricsWindow = new FullscreenLyricsWindow(_app);

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _uiTimer.Tick += (_, _) => RefreshPlaybackUi();
        _uiTimer.Start();

        RefreshPlaybackUi();
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

    protected override void OnClosed(EventArgs e)
    {
        _uiTimer.Stop();
        _lyricsWindow?.Close();
        _fsLyricsWindow?.Close();
        base.OnClosed(e);
    }
}
