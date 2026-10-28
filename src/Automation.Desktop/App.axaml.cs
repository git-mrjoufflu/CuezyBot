using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Automation.Persistence.Sqlite.Storage;
using Avalonia.Threading;

namespace Automation.Desktop;

public sealed partial class App : global::Avalonia.Application
{
    private RuntimeDataLease? runtimeLease;
    private DispatcherTimer? reconnectTimer;
    private DispatcherTimer? automationTimer;
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
            reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            reconnectTimer.Tick += async (_, _) =>
            {
                try { await AutoReconnect.RetryAsync(); }
                catch { /* A later tick retries without opening an authentication dialog. */ }
                mainWindow.UpdateIntegrationCount(ObsSessionState.Client?.IsConnected == true, TwitchSessionState.EventSubClient?.IsConnected == true);
            };
            reconnectTimer.Start();
            automationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            automationTimer.Tick += async (_, _) =>
            {
                try { await PrototypeServices.ProcessTimersAsync(); }
                catch { /* A faulty workflow is recorded by the execution engine; the scheduler keeps running. */ }
            };
            automationTimer.Start();
            _ = PrototypeServices.ProcessTimersAsync();
            desktop.Exit += (_, _) => { reconnectTimer.Stop(); automationTimer.Stop(); };
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
            await AutoReconnect.LoadAsync();
            await AutoReconnect.RetryAsync();
            mainWindow.UpdateIntegrationCount(ObsSessionState.Client?.IsConnected == true, TwitchSessionState.EventSubClient?.IsConnected == true);
        }
        catch (Exception exception)
        {
            mainWindow.UpdateIntegrationCount(false, false);
            mainWindow.SetConnectionStatus($"Coffre de secrets indisponible : {exception.Message}");
        }
    }
}
