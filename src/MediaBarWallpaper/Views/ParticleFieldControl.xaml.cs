using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MediaBarWallpaper.Views;

/// <summary>
/// 粒子星空动画壁纸：深色渐变背景 + 缓慢漂移的星光粒子（带闪烁），
/// 用 DispatcherTimer 以约 30fps 驱动，空闲时可降帧。
/// </summary>
public partial class ParticleFieldControl : UserControl
{
    private const int ParticleCount = 130;

    private struct Particle
    {
        public Ellipse Dot;
        public double Vx;
        public double Vy;
        public double Phase;   // 闪烁相位
        public double Speed;   // 闪烁速度
    }

    private readonly Random _rand = new();
    private readonly List<Particle> _particles = new();
    private DispatcherTimer? _timer;
    private bool _built;
    private int _fps = 30;

    public ParticleFieldControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => { _built = false; Build(); };
    }

    /// <summary>帧率（空闲降帧时由外部调低）。</summary>
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

    public void Start()
    {
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

    private void Build()
    {
        if (_built || ActualWidth <= 1 || ActualHeight <= 1) return;

        Root.Children.Clear();
        _particles.Clear();

        for (int i = 0; i < ParticleCount; i++)
        {
            double size = 1.5 + _rand.NextDouble() * 2.5;
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Colors.White),
                Opacity = 0.25 + _rand.NextDouble() * 0.6,
            };
            Canvas.SetLeft(dot, _rand.NextDouble() * ActualWidth);
            Canvas.SetTop(dot, _rand.NextDouble() * ActualHeight);
            Root.Children.Add(dot);

            _particles.Add(new Particle
            {
                Dot = dot,
                Vx = (_rand.NextDouble() - 0.5) * 8,
                Vy = -(6 + _rand.NextDouble() * 22),   // 缓慢上漂
                Phase = _rand.NextDouble() * Math.PI * 2,
                Speed = 1.5 + _rand.NextDouble() * 3.5,
            });
        }

        _built = true;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_built || _particles.Count == 0) return;

        double w = ActualWidth, h = ActualHeight;
        double dt = 1.0 / _fps;
        double t = Environment.TickCount / 1000.0;

        foreach (ref Particle p in CollectionsMarshal.AsSpan(_particles))
        {
            double x = Canvas.GetLeft(p.Dot) + p.Vx * dt;
            double y = Canvas.GetTop(p.Dot) + p.Vy * dt;

            if (y < -8) { y = h + 8; x = _rand.NextDouble() * w; }
            if (x < -8) x = w + 8;
            else if (x > w + 8) x = -8;

            Canvas.SetLeft(p.Dot, x);
            Canvas.SetTop(p.Dot, y);

            double tw = 0.25 + 0.6 * (0.5 + 0.5 * Math.Sin(t * p.Speed + p.Phase));
            p.Dot.Opacity = Math.Clamp(tw, 0.05, 0.95);
        }
    }
}
