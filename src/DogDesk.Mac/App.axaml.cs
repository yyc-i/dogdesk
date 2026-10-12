using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DogDesk.Mac.Services;

namespace DogDesk.Mac;

public partial class App : Application
{
    public AppServices Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = new AppServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Views.MainWindow(Services);
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                // v0.6 记忆播放：退出前保存队列与位置
                Services.SaveOnExit();
                Services.Dispose();
            };
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
