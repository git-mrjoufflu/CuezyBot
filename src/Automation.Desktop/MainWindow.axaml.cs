using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Automation.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnTestClick(object? sender, RoutedEventArgs eventArgs)
    {
        TestStatus.Text = "Simulation réussie : les 3 étapes seraient exécutées.";
    }

    internal void SetConnectionStatus(string message) => TestStatus.Text = message;

    internal void UpdateIntegrationCount(bool obsConnected, bool twitchConnected)
    {
        ObsActionSection.IsVisible = obsConnected;
        TwitchActionSection.IsVisible = twitchConnected;

        var connected = (obsConnected ? 1 : 0) + (twitchConnected ? 1 : 0);
        IntegrationObsTooltipDot.Fill = new SolidColorBrush(Color.Parse(obsConnected ? "#53D994" : "#F1B85B"));
        IntegrationObsTooltipStatus.Text = obsConnected ? "Connecté" : "Non connecté";
        IntegrationObsTooltipStatus.Foreground = new SolidColorBrush(Color.Parse(obsConnected ? "#72F0AA" : "#FFD081"));
        IntegrationTwitchTooltipDot.Fill = new SolidColorBrush(Color.Parse(twitchConnected ? "#53D994" : "#F1B85B"));
        IntegrationTwitchTooltipStatus.Text = twitchConnected ? "Connecté" : "Non connecté";
        IntegrationTwitchTooltipStatus.Foreground = new SolidColorBrush(Color.Parse(twitchConnected ? "#72F0AA" : "#FFD081"));
        IntegrationCountStatus.Text = $"{connected}/2 intégrations connectées";
        IntegrationStatusDot.Background = new SolidColorBrush(Color.Parse(connected switch
        {
            2 => "#53D994",
            1 => "#F1B85B",
            _ => "#7C849F"
        }));
        IntegrationCountStatus.Foreground = new SolidColorBrush(Color.Parse(connected switch
        {
            2 => "#72F0AA",
            1 => "#FFD081",
            _ => "#9AA1BD"
        }));
    }

    private async void OnIntegrationsClick(object? sender, RoutedEventArgs eventArgs)
    {
        var window = new IntegrationsWindow();
        await window.ShowDialog(this);
        UpdateIntegrationCount(
            ObsSessionState.Client?.IsConnected == true,
            TwitchSessionState.EventSubClient?.IsConnected == true);
    }

    private async void OnHistoryClick(object? sender, RoutedEventArgs eventArgs)
    {
        await new HistoryWindow().ShowDialog(this);
    }

    private void OnTwitchSectionClick(object? sender, RoutedEventArgs eventArgs) =>
        ToggleSection(TwitchActions, TwitchSectionChevron);

    private void OnObsSectionClick(object? sender, RoutedEventArgs eventArgs) =>
        ToggleSection(ObsActions, ObsSectionChevron);

    private void OnCoreSectionClick(object? sender, RoutedEventArgs eventArgs) =>
        ToggleSection(CoreActions, CoreSectionChevron);

    private static void ToggleSection(Control content, TextBlock chevron)
    {
        content.IsVisible = !content.IsVisible;
        chevron.Text = content.IsVisible ? "⌃" : "⌄";
    }
}
