using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Automation.Desktop;

public sealed partial class IntegrationsWindow : Window
{
    public IntegrationsWindow()
    {
        InitializeComponent();
        ApplyLanguage(UiLanguageState.IsEnglish);
    }

    private void ApplyLanguage(bool english)
    {
        Title = english ? "Integrations — CuezyBot" : "Intégrations — CuezyBot";
        IntegrationsTitle.Text = english ? "Integrations" : "Intégrations";
        IntegrationsDescription.Text = english
            ? "Connect the services used in your automations."
            : "Connecte les services utilisés dans tes automatisations.";
        ObsButton.Content = english ? "Configure OBS" : "Configurer OBS";
        TwitchButton.Content = english ? "Connect Twitch" : "Connecter Twitch";
        CloseButton.Content = english ? "Close" : "Fermer";
        foreach (var text in this.GetVisualDescendants().OfType<TextBlock>())
        {
            text.Text = text.Text switch
            {
                "Change de scène et contrôle OBS depuis les workflows CuezyBot." => english ? "Change scenes and control OBS from CuezyBot workflows." : text.Text,
                "Reçois les follows et autres événements autorisés de ta chaîne." => english ? "Receive follows and other authorized events from your channel." : text.Text,
                _ => text.Text
            };
        }
    }

    private async void OnObsClick(object? sender, RoutedEventArgs eventArgs) =>
        await new ObsConnectionWindow().ShowDialog(this);

    private async void OnTwitchClick(object? sender, RoutedEventArgs eventArgs) =>
        await new TwitchConnectionWindow().ShowDialog(this);

    private void OnCloseClick(object? sender, RoutedEventArgs eventArgs) => Close();
}
