using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DogDesk.Services.Pets;
using DogDesk.Views;

namespace DogDesk;

/// <summary>
/// v0.8.0 表现层升级：桌宠按键音效/改名、壁纸预设/视频轮播/HTML 壁纸等 UI 事件接线。
/// 纯界面转发，不实现桌宠/壁纸内核（内核在 PetWindow / WallpaperEngine）。
/// </summary>
public partial class MainWindow
{
    /// <summary>读取 ComboBox 选中项 Tag（字符串）。</summary>
    private static string? SelectedTag(ComboBox c) =>
        c.SelectedItem is ComboBoxItem it && it.Tag is string s ? s : null;

    // ================= 桌宠 v0.8.0 =================

    private void OnPetKeySoundChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.PetKeySound = PetKeySoundCheck.IsChecked == true;
            _settingsService.Save();
            foreach (var w in _pets.Values)
                w.KeySoundEnabled = _settings.PetKeySound;
        }
        catch { }
    }

    private void OnPetRenameClick(object sender, RoutedEventArgs e)
    {
        try
        {
            int idx = PetList.SelectedIndex;
            if (idx < 0 || idx >= _petInfos.Count) { PetStatusText.Text = "请先在列表选中一只桌宠"; return; }
            string name = PetNameBox.Text?.Trim() ?? "";
            if (name.Length == 0) { PetStatusText.Text = "名字不能为空"; return; }
            var info = _petInfos[idx];
            string old = info.Name;
            if (old != name)
            {
                info.Name = name;
                PetStore.Delete(old);
                PetStore.Save(info);
                if (_pets.TryGetValue(old, out var w))
                {
                    _pets.Remove(old);
                    _pets[name] = w;
                }
            }
            RefreshPetList();
            PetStatusText.Text = $"已改名为「{name}」";
        }
        catch (Exception ex) { PetStatusText.Text = "改名失败：" + ex.Message; }
    }

    // ================= 壁纸 v0.8.0 =================

    private void OnParticlePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        SaveSettingsFromUi();
    }

    /// <summary>多选视频加入轮播列表（去重）。</summary>
    private void OnAddVideosClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "视频|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.flv",
                Multiselect = true,
                Title = "选择要加入轮播的视频（可多选）",
            };
            if (dlg.ShowDialog() != true) return;
            _settings.VideoPlaylist ??= new System.Collections.Generic.List<string>();
            foreach (var f in dlg.FileNames)
                if (!_settings.VideoPlaylist.Contains(f, StringComparer.OrdinalIgnoreCase))
                    _settings.VideoPlaylist.Add(f);
            _settingsService.Save();
            int n = _settings.VideoPlaylist.Count;
            VideoPlaylistText.Text = n > 0 ? $"已载入 {n} 个视频入轮播（点「应用壁纸」生效）" : "未设置";
            Log($"[壁纸] 视频轮播列表：{n} 个");
        }
        catch (Exception ex) { Log("[壁纸] 添加视频失败：" + ex.Message); }
    }

    private void OnVideoPrevClick(object sender, RoutedEventArgs e)
    {
        if (!_wallpaper.IsRunning) { WallpaperStatusText.Text = "壁纸未运行（先点应用壁纸）"; return; }
        _wallpaper.PrevVideo();
    }

    private void OnVideoNextClick(object sender, RoutedEventArgs e)
    {
        if (!_wallpaper.IsRunning) { WallpaperStatusText.Text = "壁纸未运行（先点应用壁纸）"; return; }
        _wallpaper.NextVideo();
    }

    private void OnVideoPauseClick(object sender, RoutedEventArgs e)
    {
        if (!_wallpaper.IsRunning) { WallpaperStatusText.Text = "壁纸未运行（先点应用壁纸）"; return; }
        _wallpaper.ToggleVideoPause();
    }

    private void OnHtmlBrowseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "网页|*.html;*.htm",
                Title = "选择本地 html 文件作为壁纸",
            };
            if (dlg.ShowDialog() != true) return;
            HtmlPathBox.Text = dlg.FileName;
            SaveSettingsFromUi();
        }
        catch (Exception ex) { Log("[壁纸] 选择 html 失败：" + ex.Message); }
    }
}
