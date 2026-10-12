using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MediaBarWallpaper.Services;
using UserControl = System.Windows.Controls.UserControl;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace MediaBarWallpaper.Views;

/// <summary>
/// 16 段频谱律动柱。可在 WPF 壁纸（动画/图片源）内直接叠加，
/// 也可放进独立透明窗口（解决 mpv 视频源的 airspace 遮挡）。
/// 服务端已做平滑，这里直接映射 ScaleY。
/// </summary>
public partial class SpectrumBarsControl : UserControl
{
    private AudioSpectrumService? _spectrum;
    private DispatcherTimer? _timer;
    private readonly Rectangle[] _bars = new Rectangle[AudioSpectrumService.BandCount];
    private bool _built;

    public static readonly DependencyProperty BarHeightProperty =
        DependencyProperty.Register(nameof(BarHeight), typeof(double), typeof(SpectrumBarsControl),
            new PropertyMetadata(100.0));

    /// <summary>柱条最大高度（物理像素）。</summary>
    public double BarHeight
    {
        get => (double)GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    public SpectrumBarsControl()
    {
        InitializeComponent();
        IsHitTestVisible = false;
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

    private void EnsureBars()
    {
        if (_built || ActualWidth <= 1) return;

        BarsCanvas.Children.Clear();
        double w = ActualWidth;
        double h = BarHeight;
        double slot = w / AudioSpectrumService.BandCount;
        double barW = Math.Max(2, slot * 0.5);

        for (int i = 0; i < _bars.Length; i++)
        {
            var r = new Rectangle
            {
                Width = barW,
                Height = h,
                RadiusX = 3,
                RadiusY = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0xCC, 0x4F, 0xC3, 0xE8)),
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new ScaleTransform(1, 0.03)
            };
            Canvas.SetLeft(r, i * slot + (slot - barW) / 2);
            Canvas.SetBottom(r, 0);
            _bars[i] = r;
            BarsCanvas.Children.Add(r);
        }
        _built = true;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        EnsureBars();
        if (_spectrum is null || !_built) return;

        float[] bands = _spectrum.GetLatest();
        for (int i = 0; i < _bars.Length; i++)
        {
            if (_bars[i].RenderTransform is ScaleTransform st)
            {
                float v = i < bands.Length ? bands[i] : 0;
                st.ScaleY = Math.Max(0.03, Math.Min(1, v));
            }
        }
    }
}
