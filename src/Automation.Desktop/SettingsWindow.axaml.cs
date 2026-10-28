using Avalonia.Controls;
using Avalonia.Interactivity;
using Automation.Persistence.Sqlite.Storage;

namespace Automation.Desktop;

public sealed partial class SettingsWindow : Window
{
    private bool english;
    private bool reconnectOptionsLoaded;
    public SettingsWindow() : this("Français", null) { }

    private readonly Action<bool>? languageChanged;

    public SettingsWindow(string language, Action<bool>? languageChanged)
    {
        this.languageChanged = languageChanged;
        InitializeComponent();
        ApplyLanguage(language == "English");
        LanguageCombo.SelectedIndex = english ? 1 : 0;
        UpdateIntegrationStatus();
        Opened += async (_, _) =>
        {
            try
            {
                await AutoReconnect.LoadAsync();
                ObsAutoReconnectCheck.IsChecked = AutoReconnect.ObsEnabled;
                TwitchAutoReconnectCheck.IsChecked = AutoReconnect.TwitchEnabled;
                reconnectOptionsLoaded = true;
            }
            catch
            {
                var message = english ? "Unable to load saved reconnection preference." : "Impossible de charger la préférence de reconnexion.";
                ObsAutoReconnectDescription.Text = message;
                TwitchAutoReconnectDescription.Text = message;
            }
        };
    }

    public string? SavedLanguage { get; private set; }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnAutoReconnectChanged(object? sender, RoutedEventArgs e)
    {
        if (!reconnectOptionsLoaded) return;
        var twitch = sender == TwitchAutoReconnectCheck;
        var enabled = (twitch ? TwitchAutoReconnectCheck : ObsAutoReconnectCheck).IsChecked == true;
        try
        {
            await ConnectionPersistence.SetAutoReconnectAsync(twitch, enabled);
            if (twitch) AutoReconnect.TwitchEnabled = enabled; else AutoReconnect.ObsEnabled = enabled;
        }
        catch
        {
            var description = twitch ? TwitchAutoReconnectDescription : ObsAutoReconnectDescription;
            description.Text = english ? "Unable to save this preference." : "Impossible d’enregistrer cette préférence.";
        }
    }

