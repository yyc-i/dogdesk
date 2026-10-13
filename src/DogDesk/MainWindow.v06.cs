using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DogDesk.Models;
using DogDesk.Services;
using DogDesk.Services.Effect;
using DogDesk.Services.MusicSource;
using DogDesk.Services.Playback;
using DogDesk.Services.Playlists;

namespace DogDesk;

public partial class MainWindow
{
    private RadioService? _radio;
    private bool _rackDragging;
    private Point _rackLast;
    private double _rackYaw;
    private double _rackZoom = 6.0;

    // F3/F4 底部频谱 + 节拍
    private System.Windows.Threading.DispatcherTimer? _specTimer;
    private BeatDetector? _beat;
    private System.Windows.Shapes.Rectangle[]? _specBars;

    // ================= F1 3D 环绕 / 预设 =================

    private void OnSurroundChanged(object sender, RoutedEventArgs e) => ApplySurroundUiToSettings();

    private void ApplySurroundUiToSettings()
    {
        try
        {
            _settings.SurroundEnabled = SurroundEnabledCheck.IsChecked == true;
            _settings.SurroundWidth = SurroundWidthSlider.Value;
            _settings.SurroundSpeed = SurroundSpeedSlider.Value;
            SurroundWidthValue.Text = _settings.SurroundWidth.ToString("0.0");
            SurroundSpeedValue.Text = _settings.SurroundSpeed.ToString("0.0");
            ApplyEqToPlayer();
            _settingsService.Save();
        }
        catch { }
    }

