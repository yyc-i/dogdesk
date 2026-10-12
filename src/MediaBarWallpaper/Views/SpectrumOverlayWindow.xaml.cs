using System.Windows;
using MediaBarWallpaper.Models;
using MediaBarWallpaper.Services;

namespace MediaBarWallpaper.Views;

/// <summary>
/// 独立透明频谱窗口。mpv 视频壁纸使用独立 hwnd（WindowsFormsHost），
/// 同窗口内的 WPF 元素会被视频遮挡（airspace），因此把频谱放到本透明分层窗口，
/// 由 WallpaperEngine SetParent 到 WorkerW 并置于视频窗口之上。
/// </summary>
public partial class SpectrumOverlayWindow : Window
{
    public SpectrumOverlayWindow()
    {
        InitializeComponent();
    }

    public void Start(AudioSpectrumService spectrum, double barHeight = 110,
        SpectrumStyle style = SpectrumStyle.Bars)
    {
        Bars.BarHeight = barHeight;
        Bars.SpectrumStyle = style;
        Bars.Start(spectrum);
    }

    protected override void OnClosed(EventArgs e)
    {
        Bars.Stop();
        base.OnClosed(e);
    }
}