    private async void OnBackupClick(object? sender, RoutedEventArgs e)
    {
        BackupButton.IsEnabled = false;
        BackupStatus.Text = english ? "Creating backup…" : "Création de la sauvegarde…";
        try
        {
            var file = await Task.Run(() => SqliteBackups.Create(new AppDataPaths()));
            BackupStatus.Text = (english ? "Backup created: " : "Sauvegarde créée : ") + file;
        }
        catch (Exception exception)
        {
            BackupStatus.Text = (english ? "Backup failed: " : "Échec de sauvegarde : ") + exception.Message;
        }
        finally { BackupButton.IsEnabled = true; }
    }

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
        ObsButton.IsEnabled = false;
        try
        {
        if (ObsSessionState.Client?.IsConnected == true)
        {
            AutoReconnect.ObsPaused = true;
            await ObsSessionState.Client.DisposeAsync();
            ObsSessionState.Client = null;
        }
        else
        {
            AutoReconnect.ObsPaused = false;
            ObsStatus.Text = english ? "Reconnecting…" : "Reconnexion…";
            var result = await ConnectionPersistence.ReconnectAsync(false);
            if (!result.ObsConnected && result.Errors.Count == 0) await new ObsConnectionWindow().ShowDialog(this);
            else if (result.Errors.Count > 0) { UpdateIntegrationStatus(); ObsStatus.Text = english ? "Reconnection failed. Check OBS or use Configure." : "Reconnexion impossible. Vérifie OBS ou utilise Configurer."; return; }
        }
        UpdateIntegrationStatus();
        }
        catch (Exception) { ObsStatus.Text = english ? "OBS connection failed. Try again or use Configure." : "Connexion OBS impossible. Réessaie ou utilise Configurer."; }
        finally { ObsButton.IsEnabled = true; }
    }

    private async void OnTwitchClick(object? sender, RoutedEventArgs e)
    {
        TwitchButton.IsEnabled = false;
        try
        {
        if (TwitchSessionState.EventSubClient?.IsConnected == true)
        {
            AutoReconnect.TwitchPaused = true;
            await TwitchSessionState.EventSubClient.DisposeAsync();
            TwitchSessionState.EventSubClient = null;
            TwitchSessionState.Identity = null;
            TwitchSessionState.Token = null;
        }
        else
        {
            AutoReconnect.TwitchPaused = false;
            TwitchStatus.Text = english ? "Reconnecting…" : "Reconnexion…";
            var result = await ConnectionPersistence.ReconnectAsync(true);
            if (!result.TwitchConnected && result.Errors.Count == 0) await new TwitchConnectionWindow().ShowDialog(this);
            else if (result.Errors.Count > 0) { UpdateIntegrationStatus(); TwitchStatus.Text = english ? "Reconnection failed. Retry or renew authorization if needed." : "Reconnexion impossible. Réessaie ou renouvelle l’autorisation si nécessaire."; return; }
        }
        UpdateIntegrationStatus();
        }
        catch (Exception) { TwitchStatus.Text = english ? "Twitch connection failed. Try again." : "Connexion Twitch impossible. Réessaie."; }
        finally { TwitchButton.IsEnabled = true; }
    }

    private async void OnObsConfigureClick(object? sender, RoutedEventArgs e)
    {
        await new ObsConnectionWindow().ShowDialog(this);
        UpdateIntegrationStatus();
    }

    private async void OnTwitchAuthorizeClick(object? sender, RoutedEventArgs e)
    {
        await new TwitchConnectionWindow().ShowDialog(this);
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
        ObsButton.Content = english ? (obs ? "Disconnect" : "Reconnect") : (obs ? "Déconnecter" : "Reconnecter");
        TwitchButton.Content = english ? (twitch ? "Disconnect" : "Reconnect") : (twitch ? "Déconnecter" : "Reconnecter");
    }

    private void ApplyLanguage(bool isEnglish)
    {
        english = isEnglish;
        UiLanguageState.IsEnglish = isEnglish;
        Title = english ? "Settings" : "Paramètres";
        SettingsTitle.Text = english ? "Settings" : "Paramètres";
        SettingsDescription.Text = english ? "Customize how CuezyBot behaves." : "Personnalise le comportement de CuezyBot.";
        LanguageTitle.Text = english ? "Language" : "Langue";
        LanguageDescription.Text = english ? "Choose the interface language." : "Choisis la langue de l’interface.";
        IntegrationsTitle.Text = english ? "Integrations" : "Intégrations";
        ObsAutoReconnectCheck.Content = english ? "Automatically reconnect" : "Reconnexion automatique";
        TwitchAutoReconnectCheck.Content = english ? "Automatically reconnect" : "Reconnexion automatique";
        var reconnectDescription = english ? "Retry every 15 seconds after a connection loss. A manual disconnect pauses retries." : "Réessaie toutes les 15 secondes après une coupure. Une déconnexion volontaire suspend les essais.";
        ObsAutoReconnectDescription.Text = reconnectDescription;
        TwitchAutoReconnectDescription.Text = reconnectDescription;
        ObsConfigureButton.Content = english ? "Configure" : "Configurer";
        TwitchAuthorizeButton.Content = english ? "Renew authorization" : "Renouveler l’autorisation";
        BackupsTitle.Text = english ? "Backups" : "Sauvegardes";
        BackupsDescription.Text = english ? "Save automations, settings and history in data/backups. System credentials and media files are excluded." : "Sauvegarde les automatisations, réglages et historiques dans data/backups. Les identifiants système et fichiers médias sont exclus.";
        BackupButton.Content = english ? "Create backup" : "Créer une sauvegarde";
        UpdateIntegrationStatus();
    }
}
