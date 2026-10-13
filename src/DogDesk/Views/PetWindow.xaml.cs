// SPDX-License-Identifier: MIT
// 桌宠拍键盘/拍爪交互思路参考自 BongoCat（原版 Apache-2.0）：
//   https://github.com/ayangweb/BongoCat —— 原版以 Apache-2.0 发布，此处仅参考其
//   "猫趴在键盘上、按键时对应键帽高亮并同步拍爪" 的交互思路；
//   键盘布局、卡通形象、多帧表情、渲染与动画逻辑全部为本项目自研，未复制其素材/代码。
//   重构版 vladelaina/BongoCat（AGPL-3.0）具有传染性，dogdesk 为 MIT，未引用其任何代码或素材。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DogDesk.Interop;

namespace DogDesk.Views;

/// <summary>
/// 桌宠窗口（v0.8.0 BongoCat 级效果，全自研）：
/// 猫趴在自绘简化 Qwerty 键盘上；GetAsyncKeyState 轮询本机键盘（仅本机、不上传），
/// 按下任意键 → 对应键帽高亮脉冲 + 最近一侧猫爪同步拍向该键 + 可选按键音效。
/// 渲染按状态调速：互动/按键时 ~60fps 平滑插值，空闲 ~8fps 低功耗；
/// 多帧表情：随机眨眼、耳朵微动、尾巴正弦摆动、摸头开心反应。
/// 渲染异常时回退到极简自绘猫，保证不崩。
/// </summary>
public partial class PetWindow : Window
{
    [DllImport("user32.dll")]
    private static extern ushort GetAsyncKeyState(int vKey);

    // 逻辑画布尺寸（与 XAML Canvas 一致）
    private const double CW = 360, CH = 300;

    private DispatcherTimer? _anim;
    private bool _dragging, _fallback;
    private Point _dragOffset;

    // ---- 可配置（由主窗口按 PetInfo 注入）----
    /// <summary>毛色（十六进制）。</summary>
    public string FurColor { get; set; } = "#FFF3E0";
    /// <summary>按键音效开关。</summary>
    public bool KeySoundEnabled { get; set; } = true;

    // ---- 对外事件 ----
    public Action<PetWindow>? PositionChanged;
    public Action<PetWindow>? Clicked;                 // 左键摸头
    public Action<PetWindow>? DialogRequested;         // 右键「对话」
    public Action<PetWindow>? CustomizeRequested;     // 右键「自定义颜色/大小」
    public Action<PetWindow>? CloseRequested;          // 右键「关闭」

    // ---- 场景元素 ----
    private Ellipse? _pawL, _pawR;
    private Line? _eyeL, _eyeR;
    private Polygon? _earL, _earR;
    private Ellipse? _tail;
    private TranslateTransform? _tailTf;
    private Panel? _keyboardLayer;
    private readonly List<KeyDef> _keys = new();
    private readonly HashSet<int> _down = new();

    private double _pawLTargetY, _pawRTargetY;
    private double _pawLTargetX, _pawRTargetX;
    private DateTime _interactiveUntil = DateTime.MinValue;
    private DateTime _happyUntil = DateTime.MinValue;
    private DateTime _nextBlink = DateTime.MinValue;
    private bool _eyesClosed;
    private DateTime _blinkUntil = DateTime.MinValue;
    private double _earWagPhase;

    private sealed class KeyDef
    {
        public int Vk;
        public FrameworkElement Keycap = null!;
        public double Cx, Cy;     // 键位中心（逻辑坐标）
        public SolidColorBrush BaseBrush = new(Color.FromRgb(0x2A, 0x2E, 0x3F));
    }

    public PetWindow()
    {
        InitializeComponent();
        try { BuildScene(); }
        catch { try { BuildFallbackCat(); } catch { /* 仍失败则留空，窗口可用 */ } }
    }

    // ================= 场景构建 =================

    private Color ResolveFur()
    {
        try { return (Color)ColorConverter.ConvertFromString(FurColor ?? "#FFF3E0"); }
        catch { return (Color)ColorConverter.ConvertFromString("#FFF3E0"); }
    }

