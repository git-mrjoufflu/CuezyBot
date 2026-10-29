using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Automation.Persistence.Sqlite.Storage;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Reflection;

namespace Automation.Desktop;

public sealed partial class App : global::Avalonia.Application
{
    private RuntimeDataLease? runtimeLease;
    private DispatcherTimer? reconnectTimer;
    private DispatcherTimer? automationTimer;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();
            base.OnFrameworkInitializationCompleted();
            await Task.Delay(1800);
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
                splash.Close();
                desktop.MainWindow = errorWindow;
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();
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

    }

    private sealed class SplashWindow : Window
    {
        public SplashWindow()
        {
            Width = 460; Height = 500; CanResize = false; WindowDecorations = Avalonia.Controls.WindowDecorations.None; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.Parse("#080B16"));
            var image = new Image { Width = 330, Height = 330, Stretch = Stretch.Uniform };
            image.Source = new Bitmap(AssetLoader.Open(new Uri("avares://Automation.Desktop/Assets/cuezybot-logo.png")));
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            Content = new StackPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Spacing = 12, Children =
            {
                image,
                new TextBlock { Text = "CuezyBot", FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                new TextBlock { Text = $"Version {version}", FontSize = 13, Foreground = Brush.Parse("#9DA8CB"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                new ProgressBar { Width = 180, Height = 4, IsIndeterminate = true }
            }};
        }
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
