using System.Globalization;
using Automation.Connectors.Obs;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Automation.Desktop;

public sealed partial class ObsConnectionWindow : Window
{
    private ObsWebSocketClient? connectedClient;

    public ObsConnectionWindow()
    {
        InitializeComponent();
        connectedClient = ObsSessionState.Client;
        if (connectedClient?.IsConnected == true)
        {
            ConnectionStatus.Text = "OBS est connecté";
            ConnectionDetail.Text = "La session WebSocket 5.x est active en mémoire.";
            StatusDot.Background = new SolidColorBrush(Color.Parse("#53D994"));
            TestSceneButton.IsEnabled = true;
        }
    }

    private async void OnTestConnectionClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (!int.TryParse(ServerPort.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            ShowFailure("Le port doit être un nombre compris entre 1 et 65535.");
            return;
        }

        var host = ServerHost.Text?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowFailure("Indique l’adresse du serveur OBS.");
            return;
        }

        TestConnectionButton.IsEnabled = false;
        ConnectionStatus.Text = "Connexion en cours…";
        ConnectionDetail.Text = $"Tentative vers {host}:{port}";
        StatusDot.Background = new SolidColorBrush(Color.Parse("#F1B85B"));

        try
        {
            if (connectedClient is not null)
            {
                await connectedClient.DisposeAsync();
                connectedClient = null;
                ObsSessionState.Client = null;
            }

            var client = new ObsWebSocketClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await client.ConnectAsync(
                    new Uri($"ws://{host}:{port}"),
                    ServerPassword.Text,
                    timeout.Token);
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }

            connectedClient = client;
            ObsSessionState.Client = client;
            await ConnectionPersistence.SaveObsAsync(
                host,
                port,
                ServerPassword.Text ?? string.Empty,
                timeout.Token);
            ConnectionStatus.Text = "OBS est connecté";
            ConnectionDetail.Text = "Connexion enregistrée dans le coffre de secrets du système.";
            StatusDot.Background = new SolidColorBrush(Color.Parse("#53D994"));
            TestSceneButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            ShowFailure(ToFriendlyMessage(exception));
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private async void OnTestSceneClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (connectedClient is null)
        {
            ShowFailure("Teste d’abord la connexion OBS.");
            return;
        }

        TestSceneButton.IsEnabled = false;
        try
        {
            await connectedClient.SetCurrentProgramSceneAsync(
                "obs-principal",
                "Merci");
            ConnectionStatus.Text = "Scène Merci sélectionnée";
            ConnectionDetail.Text = "OBS a confirmé la commande SetCurrentProgramScene.";
            StatusDot.Background = new SolidColorBrush(Color.Parse("#53D994"));
        }
        catch (Exception exception)
        {
            ShowFailure(ToFriendlyMessage(exception));
        }
        finally
        {
            TestSceneButton.IsEnabled = connectedClient.IsConnected;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs eventArgs) => Close();

    private void ShowFailure(string message)
    {
        ConnectionStatus.Text = "Connexion impossible";
        ConnectionDetail.Text = message;
        StatusDot.Background = new SolidColorBrush(Color.Parse("#FF5368"));
    }

    private static string ToFriendlyMessage(Exception exception) => exception switch
    {
        OperationCanceledException => "OBS n’a pas répondu dans les 10 secondes.",
        ObsRequestException request => $"OBS a refusé la demande : {request.Message}",
        InvalidOperationException invalid => invalid.Message,
        _ => "Vérifie que le serveur WebSocket OBS est actif, puis réessaie."
    };
}
