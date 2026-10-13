using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DogDesk.Models;
using DogDesk.Services;
using UserControl = System.Windows.Controls.UserControl;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace DogDesk.Views;

/// <summary>
/// 16 段频谱可视化控件，支持三种样式：柱状 Bars / 圆环 Ring / 波形 Wave。
/// 服务端已做平滑，这里直接映射。可在 WPF 壁纸内叠加，或放入独立透明窗口
/// （解决 mpv 视频源的 airspace 遮挡）。
/// </summary>
public partial class SpectrumBarsControl : UserControl
{
    private AudioSpectrumService? _spectrum;
    private DispatcherTimer? _timer;
    private bool _built;
    private SpectrumStyle _style = SpectrumStyle.Bars;

    // 三种样式的元素
    private readonly Rectangle[] _bars = new Rectangle[AudioSpectrumService.BandCount];
    private readonly Line[] _rings = new Line[AudioSpectrumService.BandCount];
    private Polyline? _wave;
    private const int PixelLevels = 8;
    private readonly Rectangle[,] _pixel = new Rectangle[AudioSpectrumService.BandCount, PixelLevels];

    public static readonly DependencyProperty BarHeightProperty =
        DependencyProperty.Register(nameof(BarHeight), typeof(double), typeof(SpectrumBarsControl),
            new PropertyMetadata(100.0));