    private void BuildScene()
    {
        Color fur = ResolveFur();
        Color outline = Color.FromRgb(0x8D, 0x6E, 0x63);
        Color pawColor = Color.FromRgb(0xFF, 0xCC, 0x80);
        var outlinePen = new SolidColorBrush(outline);

        // ---- 尾巴（先画，垫在身体后面）----
        _tail = new Ellipse
        {
            Width = 34, Height = 14,
            Fill = new SolidColorBrush(fur),
            Stroke = outlinePen, StrokeThickness = 1.5,
        };
        _tailTf = new TranslateTransform();
        _tail.RenderTransform = _tailTf;
        Canvas.SetLeft(_tail, 250); Canvas.SetTop(_tail, 150);
        Root.Children.Add(_tail);

        // ---- 身体（大圆）----
        var body = new Ellipse
        {
            Width = 150, Height = 130,
            Fill = new SolidColorBrush(fur),
            Stroke = outlinePen, StrokeThickness = 2,
        };
        Canvas.SetLeft(body, 105); Canvas.SetTop(body, 45);
        Root.Children.Add(body);

        // ---- 耳朵 ----
        _earL = new Polygon
        {
            Points = new PointCollection { new(128, 60), new(140, 22), new(168, 52) },
            Fill = new SolidColorBrush(fur), Stroke = outlinePen, StrokeThickness = 2,
        };
        _earR = new Polygon
        {
            Points = new PointCollection { new(232, 60), new(220, 22), new(192, 52) },
            Fill = new SolidColorBrush(fur), Stroke = outlinePen, StrokeThickness = 2,
        };
        Root.Children.Add(_earL);
        Root.Children.Add(_earR);

        // ---- 眼睛（眨眼用 Line 表示开心弯眼，平时用小圆点切换）----
        _eyeL = MakeEye(150, 100);
        _eyeR = MakeEye(196, 100);
        Root.Children.Add(_eyeL);
        Root.Children.Add(_eyeR);

        // 鼻子
        var nose = new Ellipse
        {
            Width = 8, Height = 6,
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
        };
        Canvas.SetLeft(nose, 176); Canvas.SetTop(nose, 118);
        Root.Children.Add(nose);

        // ---- 键盘（静态层，仅按下时脉冲，不逐帧重画）----
        _keyboardLayer = new Canvas { CacheMode = new BitmapCache() };
        BuildKeyboard();
        Root.Children.Add(_keyboardLayer);

        // ---- 爪子（拍键动画层，最后画在最上层）----
        _pawL = new Ellipse { Width = 30, Height = 20, Fill = new SolidColorBrush(pawColor), Stroke = outlinePen, StrokeThickness = 1.5 };
        _pawR = new Ellipse { Width = 30, Height = 20, Fill = new SolidColorBrush(pawColor), Stroke = outlinePen, StrokeThickness = 1.5 };
        _pawLTargetX = 130; _pawRTargetX = 200;
        _pawLTargetY = 168; _pawRTargetY = 168;
        Canvas.SetLeft(_pawL, _pawLTargetX); Canvas.SetTop(_pawL, _pawLTargetY);
        Canvas.SetLeft(_pawR, _pawRTargetX); Canvas.SetTop(_pawR, _pawRTargetY);
        Root.Children.Add(_pawL);
        Root.Children.Add(_pawR);

        // 初始眨眼时间
        _nextBlink = DateTime.Now.AddMilliseconds(1500 + new Random().NextDouble() * 2500);
    }

    private Line MakeEye(double x, double y)
    {
        var eye = new Line
        {
            X1 = x - 5, X2 = x + 5, Y1 = y, Y2 = y,
            Stroke = new SolidColorBrush(Color.FromRgb(0x3E, 0x27, 0x23)),
            StrokeThickness = 2.5,
        };
        return eye;
    }

