using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MediaBarWallpaper.Models;
using MediaBarWallpaper.Services;
using UserControl = System.Windows.Controls.UserControl;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace MediaBarWallpaper.Views;

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
