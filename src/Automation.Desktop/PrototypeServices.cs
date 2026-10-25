using System.Text.Json.Nodes;
using Automation.Application.Follows;
using Automation.Connectors.Twitch;
using Automation.Domain.Execution;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Desktop;

internal static class PrototypeServices
{
    private static readonly AppDataPaths Paths = new();
    private static readonly SqliteDatabase Database = new(Paths);
    private static bool initialized;
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);

    internal static async Task<FollowerProcessingResult> ProcessFollowAsync(
        TwitchFollowEvent follow,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var obs = ObsSessionState.Client;
        if (obs is null || !obs.IsConnected)
        {
            throw new InvalidOperationException(
                "OBS n’est pas connecté. Ouvre Intégrations et connecte OBS avant le prochain follower.");
        }

        var eventEnvelope = new EventEnvelope(
            Guid.NewGuid(),
            "twitch",
            "twitch-principal",
            follow.MessageId,
            "channel.follow",
            2,
            DateTimeOffset.UtcNow,
            new JsonObject
            {
                ["userId"] = follow.UserId,
                ["userLogin"] = follow.UserLogin,
                ["userName"] = follow.UserName,
                ["broadcasterUserId"] = follow.BroadcasterUserId,
                ["followedAt"] = follow.FollowedAt.ToString("O")
            });

        var service = new FollowerAutomationService(
            new SqliteEventAdmissionStore(Database),
            new SqliteExecutionStore(Database),
            new SqlitePersistentVariableStore(Database),
            obs);
        return await service.ProcessAsync(eventEnvelope, cancellationToken);
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
            if (!initialized)
            {
                await Database.InitializeAsync(cancellationToken);
                initialized = true;
            }
        }
        finally
        {
            InitializationGate.Release();
        }
    }
}
