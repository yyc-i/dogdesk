using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DogDesk.Views;

/// <summary>
/// 粒子/场景动画壁纸（v0.8.0 扩充为参数化预设）：
/// 内置 4 种预设 Starfield 星空 / Flame 火焰 / Fluid 流体 / Rain 落雨；
/// 可选鼠标互动（粒子跟随/涟漪，仅前台交互时作用，低耗）。
/// DispatcherTimer 驱动，帧率可调（空闲/全屏降帧）。
/// </summary>
public partial class ParticleFieldControl : UserControl
{
    private const int ParticleCount = 130;

    // 预设名（与设置 ParticlePreset 对齐）
    public const string PresetStarfield = "Starfield";
    public const string PresetFlame = "Flame";
    public const string PresetFluid = "Fluid";
    public const string PresetRain = "Rain";

    private struct Particle
    {
        public Shape Dot;
        public double Vx;
        public double Vy;
        public double Phase;   // 闪烁/波动相位
        public double Speed;   // 闪烁速度
        public double BaseOsc; // 流体摆动幅度
    }

    private readonly Random _rand = new();
    private readonly List<Particle> _particles = new();
    private DispatcherTimer? _timer;
    private bool _built;
    private int _fps = 30;

    private Point _mouse = new(double.NaN, double.NaN);

    /// <summary>预设：Starfield / Flame / Fluid / Rain。</summary>
    public string Preset { get; set; } = PresetStarfield;

    /// <summary>鼠标互动：粒子对鼠标产生涟漪/推力（默认关，由外部按设置开启）。</summary>
    public bool MouseInteraction { get; set; }