    private void BuildKeyboard()
    {
        const double kw = 26, kh = 24, gap = 4;
        // 每行：字符序列 + 对应 VK
        AddRow(new[] { ('Q',0x51),('W',0x57),('E',0x45),('R',0x52),('T',0x54),('Y',0x59),('U',0x55),('I',0x49),('O',0x4F),('P',0x50) },
               32, 180, kw, kh, gap);
        AddRow(new[] { ('A',0x41),('S',0x53),('D',0x44),('F',0x46),('G',0x47),('H',0x48),('J',0x4A),('K',0x4B),('L',0x4C) },
               47, 210, kw, kh, gap);
        AddRow(new[] { ('Z',0x5A),('X',0x58),('C',0x43),('V',0x56),('B',0x42),('N',0x4E),('M',0x4D) },
               77, 240, kw, kh, gap);

        // 空格
        var space = MakeKeycap("___", 0x20, 120, 272, 120, 22);
        _keys.Add(space);
    }

    private void AddRow((char c, int vk)[] row, double x0, double y, double kw, double kh, double gap)
    {
        double x = x0;
        foreach (var (c, vk) in row)
        {
            var k = MakeKeycap(c.ToString(), vk, x, y, kw, kh);
            _keys.Add(k);
            x += kw + gap;
        }
    }

