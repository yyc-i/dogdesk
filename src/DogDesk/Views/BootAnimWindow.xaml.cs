using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DogDesk.Views;

/// <summary>开机动画窗口：居中无边框，超时/点击/Esc 跳过，用完即释放。</summary>
public partial class BootAnimWindow : Window
{
    private readonly DispatcherTimer _close;

    public BootAnimWindow(double durationSec, string style, string path)
    {
        InitializeComponent();

        // 极简样式变体
        try
        {
            if (style == "Gradient")
            {
                AuroraBrush.GradientStops[0].Color = Color.FromRgb(0x22, 0x22, 0x33);
                AuroraBrush.GradientStops[2].Color = Color.FromRgb(0x44, 0x33, 0x55);
            }
            else if (style == "FlowText")
            {
                LogoText.Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
            }
        }
        catch { }

        // F7：Image 样式（BootAnimPath 图片）
        if (style == "Image" && !string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                BootImage.Source = bmp;
                BootImage.Visibility = Visibility.Visible;
                LogoText.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        _close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, durationSec)) };
        _close.Tick += (_, _) => { _close.Stop(); Close(); };
        _close.Start();
    }

    private void Skip(object sender, MouseButtonEventArgs e)
    {
        _close.Stop();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _close.Stop();
        try { BootImage.Source = null; } catch { }
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { _close.Stop(); Close(); }
    }
}