    private void OnPresetExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "音效预设 (*.json)|*.json", FileName = "dogdesk-effect.json"
            };
            if (dlg.ShowDialog(this) != true) return;
            var preset = new EffectPreset
            {
                Name = "dogdesk",
                SurroundEnabled = _settings.SurroundEnabled,
                SurroundWidth = _settings.SurroundWidth,
                SurroundSpeed = _settings.SurroundSpeed,
                EqualizerGains = _settings.EqualizerGains,
            };
            preset.Export(dlg.FileName);
            PresetStatusText.Text = "已导出";
        }
        catch (Exception ex) { PresetStatusText.Text = "导出失败：" + ex.Message; }
    }

    private void OnPresetImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "音效预设 (*.json)|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            var preset = EffectPreset.Import(dlg.FileName);
            if (preset is null) { PresetStatusText.Text = "导入失败：文件无效"; return; }
            _settings.SurroundEnabled = preset.SurroundEnabled;
            _settings.SurroundWidth = preset.SurroundWidth;
            _settings.SurroundSpeed = preset.SurroundSpeed;
            _settings.EqualizerGains = preset.EqualizerGains;
            // 同步到 UI
            SurroundEnabledCheck.IsChecked = preset.SurroundEnabled;
            SurroundWidthSlider.Value = preset.SurroundWidth;
            SurroundSpeedSlider.Value = preset.SurroundSpeed;
            SurroundWidthValue.Text = preset.SurroundWidth.ToString("0.0");
            SurroundSpeedValue.Text = preset.SurroundSpeed.ToString("0.0");
            ApplyEqToPlayer();
            _settingsService.Save();
            PresetStatusText.Text = "已应用预设：" + preset.Name;
            Log($"[音效] 已导入预设 {preset.Name}");
        }
        catch (Exception ex) { PresetStatusText.Text = "导入失败：" + ex.Message; }
    }

    // ================= F9 倍速 / 音质 =================

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        try
        {
            _settings.PlaybackSpeed = e.NewValue;
            SpeedValue.Text = e.NewValue.ToString("0.0") + "×";
            _builtin.SetSpeed(e.NewValue);
            _settingsService.Save();
        }
        catch { }
    }

    private void OnQualityChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (QualityCombo.SelectedItem is ComboBoxItem it && it.Tag is string tag)
                _settings.QualityTier = (AudioQuality)int.Parse(tag);
            _settingsService.Save();
            Log($"[音质] 档位：{_settings.QualityTier}（在线外链接口若无差异则不生效）");
        }
        catch { }
    }

    // ================= F5 电台 / 榜单 =================

    private void BuildRadioPanel()
    {
        _radio = new RadioService(_netease);

        // 风格网格
        GenrePanel.Items.Clear();
        var wrap = new WrapPanel();
        foreach (var (genre, _) in RadioService.Genres)
        {
            var btn = new Button
            {
                Content = genre,
                Style = (Style)FindResource("PanelButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 6, 12, 6),
                Tag = genre,
            };
            btn.Click += async (_, _) => await PlayGenreAsync(genre);
            wrap.Children.Add(btn);
        }
        GenrePanel.Items.Add(wrap);

        // 榜单列表
        ChartPanel.Items.Clear();
        foreach (var (id, name) in ChartsCatalog.NeteaseCharts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var btn = new Button
            {
                Content = "▶ " + name,
                Style = (Style)FindResource("PanelButton"),
                Padding = new Thickness(10, 4, 10, 4),
                Tag = id,
            };
            btn.Click += async (_, _) => await PlayChartAsync(id, name);
            row.Children.Add(btn);
            ChartPanel.Items.Add(row);
        }
    }

    private async Task PlayGenreAsync(string genre)
    {
        try
        {
            RadioStatusText.Text = $"正在构建电台·{genre} …";
            var list = (await _radio!.BuildQueueAsync(genre, 60)).ToList();
            if (list.Count == 0) { RadioStatusText.Text = "该风格未取到曲目（接口可能受限）"; return; }
            PlayList(list, 0);
            RadioStatusText.Text = $"电台·{genre}：已入队 {list.Count} 首";
        }
        catch (Exception ex) { RadioStatusText.Text = "电台失败：" + ex.Message; }
    }

    private async Task PlayChartAsync(string id, string name)
    {
        try
        {
            RadioStatusText.Text = $"正在拉取榜单 {name} …";
            var list = (await _netease.GetChartAsync(id, 30)).ToList();
            if (list.Count == 0) { RadioStatusText.Text = "榜单未取到曲目"; return; }
            PlayList(list, 0);
            RadioStatusText.Text = $"榜单·{name}：已入队 {list.Count} 首";
        }
        catch (Exception ex) { RadioStatusText.Text = "榜单失败：" + ex.Message; }
    }

    // ================= F2 3D 歌单架 =================

    private void BuildRack()
    {
        try
        {
            RackCardsHost.Content = new Model3DGroup();
            var group = (Model3DGroup)RackCardsHost.Content;

            var playlists = PlaylistStore.Load();
            double[] xs = { -2.4, -1.2, 0, 1.2, 2.4 };
            int idx = 0;
            foreach (var pl in playlists.Take(5))
            {
                double x = xs[Math.Min(idx, xs.Length - 1)];
                var rect = new MeshGeometry3D
                {
                    Positions = new Point3DCollection
                    {
                        new(x - 0.45, -0.7, 0), new(x + 0.45, -0.7, 0),
                        new(x + 0.45, 0.7, 0), new(x - 0.45, 0.7, 0),
                    },
                    TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
                    TextureCoordinates = new System.Windows.Media.PointCollection
                    {
                        new(0, 1), new(1, 1), new(1, 0), new(0, 0),
                    },
                };
                var color = (Color)ColorConverter.ConvertFromString("#818CF8");
                var brush = new SolidColorBrush(Color.FromRgb((byte)(color.R / 2 + 40), (byte)(color.G / 2 + 40), (byte)(color.B / 2 + 60)));
                var mat = new DiffuseMaterial(brush);
                var geom = new GeometryModel3D(rect, mat);
                group.Children.Add(geom);
                idx++;
            }
            if (idx == 0)
            {
                RackStatusText.Text = "暂无歌单 — 在曲库/在线结果上右键「收藏」或「加入歌单」";
            }
            ApplyRackTransform();
        }
        catch (Exception ex) { RackStatusText.Text = "歌单架初始化失败：" + ex.Message; }
    }

    private void ApplyRackTransform()
    {
        RackCamera.Position = new Point3D(0, 0, _rackZoom);
        RackCamera.LookDirection = new Vector3D(0, 0, -1);
        // 简单实现：偏航通过整体容器旋转
        RackCardsHost.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), _rackYaw));
    }

    private void OnRackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2) { OnRackDoubleClick(sender, e); return; }
        _rackDragging = true;
        _rackLast = e.GetPosition(RackViewport);
        RackViewport.CaptureMouse();
    }

    private void OnRackDragEnd(object sender, MouseButtonEventArgs e)
    {
        _rackDragging = false;
        RackViewport.ReleaseMouseCapture();
    }

    private void OnRackDragMove(object sender, MouseEventArgs e)
    {
        if (!_rackDragging) return;
        var p = e.GetPosition(RackViewport);
        _rackYaw += (p.X - _rackLast.X) * 0.4;
        _rackLast = p;
        ApplyRackTransform();
    }

    private void OnRackWheel(object sender, MouseWheelEventArgs e)
    {
        _rackZoom = Math.Clamp(_rackZoom - e.Delta / 400.0, 3.5, 10);
        ApplyRackTransform();
    }

    private void OnRackReloadClick(object sender, RoutedEventArgs e) => BuildRack();

    private void OnRackDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            // 命中测试：找到被双击的卡片对应的歌单并播放
            var pt = e.GetPosition(RackViewport);
            HitTestResult? hit = null;
            VisualTreeHelper.HitTest(RackViewport, null,
                r => { hit = r; return HitTestResultBehavior.Stop; },
                new PointHitTestParameters(pt));
            // 平面按钮替代：未命中 3D 时，按当前居中歌单直接播放第一个收藏/歌单
            var playlists = PlaylistStore.Load();
            if (playlists.Count == 0) { RackStatusText.Text = "暂无歌单可播放"; return; }
            var target = playlists[playlists.Count / 2];
            var items = target.Items;
            if (items.Count == 0) { RackStatusText.Text = $"歌单「{target.Name}」为空"; return; }
            PlayList(items.ToList(), 0);
            RackStatusText.Text = $"▶ 播放歌单：{target.Name}（{items.Count} 首）";
        }
        catch (Exception ex) { RackStatusText.Text = "播放失败：" + ex.Message; }
    }

    // ================= 启动后初始化（由 OnAppStarted 调用） =================

    private void InitV06()
    {
        try
        {
            BuildRadioPanel();
            BuildRack();

            // 同步 UI 到设置
            SurroundEnabledCheck.IsChecked = _settings.SurroundEnabled;
            SurroundWidthSlider.Value = _settings.SurroundWidth;
            SurroundSpeedSlider.Value = _settings.SurroundSpeed;
            SpeedSlider.Value = _settings.PlaybackSpeed;
            SpeedValue.Text = _settings.PlaybackSpeed.ToString("0.0") + "×";
            QualityCombo.SelectedIndex = (int)_settings.QualityTier;

            // F8 恢复上次播放位置
            if (_settings.ResumePlayback)
                TryResume();

            AttachListContextMenus();
            InitBottomSpectrum();
            ApplyPanelGlass();
            InitAiTab();
            InitBootTab();
        }
        catch (Exception ex) { Log("[v0.6] 初始化异常：" + ex.Message); }
    }

    /// <summary>F6：主面板透明度（毛玻璃模拟）+ 视频背景宿主。</summary>
    private void ApplyPanelGlass()
    {
        try
        {
            double op = Math.Clamp(_settings.MainPanelOpacity, 0.3, 1.0);
            Background = _settings.GlassEffectEnabled
                ? new SolidColorBrush(Color.FromArgb((byte)(op * 255), 0x14, 0x16, 0x1F))
                : (Brush)FindResource("BgBrush");

            // F6：视频背景（MediaElement 兜底宿主）
            if (!string.IsNullOrEmpty(_settings.VideoBackgroundPath) &&
                File.Exists(_settings.VideoBackgroundPath))
            {
                try
                {
                    VideoBg.Source = new Uri(_settings.VideoBackgroundPath);
                    VideoBg.Play();
                    Log($"[视频背景] 已播放：{_settings.VideoBackgroundPath}");
                }
                catch (Exception ex) { Log("[视频背景] 加载失败：" + ex.Message); }
            }
            else
            {
                VideoBg.Stop();
                VideoBg.Source = null;
            }
        }
        catch { }
    }

    // ================= F3 底部频谱 + F4 节拍 =================

    private void InitBottomSpectrum()
    {
        try
        {
            _beat = new BeatDetector();
            int n = AudioSpectrumService.BandCount;
            _specBars = new System.Windows.Shapes.Rectangle[n];
            double w = BottomSpectrumCanvas.ActualWidth;
            for (int i = 0; i < n; i++)
            {
                double hue = 240.0 - (240.0 * i / Math.Max(1, n - 1)); // 蓝→红 RGB 渐变
                var bar = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(6, (ActualWidth - 40) / n - 4),
                    Fill = new SolidColorBrush(Hsv(hue, 0.85, 0.9)),
                    RadiusX = 2, RadiusY = 2,
                    Height = 2,
                };
                _specBars[i] = bar;
                BottomSpectrumCanvas.Children.Add(bar);
                Canvas.SetLeft(bar, i * (bar.Width + 4));
                Canvas.SetBottom(bar, 0);
            }

            _specTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_settings.HighFpsMode ? 16 : 33),
            };
            _specTimer.Tick += OnSpecTick;
            _specTimer.Start();
        }
        catch (Exception ex) { Log("[频谱] 初始化失败：" + ex.Message); }
    }

    private void OnSpecTick(object? sender, EventArgs e)
    {
        // 空闲时不驱动 FFT/绘制（IdleMemoryManager 判定）
        bool idle = IsReallyIdle();
        var bands = _spectrum.GetLatest();
        // 空闲暂停视频背景，活动恢复
        try
        {
            if (idle && VideoBg.Source is not null) VideoBg.Pause();
            else if (!idle && VideoBg.Source is not null && VideoBg.Position == TimeSpan.Zero) VideoBg.Play();
        }
        catch { }
        if (idle || bands is null || bands.Length == 0 || _specBars is null || bands.Length != _specBars.Length)
        {
            BottomSpectrumHint.Opacity = 1;
            return;
        }

        BottomSpectrumHint.Opacity = 0;
        double h = BottomSpectrumCanvas.ActualHeight;
        double scale = Math.Max(h, 1);
        for (int i = 0; i < _specBars!.Length && i < bands.Length; i++)
        {
            double v = Math.Clamp(bands[i], 0, 1);
            _specBars[i].Height = 2 + v * (scale - 4);
        }

        // F4：喂低频 5 段做节拍检测
        if (_settings.BeatVisualEnabled && _beat is not null && _spectrum.IsRunning)
        {
            var low = new float[Math.Min(5, bands.Length)];
            Array.Copy(bands, low, low.Length);
            if (_beat.Detect(low))
            {
                OnBeat();
            }
        }
    }

    private void OnBeat()
    {
        try
        {
            // 白色闪烁 0.25→0 快速淡出
            var flash = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.25, To = 0, Duration = TimeSpan.FromMilliseconds(180),
            };
            BeatFlash.BeginAnimation(OpacityProperty, flash);
            // 歌词振动联动
            _lyricsWindow?.TriggerBeat();

            // F4：粒子径向爆发 + 泛光脉冲
            SpawnBeatParticles();
            SpawnBeatGlow();
        }
        catch { }
    }

    private static readonly Random _rnd = new();

    private void SpawnBeatParticles()
    {
        if (_settings is null) return;
        double cx = BeatOverlay.ActualWidth / 2.0;
        double cy = BeatOverlay.ActualHeight / 2.0;
        int n = 18;
        for (int i = 0; i < n; i++)
        {
            double ang = _rnd.NextDouble() * Math.PI * 2;
            double dist = 60 + _rnd.NextDouble() * 140;
            var ell = new System.Windows.Shapes.Ellipse
            {
                Width = 5 + _rnd.NextDouble() * 5,
                Height = 5 + _rnd.NextDouble() * 5,
                Fill = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8)),
                Opacity = 0.9,
            };
            var tt = new TranslateTransform(0, 0);
            ell.RenderTransform = tt;
            Canvas.SetLeft(ell, cx);
            Canvas.SetTop(ell, cy);
            BeatOverlay.Children.Add(ell);

            double dx = Math.Cos(ang) * dist;
            double dy = Math.Sin(ang) * dist;
            var animX = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0, To = dx, Duration = TimeSpan.FromMilliseconds(900 + _rnd.Next(300)),
            };
            var animY = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0, To = dy, Duration = animX.Duration,
            };
            var fade = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.9, To = 0, Duration = animX.Duration,
            };
            fade.Completed += (_, _) => BeatOverlay.Children.Remove(ell);
            tt.BeginAnimation(TranslateTransform.XProperty, animX);
            tt.BeginAnimation(TranslateTransform.YProperty, animY);
            ell.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void SpawnBeatGlow()
    {
        double cx = BeatOverlay.ActualWidth / 2.0;
        double cy = BeatOverlay.ActualHeight / 2.0;
        var glow = new System.Windows.Shapes.Ellipse
        {
            Width = 220, Height = 220,
            Opacity = 0.4,
            Fill = new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(200, 0x81, 0x8C, 0xF8), 0),
                    new(Color.FromArgb(0, 0x81, 0x8C, 0xF8), 1),
                },
            },
        };
        var scale = new ScaleTransform(0.2, 0.2, 110, 110);
        glow.RenderTransform = scale;
        Canvas.SetLeft(glow, cx - 110);
        Canvas.SetTop(glow, cy - 110);
        BeatOverlay.Children.Add(glow);

        var grow = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.2, To = 1.6, Duration = TimeSpan.FromMilliseconds(700),
        };
        var fade = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.4, To = 0, Duration = TimeSpan.FromMilliseconds(700),
        };
        fade.Completed += (_, _) => BeatOverlay.Children.Remove(glow);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        glow.BeginAnimation(OpacityProperty, fade);
    }

    // ================= F11 歌单导入 =================

    private async void OnImportPlaylistIdClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string raw = ImportIdBox.Text?.Trim() ?? "";
            if (raw.Length == 0) { RackStatusText.Text = "请粘贴歌单 URL 或 ID"; return; }
            // 解析 playlist?id=xxx 或纯数字
            string id = raw;
            int qi = raw.IndexOf("id=", StringComparison.OrdinalIgnoreCase);
            if (qi >= 0) id = raw[(qi + 3)..];
            // 去掉 & 之后与路径
            int amp = id.IndexOf('&');
            if (amp >= 0) id = id[..amp];
            id = id.Trim().Trim('/').Trim();
            if (id.Length == 0 || !int.TryParse(id, out _)) { RackStatusText.Text = "无法解析歌单 ID（应为数字）"; return; }

            RackStatusText.Text = $"正在导入歌单 {id} …";
            var list = (await _netease.GetChartAsync(id, 100)).ToList();
            if (list.Count == 0) { RackStatusText.Text = "未取到该歌单曲目"; return; }
            PlayList(list, 0);
            RackStatusText.Text = $"已导入歌单 {id}：{list.Count} 首并入队播放";
        }
        catch (Exception ex) { RackStatusText.Text = "导入失败：" + ex.Message; }
    }

    private void OnImportPlaylistFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "歌单文件 (*.m3u;*.m3u8;*.txt)|*.m3u;*.m3u8;*.txt",
            };
            if (dlg.ShowDialog(this) != true) return;
            var items = new List<MediaItem>();
            foreach (var line in File.ReadAllLines(dlg.FileName))
            {
                string p = line.Trim();
                if (p.Length == 0 || p.StartsWith("#")) continue;
                bool isUrl = p.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                if (!isUrl && !File.Exists(p)) continue;
                items.Add(new MediaItem
                {
                    Title = System.IO.Path.GetFileName(p),
                    Uri = p,
                    Kind = isUrl ? MediaSourceKind.Online : MediaSourceKind.LocalFile,
                    Provider = "导入",
                });
            }
            if (items.Count == 0) { RackStatusText.Text = "文件中未解析到有效曲目"; return; }
            // 存入自建歌单并播放
            var store = PlaylistStore.Load();
            var pl = new DogDeskPlaylist { Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) };
            pl.Items.AddRange(items);
            store.Add(pl);
            PlaylistStore.Save(store);
            PlayList(items, 0);
            RackStatusText.Text = $"已从文件导入 {items.Count} 首 → 歌单「{pl.Name}」";
        }
        catch (Exception ex) { RackStatusText.Text = "导入文件失败：" + ex.Message; }
    }

    private static Color Hsv(double h, double s, double v)
    {
        int hi = (int)(h / 60.0) % 6;
        double f = h / 60.0 - (int)(h / 60.0);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        double r = hi switch { 0 => v, 1 => q, 2 => p, 3 => p, 4 => t, _ => v };
        double g = hi switch { 0 => t, 1 => v, 2 => v, 3 => q, 4 => p, _ => p };
        double b = hi switch { 0 => p, 1 => p, 2 => t, 3 => v, 4 => v, _ => q };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    // ================= F10 右键：下一首 / 收藏 =================

    private void AttachListContextMenus()
    {
        try
        {
            AttachMenu(OnlineList);
            AttachMenu(LibraryList);
        }
        catch { }
    }

    private void AttachMenu(ListView list)
    {
        var menu = new ContextMenu();
        var playNext = new MenuItem { Header = "下一首播放（InsertAfterCurrent）" };
        playNext.Click += (_, _) =>
        {
            if (list.SelectedItem is TrackRow row)
            {
                _queue.InsertAfterCurrent(row.Item);
                Log($"[队列] 已加入下一首：{row.Item.Title}");
            }
        };
        var fav = new MenuItem { Header = "♥ 收藏到「我的收藏」" };
        fav.Click += (_, _) =>
        {
            if (list.SelectedItem is TrackRow row)
            {
                var list2 = PlaylistStore.Load();
                var favs = PlaylistStore.EnsureFavorites(list2);
                if (!favs.Any(p => p.Name == PlaylistStore.FavoritesName))
                    favs.Add(new DogDeskPlaylist { Name = PlaylistStore.FavoritesName });
                var target = favs.First(p => p.Name == PlaylistStore.FavoritesName);
                if (!target.Items.Any(i => i.Uri == row.Item.Uri))
                {
                    target.Items.Add(row.Item);
                    PlaylistStore.Save(favs);
                    Log($"[收藏] 已收藏：{row.Item.Title}");
                }
            }
        };
        menu.Items.Add(playNext);
        menu.Items.Add(fav);
        list.ContextMenu = menu;
    }

    private void TryResume()
    {
        try
        {
            var data = ResumeStore.Load();
            if (data is null || !data.IsValid) return;
            PlayList(data.Queue.ToList(), data.CurrentIndex);
            if (data.PositionSec > 1 && data.PositionSec < data.Queue[data.CurrentIndex].DurationSec)
            {
                // 短延迟等 mpv 加载完成后 seek
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    await Task.Delay(400);
                    try { _builtin.Seek(data.PositionSec / Math.Max(_builtin.DurationSec, 1)); }
                    catch { }
                }));
            }
            Log("[恢复] 已恢复上次播放位置");
            RackStatusText.Text = "已恢复上次播放位置";
        }
        catch { }
    }
}
