using System.Windows;
using MediaBarWallpaper.Models;
using MediaBarWallpaper.Services.Mpv;

namespace MediaBarWallpaper.Views;

/// <summary>
/// 基于 libmpv 的视频壁纸窗口：WindowsFormsHost 承载 WinForms Panel，
/// 把 Panel 句柄作为 wid 交给 mpv，mpv 自建 GPU 上下文输出全格式视频（硬解）。
/// 由 WallpaperEngine SetParent 到 WorkerW。
/// </summary>
public partial class MpvWallpaperWindow : Window
{
    private MpvVideoPlayer? _player;

    public bool IsPlaying => _player?.IsLoaded == true;

    public MpvWallpaperWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    /// <summary>窗口 Show 且 SetParent 之后调用：创建 mpv 并加载视频。</summary>
    public void StartPlayback(string path, Action<string>? log = null,
        StretchMode stretch = StretchMode.Cover)
    {
        if (_player is not null) return;

        // 读取 Handle 会强制创建宿主窗口；此时 WindowsFormsHost 已加载
        IntPtr wid = VideoPanel.Handle;

        _player = new MpvVideoPlayer(wid, stretch);
        _player.Log += msg => log?.Invoke(msg);
        _player.Initialize();
        _player.LoadFile(path);
    }

    public void SetPaused(bool paused) => _player?.SetPaused(paused);

    private void OnClosed(object? sender, EventArgs e)
    {
        _player?.Dispose();
        _player = null;
    }
}
