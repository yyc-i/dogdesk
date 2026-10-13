using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DogDesk.Models;

namespace DogDesk.Mac.Views;

/// <summary>
/// 开机动画窗口（v0.7）：无边框居中，Esc / 点击 / 超时自动关闭。
/// 内置 4 种样式：Aurora 极光渐变流动、Particle 粒子、FlowText 流光文字、Image 自定义图片。
/// </summary>
public sealed class BootAnimWindow : Window
{
    private readonly DispatcherTimer _motion = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private readonly List<Ellipse> _particles = new();
    private readonly Random _rng = new();
    private double _t;

    public BootAnimWindow(AppSettings settings)
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Width = 520;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildContent(settings);

        _autoClose.Interval = TimeSpan.FromSeconds(Math.Clamp(settings.BootAnimDurationSec, 1.0, 15.0));
        _autoClose.Tick += (_, _) => Close();
        _motion.Tick += (_, _) => OnMotion();

        PointerPressed += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void BuildContent(AppSettings s)
    {
        string style = s.BootAnimStyle ?? "Aurora";
        var host = new Panel { Background = new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x14)) };

        switch (style)
        {
            case "Particle": BuildParticles(host); break;
            case "FlowText": BuildFlowText(host); break;
            case "Image":
                if (!string.IsNullOrWhiteSpace(s.BootAnimPath) && File.Exists(s.BootAnimPath))
                {
                    try
                    {
                        var bmp = new Bitmap(File.OpenRead(s.BootAnimPath));
                        host.Children.Add(new Image { Source = bmp, Stretch = Stretch.Uniform });
                        break;
                    }
                    catch { /* 回退到流光文字 */ }
                }
                BuildFlowText(host);
                break;
            default: BuildAurora(host); break;
        }

        Content = host;
    }

    // Aurora：两层径向渐变光斑缓慢漂移
    private void BuildAurora(Panel host)
    {
        for (int i = 0; i < 3; i++)
        {
            var e = new Ellipse
            {
                Width = 260, Height = 260, Opacity = 0.5,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb((byte)(0x6a + (i * 30) % 120), 0x8c, 0xf8), 0),
                        new GradientStop(Colors.Transparent, 1),
                    },
                },
            };
            _particles.Add(e);
            host.Children.Add(e);
        }
        host.Children.Add(new TextBlock
        {
            Text = "dogdesk", FontSize = 34, FontWeight = FontWeight.Bold,
            Foreground = Brushes.White, Opacity = 0.95,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
    }

    // Particle：向上漂浮的小圆点
    private void BuildParticles(Panel host)
    {
        for (int i = 0; i < 36; i++)
        {
            double size = 3 + _rng.NextDouble() * 6;
            var e = new Ellipse
            {
                Width = size, Height = size, Opacity = 0.35 + _rng.NextDouble() * 0.5,
                Fill = new SolidColorBrush(Color.FromRgb(0x80, 0xb0, (byte)(160 + _rng.Next(80)))),
                Tag = _rng.NextDouble() * 2 + 1,
            };
            _particles.Add(e);
            host.Children.Add(e);
        }
        host.Children.Add(new TextBlock
        {
            Text = "dogdesk", FontSize = 30, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White, Opacity = 0.9,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
    }

    // FlowText：居中流光文字
    private void BuildFlowText(Panel host)
    {
        host.Children.Add(new TextBlock
        {
            Text = "dogdesk", FontSize = 44, FontWeight = FontWeight.Bold,
            Foreground = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x81, 0x8c, 0xf8), 0),
                    new GradientStop(Color.FromRgb(0xff, 0xd2, 0x8a), 0.5),
                    new GradientStop(Color.FromRgb(0x81, 0x8c, 0xf8), 1),
                },
            },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
    }

    private void OnMotion()
    {
        _t += 0.02;
        double w = Bounds.Width > 0 ? Bounds.Width : Width;
        double h = Bounds.Height > 0 ? Bounds.Height : Height;
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            double speed = p.Tag is double d ? d : 1;
            if (p.Width > 200)
            {
                // 极光光斑漂移
                double cx = w * (0.5 + 0.35 * Math.Cos(_t + i * 2.1));
                double cy = h * (0.5 + 0.35 * Math.Sin(_t * 0.8 + i * 1.7));
                Canvas.SetLeft(p, cx - p.Width / 2);
                Canvas.SetTop(p, cy - p.Height / 2);
            }
            else
            {
                // 粒子上浮
                double y = (Canvas.GetTop(p) + h) % (h + 20) - 10;
                Canvas.SetTop(p, y - speed);
                Canvas.SetLeft(p, (i * 37.0) % w);
            }
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _motion.Start();
        _autoClose.Start();
    }
}
