using System.Text.Json;
using Automation.Connectors.Obs;
using Automation.Connectors.Twitch;
using Automation.Contracts.Persistence;
using Automation.Contracts.Secrets;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;
using Automation.Platform.Secrets;

namespace Automation.Desktop;

internal static class ConnectionPersistence
{
    private const string ObsHostKey = "obs.host";
    private const string ObsPortKey = "obs.port";
    private const string ObsPasswordSecret = "obs.password";
    private const string TwitchTokenSecret = "twitch.token";
    private const string LanguageKey = "ui.language";
    private static readonly SqliteDatabase Database = new(new AppDataPaths());
    private static readonly IApplicationSettingsStore Settings = new SqliteApplicationSettingsStore(Database);
    private static readonly ISecretStore Secrets = PlatformSecretStore.Create();
    private static bool initialized;

    internal static async Task SaveLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await Settings.SetAsync(LanguageKey, language, cancellationToken);
    }

    internal static async Task<string?> GetLanguageAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await Settings.GetAsync(LanguageKey, cancellationToken);
    }

    internal static async Task SaveObsAsync(
        string host,
        int port,
        string password,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await Settings.SetAsync(ObsHostKey, host, cancellationToken);
        await Settings.SetAsync(ObsPortKey, port.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        await Secrets.StoreAsync(ObsPasswordSecret, password, cancellationToken);
    }

    internal static async Task SaveTwitchAsync(
        TwitchUserToken token,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await Secrets.StoreAsync(
            TwitchTokenSecret,
            JsonSerializer.Serialize(token),
            cancellationToken);
    }

    internal static async Task<ConnectionRestoreResult> RestoreAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var errors = new List<string>();
        var obsConnected = await RestoreObsAsync(errors, cancellationToken);
        var twitchConnected = await RestoreTwitchAsync(errors, cancellationToken);
        return new ConnectionRestoreResult(obsConnected, twitchConnected, errors);
    }

    private static async Task<bool> RestoreObsAsync(
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var host = await Settings.GetAsync(ObsHostKey, cancellationToken);
        var portText = await Settings.GetAsync(ObsPortKey, cancellationToken);
        var password = await Secrets.GetAsync(ObsPasswordSecret, cancellationToken);
        if (host is null || password is null || !int.TryParse(portText, out var port))
        {
            return false;
        }

        try
        {
            var client = new ObsWebSocketClient();
            await client.ConnectAsync(new Uri($"ws://{host}:{port}"), password, cancellationToken);
            ObsSessionState.Client = client;
            return true;
        }
        catch (Exception exception)
        {
            errors.Add($"OBS : {exception.Message}");
            return false;
        }
    }

    private static async Task<bool> RestoreTwitchAsync(
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var tokenJson = await Secrets.GetAsync(TwitchTokenSecret, cancellationToken);
        if (tokenJson is null)
        {
            return false;
        }

        try
        {
            var token = JsonSerializer.Deserialize<TwitchUserToken>(tokenJson)
                ?? throw new InvalidDataException("Le jeton Twitch enregistré est vide.");
            using var auth = new TwitchDeviceAuthClient();
            TwitchIdentity identity;
            try
            {
                identity = await auth.ValidateAsync(token, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                token = await auth.RefreshAsync(
                    TwitchAppConfiguration.ClientId,
                    token.RefreshToken,
                    cancellationToken);
                identity = await auth.ValidateAsync(token, cancellationToken);
                await SaveTwitchAsync(token, cancellationToken);
            }

            await AutomationSession.ConnectTwitchAsync(token, identity, cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            errors.Add($"Twitch : {exception.Message}");
            return false;
        }
    }

    private static async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await Database.InitializeAsync(cancellationToken);
        initialized = true;
    }
}

internal sealed record ConnectionRestoreResult(
    bool ObsConnected,
    bool TwitchConnected,
    IReadOnlyList<string> Errors);
