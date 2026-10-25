using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Automation.Desktop;

public sealed partial class App : global::Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            _ = RestoreConnectionsAsync(mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RestoreConnectionsAsync(MainWindow mainWindow)
    {
        try
        {
            var result = await ConnectionPersistence.RestoreAsync();
            mainWindow.UpdateIntegrationCount(result.ObsConnected, result.TwitchConnected);

            if (result.Errors.Count > 0)
            {
                mainWindow.SetConnectionStatus($"Reconnexion incomplète : {string.Join(" · ", result.Errors)}");
            }
        }
        catch (Exception exception)
        {
            mainWindow.UpdateIntegrationCount(false, false);
            mainWindow.SetConnectionStatus($"Coffre de secrets indisponible : {exception.Message}");
        }
    }
}
