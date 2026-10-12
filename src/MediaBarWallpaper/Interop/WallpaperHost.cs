namespace MediaBarWallpaper.Interop;

/// <summary>
/// 壁纸桌面宿主发现：定位承载桌面图标下层的 WorkerW 窗口（Win8+ 经典方案，Lively 等同思路）。
/// 流程：找到 Progman → 发送 0x052C 确保 WorkerW 存在 → 枚举顶层窗口，找到含
/// SHELLDLL_DefView 的窗口，其父窗口即 WorkerW。将壁纸窗口 SetParent 到 WorkerW 后，
/// 壁纸即渲染在桌面图标之下、成为“真正”的桌面背景。
/// </summary>
public static class WallpaperHost
{
    private const uint WM_SPAWN_WORKERW = 0x052C;
    private const uint SMTO_NORMAL = 0x0000;
    private const string DefViewClass = "SHELLDLL_DefView";

    /// <summary>找到（必要时创建）WorkerW 窗口句柄；失败返回 IntPtr.Zero。</summary>
    public static IntPtr FindOrCreateWorkerW()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            return IntPtr.Zero;

        SpawnWorkerW(progman);

        IntPtr workerW = FindWorkerW(progman);
        if (workerW == IntPtr.Zero)
        {
            // 兜底：再触发一次后重试
            SpawnWorkerW(progman);
            workerW = FindWorkerW(progman);
        }

        return workerW;
    }

    private static void SpawnWorkerW(IntPtr progman)
    {
        NativeMethods.SendMessageTimeout(
            progman, WM_SPAWN_WORKERW, new IntPtr(0xD), new IntPtr(0xD),
            SMTO_NORMAL, 1000, out _);
    }

    private static IntPtr FindWorkerW(IntPtr progman)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((top, _) =>
        {
            if (!NativeMethods.IsWindowVisible(top))
                return true;

            IntPtr defView = NativeMethods.FindWindowEx(top, IntPtr.Zero, DefViewClass, null);
            if (defView == IntPtr.Zero)
                return true;

            IntPtr parent = NativeMethods.GetParent(defView);
            // Win8+ 上 DefView 的父窗口即 WorkerW；若仍是 Progman 说明尚未成功分离
            if (parent != IntPtr.Zero && parent != progman)
            {
                found = parent;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
