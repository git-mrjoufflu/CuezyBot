using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Automation.Desktop;

public sealed partial class SettingsWindow : Window
{
    private bool english;
    public SettingsWindow() : this("Français", null) { }

    private readonly Action<bool>? languageChanged;

    public SettingsWindow(string language, Action<bool>? languageChanged)
    {
        this.languageChanged = languageChanged;
        InitializeComponent();
        ApplyLanguage(language == "English");
        LanguageCombo.SelectedIndex = english ? 1 : 0;
        UpdateIntegrationStatus();
    }

    public string? SavedLanguage { get; private set; }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo is null)
        {
            return;
        }

        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string language)
        {
            SavedLanguage = language;
            ApplyLanguage(language == "English");
            languageChanged?.Invoke(language == "English");
            await ConnectionPersistence.SaveLanguageAsync(language);
        }
    }

    private async void OnObsClick(object? sender, RoutedEventArgs e)
    {
        if (ObsSessionState.Client?.IsConnected == true)
        {
            await ObsSessionState.Client.DisposeAsync();
            ObsSessionState.Client = null;
        }
        else
        {
            await new ObsConnectionWindow().ShowDialog(this);
        }
        UpdateIntegrationStatus();
    }

    private async void OnTwitchClick(object? sender, RoutedEventArgs e)
    {
        if (TwitchSessionState.EventSubClient?.IsConnected == true)
        {
            await TwitchSessionState.EventSubClient.DisposeAsync();
            TwitchSessionState.EventSubClient = null;
            TwitchSessionState.Identity = null;
            TwitchSessionState.Token = null;
        }
        else
        {
            await new TwitchConnectionWindow().ShowDialog(this);
        }
        UpdateIntegrationStatus();
    }

    private void UpdateIntegrationStatus()
    {
        var obs = ObsSessionState.Client?.IsConnected == true;
        var twitch = TwitchSessionState.EventSubClient?.IsConnected == true;
        ObsStatus.Text = english ? (obs ? "Connected" : "Disconnected") : (obs ? "Connecté" : "Non connecté");
        TwitchStatus.Text = english ? (twitch ? "Connected" : "Disconnected") : (twitch ? "Connecté" : "Non connecté");
        ObsStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(obs ? "#72F0AA" : "#FFD081"));
        TwitchStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(twitch ? "#72F0AA" : "#FFD081"));
        ObsButton.Content = english ? (obs ? "Disconnect" : "Configure OBS") : (obs ? "Déconnecter" : "Configurer OBS");
        TwitchButton.Content = english ? (twitch ? "Disconnect" : "Connect Twitch") : (twitch ? "Déconnecter" : "Connecter Twitch");
    }

    private void ApplyLanguage(bool isEnglish)
    {
        english = isEnglish;
        Title = english ? "Settings" : "Paramètres";
        SettingsTitle.Text = english ? "Settings" : "Paramètres";
        SettingsDescription.Text = english ? "Customize how CuezyBot behaves." : "Personnalise le comportement de CuezyBot.";
        LanguageTitle.Text = english ? "Language" : "Langue";
        LanguageDescription.Text = english ? "Choose the interface language." : "Choisis la langue de l’interface.";
        IntegrationsTitle.Text = english ? "Integrations" : "Intégrations";
        UpdateIntegrationStatus();
    }
}
