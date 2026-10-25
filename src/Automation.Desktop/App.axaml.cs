using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Automation.Persistence.Sqlite.Storage;

namespace Automation.Desktop;

public sealed partial class App : global::Avalonia.Application
{
    private RuntimeDataLease? runtimeLease;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                runtimeLease = RuntimeDataLease.Acquire(new AppDataPaths());
                desktop.Exit += (_, _) => runtimeLease.Dispose();
            }
            catch (Exception exception)
            {
                var errorWindow = new Window { Title = "CuezyBot", Width = 580, Height = 220 };
                var close = new Button { Content = "Fermer / Close" };
                close.Click += (_, _) => errorWindow.Close();
                errorWindow.Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16, Children =
                {
                    new TextBlock { Text = exception.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close
                } };
                desktop.MainWindow = errorWindow;
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            _ = RestoreLanguageAsync(mainWindow);
            _ = RestoreConnectionsAsync(mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RestoreLanguageAsync(MainWindow mainWindow)
    {
        try
        {
            var language = await ConnectionPersistence.GetLanguageAsync();
            if (language is not null)
            {
                mainWindow.ApplyLanguage(language == "English");
            }
        }
        catch
        {
            // The default French UI remains available if the local settings store is unavailable.
        }
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
