using System.Windows;

namespace MediaBarWallpaper.Interop;

/// <summary>
/// 任务栏定位服务：获取任务栏矩形，计算任务栏媒体条的吸附位置。
/// 媒体条是无边框悬浮窗，通过 SetWindowPos 吸附到任务栏区域（非侵入式，不修改 Explorer）。
/// </summary>
public static class TaskbarService
{
    public enum DockPosition { Left, Center, Right }

    private const string TrayClass = "Shell_TrayWnd";

    /// <summary>获取主任务栏窗口矩形（物理像素）。任务栏隐藏时可能返回 null。</summary>
    public static Rect? GetTaskbarRect()
    {
        IntPtr hwnd = NativeMethods.FindWindow(TrayClass, null);
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r))
            return null;
        return new Rect(r.Left, r.Top, r.Width, r.Height);
    }

    /// <summary>
    /// 计算媒体条吸附位置与尺寸（物理像素）。
    /// 水平任务栏：媒体条垂直居中于任务栏内部，X 按靠左/居中/靠右排布；
    /// 垂直任务栏：媒体条水平居中于任务栏内部，Y 按靠左(上端)/居中/靠右(下端)排布。
    /// </summary>
    public static (int X, int Y, int W, int H) ComputeAnchor(
        Rect taskbar, int barWidthPx, int barHeightPx, DockPosition dock)
    {
        bool horizontal = taskbar.Width >= taskbar.Height;

        if (horizontal)
        {
            int h = Math.Min(barHeightPx, Math.Max(24, (int)taskbar.Height - 6));
            int w = Math.Min(barWidthPx, Math.Max(160, (int)taskbar.Width - 24));
            int y = (int)taskbar.Top + ((int)taskbar.Height - h) / 2;
            int x = dock switch
            {
                DockPosition.Left   => (int)taskbar.Left + 10,
                DockPosition.Right  => (int)taskbar.Right - w - 10,
                _                   => (int)taskbar.Left + ((int)taskbar.Width - w) / 2,
            };
            return (x, y, w, h);
        }
        else
        {
            int w = Math.Min(barWidthPx, Math.Max(160, (int)taskbar.Width - 6));
            int h = Math.Min(barHeightPx, Math.Max(48, (int)taskbar.Height - 24));
            int x = (int)taskbar.Left + ((int)taskbar.Width - w) / 2;
            int y = dock switch
            {
                DockPosition.Left   => (int)taskbar.Top + 10,
                DockPosition.Right  => (int)taskbar.Bottom - h - 10,
                _                   => (int)taskbar.Top + ((int)taskbar.Height - h) / 2,
            };
            return (x, y, w, h);
        }
    }
}
