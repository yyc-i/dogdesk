using System.IO;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 开机自启（非侵入式）：在用户「启动」文件夹创建快捷方式，
/// 不改注册表、不需要管理员权限，用户可在任务管理器/启动应用里自由关闭。
/// </summary>
public static class AutoStartService
{
    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "MediaBarWallpaper.lnk");

    public static bool IsEnabled() => File.Exists(ShortcutPath);

    public static void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
                CreateShortcut();
            else if (File.Exists(ShortcutPath))
                File.Delete(ShortcutPath);
        }
        catch
        {
            // 自启设置失败不阻塞主流程
        }
    }

    private static void CreateShortcut()
    {
        string exe = Environment.ProcessPath ?? "";
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return;

        // WScript.Shell COM 创建 .lnk（Windows 运行时才解析，交叉编译安全）
        dynamic shell = Activator.CreateInstance(
            Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic lnk = shell.CreateShortcut(ShortcutPath);
        lnk.TargetPath = exe;
        lnk.WorkingDirectory = Path.GetDirectoryName(exe);
        lnk.Description = "MediaBarWallpaper — 任务栏媒体条 + 动态壁纸";
        lnk.Save();
    }
}