    private KeyDef MakeKeycap(string label, int vk, double x, double y, double w, double h)
    {
        var baseBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x3F));
        baseBrush.Freeze();
        var border = new Border
        {
            Width = w, Height = h,
            Background = baseBrush,
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = label, FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xCD, 0xE0)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(border, x); Canvas.SetTop(border, y);
        _keyboardLayer!.Children.Add(border);
        return new KeyDef
        {
            Vk = vk, Keycap = border,
            Cx = x + w / 2, Cy = y,
            BaseBrush = baseBrush,
        };
    }

    /// <summary>键帽按下时高亮脉冲，~140ms 后恢复默认键帽色。</summary>
    private static void PulseKeycap(KeyDef k)
    {
        if (k.Keycap is not Border border) return;
        border.Background = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        t.Tick += (s, _) => { t.Stop(); border.Background = k.BaseBrush; };
        t.Start();
    }

    /// <summary>渲染异常时的极简回退猫（保证窗口可用、可拖）。</summary>
    private void BuildFallbackCat()
    {
        _fallback = true;
        Root.Children.Clear();
        Color fur = ResolveFur();
        var body = new Ellipse { Width = 100, Height = 90, Fill = new SolidColorBrush(fur), Stroke = new SolidColorBrush(Color.FromRgb(0x8D,0x6E,0x63)), StrokeThickness = 2 };
        Canvas.SetLeft(body, 130); Canvas.SetTop(body, 60);
        Root.Children.Add(body);
    }

    // ================= 动画 / 轮询 =================

    public void StartPolling()
    {
        if (_anim is null)
        {
            _anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
            _anim.Tick += OnTick;
        }
        _anim.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_fallback) return;
        try
        {
            // 1) 按键检测（边沿触发）
            bool anyKey = false;
            foreach (var k in _keys)
            {
                bool pressed = (GetAsyncKeyState(k.Vk) & 0x8000) != 0;
                if (pressed && _down.Add(k.Vk))
                {
                    anyKey = true;
                    OnKeyPressed(k);
                }
                else if (!pressed) _down.Remove(k.Vk);
            }

            DateTime now = DateTime.Now;

            // 2) 摸头开心衰减
            bool happy = now < _happyUntil;
            ApplyEyes(happy, now);

            // 3) 眨眼调度（空闲也跑）
            if (!happy && now >= _nextBlink)
            {
                _eyesClosed = !_eyesClosed;
                _blinkUntil = now.AddMilliseconds(120);
                _nextBlink = now.AddMilliseconds(2000 + new Random().NextDouble() * 3000);
            }
            if (_eyesClosed && now > _blinkUntil) _eyesClosed = false;

            // 4) 尾巴正弦摆动 + 耳朵微动
            _earWagPhase += 0.12;
            if (_tailTf is not null)
                _tailTf.Y = Math.Sin(_earWagPhase) * 6;
            if (_earL is not null)
            {
                double dx = Math.Sin(_earWagPhase * 0.5) * 2;
                Canvas.SetLeft(_earL, dx);
                Canvas.SetLeft(_earR, -dx);
            }

            // 5) 爪子平滑插值拍键
            double t = 0.35; // 插值系数（60fps 下较跟手）
            if (_pawL is not null)
            {
                double lx = Canvas.GetLeft(_pawL), ly = Canvas.GetTop(_pawL);
                Canvas.SetLeft(_pawL, lx + (_pawLTargetX - lx) * t);
                Canvas.SetTop(_pawL, ly + (_pawLTargetY - ly) * t);
            }
            if (_pawR is not null)
            {
                double lx = Canvas.GetLeft(_pawR), ly = Canvas.GetTop(_pawR);
                Canvas.SetLeft(_pawR, lx + (_pawRTargetX - lx) * t);
                Canvas.SetTop(_pawR, ly + (_pawRTargetY - ly) * t);
            }

            // 6) 按状态调速：互动 ~60fps，空闲 ~8fps
            bool active = anyKey || now < _interactiveUntil || now < _happyUntil;
            double wantMs = active ? 16.0 : 125.0;
            if (_anim is not null && Math.Abs(_anim.Interval.TotalMilliseconds - wantMs) > 30)
                _anim.Interval = TimeSpan.FromMilliseconds(wantMs);
        }
        catch
        {
            // 渲染异常：回退极简猫，停止高频动画
            try { BuildFallbackCat(); } catch { }
            if (_anim is not null) _anim.Interval = TimeSpan.FromMilliseconds(500);
        }
    }

    private void OnKeyPressed(KeyDef k)
    {
        _interactiveUntil = DateTime.Now.AddMilliseconds(500);

        // 键帽高亮脉冲
        try { PulseKeycap(k); }
        catch { }

        // 哪只爪：以画布中线分左右
        bool leftHalf = k.Cx < CW / 2;
        double pawTargetX = k.Cx - 15;   // 爪宽 30，居中
        double pawTargetY = k.Cy - 18;   // 爪压在键上方
        if (leftHalf) { _pawLTargetX = pawTargetX; _pawLTargetY = pawTargetY; }
        else { _pawRTargetX = pawTargetX; _pawRTargetY = pawTargetY; }

        // 按键音效
        if (KeySoundEnabled)
        {
            try { System.Media.SystemSounds.Beep.Play(); } catch { }
        }
    }

    private void ApplyEyes(bool happy, DateTime now)
    {
        if (_eyeL is null || _eyeR is null) return;
        if (happy)
        {
            // 开心弯眼（^ ^）
            _eyeL.StrokeThickness = 3; _eyeR.StrokeThickness = 3;
        }
        else if (_eyesClosed)
        {
            _eyeL.StrokeThickness = 1; _eyeR.StrokeThickness = 1;
        }
        else
        {
            _eyeL.StrokeThickness = 2.5; _eyeR.StrokeThickness = 2.5;
        }
    }

    /// <summary>左键摸头：开心表情 + 音效。</summary>
    private void HeadPat()
    {
        _happyUntil = DateTime.Now.AddMilliseconds(1500);
        if (KeySoundEnabled)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }
        Clicked?.Invoke(this);
    }

    // ================= 窗口样式 / 拖动 =================

    public void ApplyNoActivate()
    {
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
    }

    private void OnDragDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragOffset = e.GetPosition(this);
        Root.CaptureMouse();
        if (e.ChangedButton == MouseButton.Left) HeadPat();
    }

    private void OnDragUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Root.ReleaseMouseCapture();
        PositionChanged?.Invoke(this);
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "摸头", () => HeadPat());
        AddMenuItem(menu, "对话", () => DialogRequested?.Invoke(this));
        AddMenuItem(menu, "自定义颜色/大小", () => CustomizeRequested?.Invoke(this));
        AddMenuItem(menu, "关闭桌宠", () => CloseRequested?.Invoke(this));
        menu.PlacementTarget = Root;
        menu.IsOpen = true;
    }

    private static void AddMenuItem(ContextMenu menu, string text, Action onClick)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var p = e.GetPosition(this);
        Left = p.X - _dragOffset.X + Left;
        Top = p.Y - _dragOffset.Y + Top;
    }
}
