using DogDesk.Interop;

namespace DogDesk.Services;

/// <summary>
/// 系统托盘（NotifyIcon）：关闭窗口时驻留后台，右键菜单快捷操作。
/// 使用完全限定名规避 WPF/WinForms 命名冲突。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private IntPtr _iconHandle;

    public event Action? RestoreRequested;
    public event Action? ApplyWallpaperRequested;
    public event Action? StopWallpaperRequested;
    public event Action? ExitRequested;
    public event Action? PetToggleRequested;
    public event Action? AiToggleRequested;

    public TrayService()
    {
        _icon = new System.Windows.Forms.NotifyIcon { Text = "dogdesk" };
        _icon.Icon = BuildIcon();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示主界面", null, (_, _) => RestoreRequested?.Invoke());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("应用壁纸", null, (_, _) => ApplyWallpaperRequested?.Invoke());
        menu.Items.Add("停止壁纸", null, (_, _) => StopWallpaperRequested?.Invoke());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("桌宠", null, (_, _) => PetToggleRequested?.Invoke());
        menu.Items.Add("AI 伙伴", null, (_, _) => AiToggleRequested?.Invoke());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => RestoreRequested?.Invoke();
    }

    public void Show() => _icon.Visible = true;
    public void Hide() => _icon.Visible = false;

    /// <summary>在托盘气泡显示提示。</summary>
    public void Balloon(string title, string text)
    {
        _icon.ShowBalloonTip(2500, title, text,
            System.Windows.Forms.ToolTipIcon.Info);
    }

    /// <summary>v0.8：下载完成等通知气泡（复用 Balloon）。</summary>
    public void ShowNotification(string title, string text) => Balloon(title, text);

    private System.Drawing.Icon BuildIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Rectangle(0, 0, 32, 32),
                System.Drawing.Color.FromArgb(124, 58, 237),
                System.Drawing.Color.FromArgb(37, 99, 235), 45f);
            g.FillEllipse(brush, 1, 1, 30, 30);

            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 3);
            g.DrawLine(pen, 9, 21, 9, 11);
            g.DrawLine(pen, 16, 21, 16, 7);
            g.DrawLine(pen, 23, 21, 23, 14);
        }

        // 句柄在进程退出时由系统回收；托盘图标单例，可接受
        _iconHandle = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(_iconHandle);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_iconHandle != IntPtr.Zero)
            NativeMethods.DestroyIcon(_iconHandle);
    }
}
