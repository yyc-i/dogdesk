using System.IO;
using System.Runtime.InteropServices;

namespace MediaBarWallpaper.Services.Mpv;

/// <summary>
/// 启动时显式预加载 libmpv-2.dll（NuGet 包输出到 BaseDirectory/libmpv/win-x64/ 子目录，
/// 不在 DllImport 默认搜索路径里）。预加载后，MpvNative 中 DllImport("libmpv-2.dll") 即可命中。
/// </summary>
internal static class MpvNativeLoader
{
    private static bool _loaded;
    private static string? _status;

    public static string Status => _status ?? "未初始化";
    public static bool IsLoaded => _loaded;

    public static bool TryPreload(Action<string>? log = null)
    {
        if (_loaded) return true;

        string path = Path.Combine(AppContext.BaseDirectory, "libmpv", "win-x64", "libmpv-2.dll");
        if (!File.Exists(path))
        {
            _status = "libmpv 缺失（将回退 MediaElement）";
            log?.Invoke($"未找到 libmpv：{path}");
            return false;
        }

        try
        {
            // 完整路径加载；libmpv 为静态构建，无额外 native 依赖
            NativeLibrary.Load(path);
            _loaded = true;
            _status = "libmpv 已加载";
            log?.Invoke("libmpv-2.dll 预加载成功");
            return true;
        }
        catch (Exception ex)
        {
            _status = $"libmpv 加载失败：{ex.Message}";
            log?.Invoke(_status);
            return false;
        }
    }
}