    /// <summary>柱条/波形最大高度（物理像素）。</summary>
    public double BarHeight
    {
        get => (double)GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    /// <summary>频谱样式（柱状/圆环/波形）。</summary>
    public SpectrumStyle SpectrumStyle
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            _built = false;
            EnsureVisual();
        }
    }

    public SpectrumBarsControl()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        SizeChanged += (_, _) => { _built = false; EnsureVisual(); };
    }

    public void Start(AudioSpectrumService spectrum)
    {
        _spectrum = spectrum;
        _timer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void EnsureVisual()
    {
        if (_built || ActualWidth <= 1 || ActualHeight <= 1) return;

        BarsCanvas.Children.Clear();
        double w = ActualWidth;
        double h = ActualHeight;
        double cx = w / 2, cy = h / 2;
        int n = AudioSpectrumService.BandCount;

        switch (_style)
        {
            case SpectrumStyle.Ring:
            {
                double maxLen = Math.Min(w, h) / 2 - 6;
                double baseLen = Math.Max(4, maxLen * 0.25);
                for (int i = 0; i < n; i++)
                {
                    double angle = -Math.PI / 2 + i * (2 * Math.PI / n);
                    double dx = Math.Cos(angle), dy = Math.Sin(angle);
                    var line = new Line
                    {
                        Stroke = new SolidColorBrush(Color.FromArgb(0xE6, 0x81, 0x8C, 0xF8)),
                        StrokeThickness = 3,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        X1 = cx + dx * baseLen,
                        Y1 = cy + dy * baseLen,
                        X2 = cx + dx * baseLen,
                        Y2 = cy + dy * baseLen,
                    };
                    _rings[i] = line;
                    BarsCanvas.Children.Add(line);
                }
                break;
            }
            case SpectrumStyle.Wave:
            {
                var poly = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(0xE6, 0x81, 0x8C, 0xF8)),
                    StrokeThickness = 3,
                    StrokeLineJoin = PenLineJoin.Round,
                    Fill = null,
                };
                var points = new PointCollection();
                for (int i = 0; i < n; i++)
                    points.Add(new Point(i * (w / (n - 1)), h * 0.6));
                poly.Points = points;
                _wave = poly;
                BarsCanvas.Children.Add(poly);
                break;
            }
            case SpectrumStyle.Pixel:
            {
                double slot = w / n;
                double block = Math.Max(4, Math.Min(slot * 0.7, h / PixelLevels * 0.8));
                for (int i = 0; i < n; i++)
                {
                    Color c = RgbForBand(i, n);
                    for (int lv = 0; lv < PixelLevels; lv++)
                    {
                        var r = new Rectangle
                        {
                            Width = block,
                            Height = block,
                            Fill = new SolidColorBrush(c),
                            Visibility = Visibility.Collapsed,
                        };
                        Canvas.SetLeft(r, i * slot + (slot - block) / 2);
                        Canvas.SetTop(r, h - block * (lv + 1) - lv * 1);
                        _pixel[i, lv] = r;
                        BarsCanvas.Children.Add(r);
                    }
                }
                break;
            }
            default: // Bars
            {
                double slot = w / n;
                double barW = Math.Max(2, slot * 0.5);
                for (int i = 0; i < n; i++)
                {
                    var r = new Rectangle
                    {
                        Width = barW,
                        Height = h,
                        RadiusX = 3,
                        RadiusY = 3,
                        Fill = new SolidColorBrush(Color.FromArgb(0xCC, 0x4F, 0xC3, 0xE8)),
                        RenderTransformOrigin = new Point(0.5, 1),
                        RenderTransform = new ScaleTransform(1, 0.03),
                    };
                    Canvas.SetLeft(r, i * slot + (slot - barW) / 2);
                    Canvas.SetBottom(r, 0);
                    _bars[i] = r;
                    BarsCanvas.Children.Add(r);
                }
                break;
            }
        }

        _built = true;
    }

    private static Color RgbForBand(int i, int n)
    {
        double t = n <= 1 ? 0 : (double)i / (n - 1);
        // 蓝->青->绿->黄->红
        byte r = (byte)(255 * Math.Clamp(t * 2 - 0.4, 0, 1));
        byte g = (byte)(255 * (1 - Math.Abs(t * 2 - 1)));
        byte b = (byte)(255 * Math.Clamp(1 - t * 2, 0, 1));
        return Color.FromArgb(0xFF, r, g, b);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        EnsureVisual();
        if (_spectrum is null || !_built) return;

        float[] bands = _spectrum.GetLatest();
        int n = AudioSpectrumService.BandCount;
        double w = ActualWidth, h = ActualHeight;
        double cx = w / 2, cy = h / 2;

        switch (_style)
        {
            case SpectrumStyle.Ring:
            {
                double maxLen = Math.Min(w, h) / 2 - 6;
                double baseLen = Math.Max(4, maxLen * 0.25);
                for (int i = 0; i < n; i++)
                {
                    float v = i < bands.Length ? bands[i] : 0;
                    double angle = -Math.PI / 2 + i * (2 * Math.PI / n);
                    double len = baseLen + v * maxLen;
                    double dx = Math.Cos(angle), dy = Math.Sin(angle);
                    _rings[i].X2 = cx + dx * len;
                    _rings[i].Y2 = cy + dy * len;
                }
                break;
            }
            case SpectrumStyle.Wave:
            {
                if (_wave is null) break;
                for (int i = 0; i < n; i++)
                {
                    float v = i < bands.Length ? bands[i] : 0;
                    double y = h - Math.Max(3, v * h * 0.92);
                    _wave.Points[i] = new Point(i * (w / (n - 1)), y);
                }
                break;
            }
            case SpectrumStyle.Pixel:
            {
                for (int i = 0; i < n; i++)
                {
                    float v = i < bands.Length ? bands[i] : 0;
                    int level = Math.Clamp((int)(v * PixelLevels), 0, PixelLevels - 1);
                    for (int lv = 0; lv < PixelLevels; lv++)
                        _pixel[i, lv].Visibility = lv <= level ? Visibility.Visible : Visibility.Collapsed;
                }
                break;
            }
            default:
            {
                for (int i = 0; i < _bars.Length; i++)
                {
                    if (_bars[i].RenderTransform is ScaleTransform st)
                    {
                        float v = i < bands.Length ? bands[i] : 0;
                        st.ScaleY = Math.Max(0.03, Math.Min(1, v));
                    }
                }
                break;
            }
        }
    }
}
