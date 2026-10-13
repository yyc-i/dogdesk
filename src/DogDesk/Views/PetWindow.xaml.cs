using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DogDesk.Interop;

namespace DogDesk.Views;

/// <summary>
/// 桌宠窗口：透明、无边框、不抢焦点（WS_EX_TOOLWINDOW|NOACTIVATE）、置顶可拖。
/// 自绘卡通猫；用 GetAsyncKeyState 轮询本机键盘状态驱动左右拍爪（不收集不上传）。
/// </summary>
public partial class PetWindow : Window
{
    [DllImport("user32.dll")]
    private static extern ushort GetAsyncKeyState(int vKey);

    private DispatcherTimer? _poll;
    private bool _dragging;
    private Point _dragOffset;
    private bool _moved;
    public Action<PetWindow>? PositionChanged;
    public Action<PetWindow>? Clicked;

    public PetWindow()
    {
        InitializeComponent();
    }

    public void StartPolling()
    {
        _poll ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _poll.Tick -= OnPoll;
        _poll.Tick += OnPoll;
        _poll.Start();
    }

    public void ApplyNoActivate()
    {
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
    }

    private void OnPoll(object? sender, EventArgs e)
    {
        // 简单拍爪映射：A/S 行 → 左爪；L/; 或空格 → 右爪。仅本机读取，不上传。
        bool left = (GetAsyncKeyState(0x41) & 0x8000) != 0;   // A
        bool right = (GetAsyncKeyState(0x20) & 0x8000) != 0;    // Space
        SetPaw(PawL, left);
        SetPaw(PawR, right);
    }

    private static void SetPaw(System.Windows.Shapes.Ellipse paw, bool hit)
    {
        Canvas.SetTop(paw, hit ? 88.0 : 98.0);
    }

    private void OnDragDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _moved = false;
        _dragOffset = e.GetPosition(this);
        Root.CaptureMouse();
    }

    private void OnDragUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Root.ReleaseMouseCapture();
        PositionChanged?.Invoke(this);
        if (!_moved) Clicked?.Invoke(this);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragOffset.X) > 4 || Math.Abs(p.Y - _dragOffset.Y) > 4) _moved = true;
        Left = p.X - _dragOffset.X + Left;
        Top = p.Y - _dragOffset.Y + Top;
    }
}
