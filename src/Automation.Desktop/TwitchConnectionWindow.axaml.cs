using System.Diagnostics;
using Automation.Connectors.Twitch;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Automation.Desktop;

public sealed partial class TwitchConnectionWindow : Window
{
    private readonly TwitchDeviceAuthClient authClient = new();
    private TwitchDeviceAuthorization? authorization;
    private CancellationTokenSource? authorizationCancellation;

    public TwitchConnectionWindow()
    {
        InitializeComponent();
        ApplyLanguage(UiLanguageState.IsEnglish);
    }

    private void ApplyLanguage(bool english)
    {
        foreach (var text in this.GetVisualDescendants().OfType<TextBlock>())
        {
            text.Text = text.Text switch
            {
                "Connecter Twitch" => english ? "Connect Twitch" : "Connecter Twitch",
                "Fermer" => english ? "Close" : "Fermer",
                "Ouvrir Twitch" => english ? "Open Twitch" : "Ouvrir Twitch",
                "Commencer la connexion" => english ? "Start connection" : "Commencer la connexion",
                "Permission demandée" => english ? "Permission requested" : "Permission demandée",
                _ => text.Text
            };
        }
        OpenBrowserButton.Content = english ? "Open Twitch" : "Ouvrir Twitch";
        ConnectButton.Content = english ? "Start connection" : "Commencer la connexion";
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs eventArgs)
    {
        authorizationCancellation?.Cancel();
        authorizationCancellation?.Dispose();
        authorizationCancellation = new CancellationTokenSource();
        ConnectButton.IsEnabled = false;

        try
        {
            ConnectionStatus.Text = "Création du code…";
            ConnectionDetail.Text = "CuezyBot contacte le service d’autorisation Twitch.";
            StatusDot.Background = new SolidColorBrush(Color.Parse("#F1B85B"));

            authorization = await authClient.StartAsync(
                TwitchAppConfiguration.ClientId,
                TwitchAppConfiguration.PrototypeScopes,
                authorizationCancellation.Token);
            UserCode.Text = authorization.UserCode;
            VerificationAddress.Text = authorization.VerificationUri.ToString();
            OpenBrowserButton.IsEnabled = true;
            OpenInBrowser(authorization.VerificationUri);

            ConnectionStatus.Text = "En attente de ton autorisation…";
            ConnectionDetail.Text = "Entre le code dans Twitch puis accepte la permission demandée.";

            var token = await authClient.WaitForAuthorizationAsync(
                TwitchAppConfiguration.ClientId,
                authorization,
                authorizationCancellation.Token);
            var identity = await authClient.ValidateAsync(token, authorizationCancellation.Token);

            if (!identity.Scopes.Contains("moderator:read:followers", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("La permission de lire les followers n’a pas été accordée.");
            }

            await AutomationSession.ConnectTwitchAsync(
                token,
                identity,
                authorizationCancellation.Token);
            await ConnectionPersistence.SaveTwitchAsync(token, authorizationCancellation.Token);

            ConnectionStatus.Text = $"Twitch actif : {identity.Login}";
            ConnectionDetail.Text = "EventSub écoute maintenant les nouveaux followers.";
            StatusDot.Background = new SolidColorBrush(Color.Parse("#53D994"));
        }
        catch (OperationCanceledException)
        {
            ShowFailure("La connexion Twitch a été annulée.");
        }
        catch (Exception exception)
        {
            ShowFailure(exception.Message);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void OnOpenBrowserClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (authorization is not null)
        {
            OpenInBrowser(authorization.VerificationUri);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs eventArgs)
    {
        authorizationCancellation?.Cancel();
        Close();
    }

    private static void OpenInBrowser(Uri address)
    {
        Process.Start(new ProcessStartInfo(address.ToString()) { UseShellExecute = true });
    }

    private void ShowFailure(string message)
    {
        ConnectionStatus.Text = "Connexion Twitch impossible";
        ConnectionDetail.Text = message;
        StatusDot.Background = new SolidColorBrush(Color.Parse("#FF5368"));
    }

    protected override void OnOpened(EventArgs eventArgs)
    {
        base.OnOpened(eventArgs);
        AutomationSession.FollowProcessed += OnFollowProcessed;
        if (TwitchSessionState.EventSubClient?.IsConnected == true && TwitchSessionState.Identity is not null)
        {
            ConnectionStatus.Text = $"Twitch actif : {TwitchSessionState.Identity.Login}";
            ConnectionDetail.Text = "Connexion restaurée et EventSub actif.";
            StatusDot.Background = new SolidColorBrush(Color.Parse("#53D994"));
        }
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        AutomationSession.FollowProcessed -= OnFollowProcessed;
        authorizationCancellation?.Cancel();
        authorizationCancellation?.Dispose();
        authClient.Dispose();
        base.OnClosed(eventArgs);
    }

    private void OnFollowProcessed(object? sender, FollowProcessedEvent processed)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (processed.Error is not null)
            {
                ShowFailure(processed.Error);
                return;
            }

            ConnectionStatus.Text = $"Nouveau follower traité : {processed.Follow.UserName}";
            ConnectionDetail.Text =
                $"Compteur : {processed.Result!.CounterValue}. Exécution : {processed.Result.ExecutionStatus?.ToString() ?? "déjà traitée"}.";
            StatusDot.Background = new SolidColorBrush(Color.Parse("#53D994"));
        });
    }
}
