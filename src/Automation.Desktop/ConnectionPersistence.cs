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
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    private static readonly SemaphoreSlim ReconnectGate = new(1, 1);

    internal static async Task<bool> GetAutoReconnectAsync(bool twitch)
    {
        await EnsureInitializedAsync(default);
        return await Settings.GetAsync(twitch ? "twitch.autoReconnect" : "obs.autoReconnect") != "false";
    }

    internal static async Task SetAutoReconnectAsync(bool twitch, bool enabled)
    {
        await EnsureInitializedAsync(default);
        await Settings.SetAsync(twitch ? "twitch.autoReconnect" : "obs.autoReconnect", enabled ? "true" : "false");
    }

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

    internal static async Task SaveWorkflowPositionsAsync(string workflowName, IReadOnlyDictionary<string, Avalonia.Point> positions, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var payload = positions.ToDictionary(pair => pair.Key, pair => new[] { pair.Value.X, pair.Value.Y });
        await Settings.SetAsync($"workflow.layout.{workflowName}", JsonSerializer.Serialize(payload), cancellationToken);
    }

    internal static async Task<IReadOnlyDictionary<string, Avalonia.Point>> GetWorkflowPositionsAsync(string workflowName, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var json = await Settings.GetAsync($"workflow.layout.{workflowName}", cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, Avalonia.Point>();
        try
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, double[]>>(json) ?? [];
            return payload.Where(pair => pair.Value.Length >= 2).ToDictionary(pair => pair.Key, pair => new Avalonia.Point(pair.Value[0], pair.Value[1]));
        }
        catch (JsonException) { return new Dictionary<string, Avalonia.Point>(); }
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

    internal static async Task<ConnectionRestoreResult> ReconnectAsync(bool twitch, CancellationToken cancellationToken = default)
    {
        await ReconnectGate.WaitAsync(cancellationToken);
        var errors = new List<string>();
        var connected = false;
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            connected = twitch ? await RestoreTwitchAsync(errors, timeout.Token) : await RestoreObsAsync(errors, timeout.Token);
        }
        catch (Exception exception) { errors.Add(exception.Message); }
        finally { ReconnectGate.Release(); }
        return new ConnectionRestoreResult(!twitch && connected, twitch && connected, errors);
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

        var client = new ObsWebSocketClient();
        try
        {
            await client.ConnectAsync(new Uri($"ws://{host}:{port}"), password, cancellationToken);
            var previous = ObsSessionState.Client;
            ObsSessionState.Client = client;
            if (previous is not null) await previous.DisposeAsync();
            return true;
        }
        catch (Exception exception)
        {
            await client.DisposeAsync();
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

        await InitializationGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            await Database.InitializeAsync(cancellationToken);
            initialized = true;
        }
        finally { InitializationGate.Release(); }
    }
}

internal sealed record ConnectionRestoreResult(
    bool ObsConnected,
    bool TwitchConnected,
    IReadOnlyList<string> Errors);