    /// <summary>帧率（空闲/全屏降帧时由外部调低）。</summary>
    public int Fps
    {
        get => _fps;
        set
        {
            _fps = Math.Clamp(value, 5, 60);
            if (_timer is not null)
                _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _fps);
        }
    }

    public ParticleFieldControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => { _built = false; Build(); };
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => _mouse = new Point(double.NaN, double.NaN);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!MouseInteraction) return;
        try { _mouse = e.GetPosition(this); } catch { _mouse = new Point(double.NaN, double.NaN); }
    }

    public void Start()
    {
        // 鼠标互动需要命中测试（默认壁纸层透明不接收鼠标）
        IsHitTestVisible = MouseInteraction;

        _timer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / _fps)
        };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        Build();
    }

    public void Stop()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private bool IsRain => Preset == PresetRain;

    private void Build()
    {
        if (_built || ActualWidth <= 1 || ActualHeight <= 1) return;

        Root.Children.Clear();
        _particles.Clear();

        for (int i = 0; i < ParticleCount; i++)
        {
            Shape dot = IsRain ? MakeRainDrop() : MakeDot();
            Canvas.SetLeft(dot, _rand.NextDouble() * ActualWidth);
            Canvas.SetTop(dot, _rand.NextDouble() * ActualHeight);
            Root.Children.Add(dot);

            double vx, vy, osc = 0;
            switch (Preset)
            {
                case PresetFlame:
                    vx = (_rand.NextDouble() - 0.5) * 10;
                    vy = -(40 + _rand.NextDouble() * 80);
                    break;
                case PresetFluid:
                    vx = 12 + _rand.NextDouble() * 26;
                    vy = 0;
                    osc = 20 + _rand.NextDouble() * 30;
                    break;
                case PresetRain:
                    vx = 20 + _rand.NextDouble() * 25;      // 轻微斜落
                    vy = 180 + _rand.NextDouble() * 140;
                    break;
                default: // Starfield
                    vx = (_rand.NextDouble() - 0.5) * 8;
                    vy = -(6 + _rand.NextDouble() * 22);
                    break;
            }

            _particles.Add(new Particle
            {
                Dot = dot,
                Vx = vx,
                Vy = vy,
                Phase = _rand.NextDouble() * Math.PI * 2,
                Speed = 1.5 + _rand.NextDouble() * 3.5,
                BaseOsc = osc,
            });
        }

        _built = true;
    }

    private Shape MakeDot()
    {
        double size = 1.5 + _rand.NextDouble() * 2.5;
        Color c = Preset switch
        {
            PresetFlame => ColorFromPalette(0xFF8A00, 0xFFD54A, _rand.NextDouble()),
            PresetFluid => ColorFromPalette(0x35, 0xA0, 0xE0, 0x9B, 0x6F, 0xF0, _rand.NextDouble()),
            _ => Colors.White,
        };
        return new Ellipse
        {
            Width = size, Height = size,
            Fill = new SolidColorBrush(c),
            Opacity = 0.25 + _rand.NextDouble() * 0.6,
        };
    }

    private Shape MakeRainDrop()
    {
        return new Rectangle
        {
            Width = 1.2,
            Height = 10 + _rand.NextDouble() * 10,
            Fill = new SolidColorBrush(Color.FromArgb(180, 165, 190, 220)),
            Opacity = 0.3 + _rand.NextDouble() * 0.5,
        };
    }

    private static Color ColorFromPalette(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2, double t)
    {
        return Color.FromRgb(
            (byte)(r1 + (r2 - r1) * t),
            (byte)(g1 + (g2 - g1) * t),
            (byte)(b1 + (b2 - b1) * t));
    }

    private static Color ColorFromPalette(int rgb1, int rgb2, double t)
    {
        var c1 = System.Windows.Media.Color.FromRgb((byte)((rgb1 >> 16) & 0xFF), (byte)((rgb1 >> 8) & 0xFF), (byte)(rgb1 & 0xFF));
        var c2 = System.Windows.Media.Color.FromRgb((byte)((rgb2 >> 16) & 0xFF), (byte)((rgb2 >> 8) & 0xFF), (byte)(rgb2 & 0xFF));
        return Color.FromRgb(
            (byte)(c1.R + (c2.R - c1.R) * t),
            (byte)(c1.G + (c2.G - c1.G) * t),
            (byte)(c1.B + (c2.B - c1.B) * t));
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_built || _particles.Count == 0) return;

        double w = ActualWidth, h = ActualHeight;
        double dt = 1.0 / _fps;
        double t = Environment.TickCount / 1000.0;
        bool hasMouse = MouseInteraction && !double.IsNaN(_mouse.X);

        foreach (ref Particle p in CollectionsMarshal.AsSpan(_particles))
        {
            double vx = p.Vx, vy = p.Vy;

            // 流体：水平漂移 + 正弦上下波动
            if (Preset == PresetFluid)
                vy = Math.Sin(t * 1.2 + p.Phase) * p.BaseOsc;

            double x = Canvas.GetLeft(p.Dot) + vx * dt;
            double y = Canvas.GetTop(p.Dot) + vy * dt;

            // 鼠标涟漪：近距离粒子被推开
            if (hasMouse)
            {
                double dx = x - _mouse.X, dy = y - _mouse.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 90 && dist > 0.1)
                {
                    double force = (90 - dist) / 90 * 180;
                    x += dx / dist * force * dt;
                    y += dy / dist * force * dt;
                }
            }

            // 边界回绕
            if (Preset == PresetRain)
            {
                if (y > h + 12) { y = -12; x = _rand.NextDouble() * w; }
            }
            else if (Preset == PresetFluid)
            {
                if (x > w + 8) { x = -8; y = _rand.NextDouble() * h; }
            }
            else // 星空/火焰 上漂回绕
            {
                if (y < -12) { y = h + 8; x = _rand.NextDouble() * w; }
                if (x < -8) x = w + 8;
                else if (x > w + 8) x = -8;
            }

            Canvas.SetLeft(p.Dot, x);
            Canvas.SetTop(p.Dot, y);

            // 闪烁（落雨不闪）
            if (!IsRain)
            {
                double tw = 0.25 + 0.6 * (0.5 + 0.5 * Math.Sin(t * p.Speed + p.Phase));
                p.Dot.Opacity = Math.Clamp(tw, 0.05, 0.95);
            }
        }
    }
}
