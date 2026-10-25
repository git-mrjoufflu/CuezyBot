using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using System.Net.Http;
using Avalonia.Threading;
using System.IO;

namespace Automation.Desktop;

public sealed partial class MainWindow : Window
{
    private bool _english;

    public MainWindow() => InitializeComponent();

    private void OnTestClick(object? sender, RoutedEventArgs eventArgs)
    {
        TestStatus.Text = _english
            ? "Simulation successful: 3 steps would run."
            : "Simulation réussie : les 3 étapes seraient exécutées.";
    }

    internal void SetConnectionStatus(string message) => TestStatus.Text = message;

    internal void UpdateIntegrationCount(bool obsConnected, bool twitchConnected)
    {
        ObsActionSection.IsVisible = obsConnected;
        TwitchActionSection.IsVisible = twitchConnected;
        CreatorAvatar.Text = twitchConnected ? "T" : "C";
        CreatorImage.IsVisible = false;
        if (twitchConnected && TwitchSessionState.Identity?.ProfileImageUrl is { } imageUrl)
        {
            _ = LoadCreatorImageAsync(imageUrl);
        }
        CreatorTitle.Text = twitchConnected && TwitchSessionState.Identity is not null
            ? TwitchSessionState.Identity.Login
            : "Créateur";
        CreatorSubtitle.Text = twitchConnected && TwitchSessionState.Identity is not null
            ? "Twitch"
            : "Espace local  ·  Privé";

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

    private async void OnSettingsClick(object? sender, RoutedEventArgs eventArgs)
    {
        var window = new SettingsWindow(_english ? "English" : "Français");
        await window.ShowDialog(this);
        if (window.SavedLanguage is { } language)
        {
            ApplyLanguage(language == "English");
        }
    }

    private void OnSettingsPointerPressed(object? sender, PointerPressedEventArgs eventArgs) =>
        OnSettingsClick(sender, new RoutedEventArgs());

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

    private async Task LoadCreatorImageAsync(string imageUrl)
    {
        try
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(imageUrl);
            await using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CreatorImage.Source = bitmap;
                CreatorImage.IsVisible = true;
                CreatorAvatar.IsVisible = false;
            });
        }
        catch
        {
            CreatorImage.IsVisible = false;
            CreatorAvatar.IsVisible = true;
        }
    }

    private void OnLanguageClick(object? sender, RoutedEventArgs eventArgs)
    {
        ApplyLanguage(!_english);
    }

    internal void ApplyLanguage(bool english)
    {
        _english = english;
        var translations = _english
            ? new Dictionary<string, string>
            {
                ["Mon Live Automatisation"] = "My Live Automation", ["Accueil"] = "Home", ["Workflows"] = "Workflows",
                ["Événements"] = "Events", ["Actions"] = "Actions", ["Intégrations"] = "Integrations",
                ["Variables"] = "Variables", ["Pages"] = "Pages", ["Paramètres"] = "Settings",
                ["Tester"] = "Test", ["Enregistrer"] = "Save", ["Démarrer"] = "Start",
                ["Bibliothèque de modèles"] = "Template library", ["Voir tout  ›"] = "View all  ›",
                ["Twitch"] = "Twitch", ["OBS Studio"] = "OBS Studio", ["CORE"] = "CORE",
                ["Rechercher une action…"] = "Search for an action…", ["Données enregistrées localement"] = "Data saved locally",
                ["Moteur actif"] = "Engine active", ["Intégrations"] = "Integrations"
            }
            : new Dictionary<string, string>
            {
                ["My Live Automation"] = "Mon Live Automatisation", ["Home"] = "Accueil", ["Events"] = "Événements",
                ["Integrations"] = "Intégrations", ["Settings"] = "Paramètres", ["Test"] = "Tester",
                ["Save"] = "Enregistrer", ["Start"] = "Démarrer", ["Template library"] = "Bibliothèque de modèles",
                ["View all  ›"] = "Voir tout  ›", ["Search for an action…"] = "Rechercher une action…",
                ["Data saved locally"] = "Données enregistrées localement", ["Engine active"] = "Moteur actif"
            };

        foreach (var control in this.GetVisualDescendants().OfType<TextBlock>())
        {
            if (control.Text is { } text && translations.TryGetValue(text, out var translated))
            {
                control.Text = translated;
            }
        }
    }
}
