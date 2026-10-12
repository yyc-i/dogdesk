using Avalonia;

namespace MediaBarWallpaper.Mac;

internal static class Program
{
    /// <summary>
    ///  Avalonia 桌面入口。macOS 上由 .app/Contents/MacOS/MediaBarWallpaper.Mac 启动。
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
