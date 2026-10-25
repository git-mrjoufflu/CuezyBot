using System.Text.Json.Nodes;
using Automation.Application.Follows;
using Automation.Connectors.Twitch;
using Automation.Domain.Execution;
using Automation.Domain.Workflows;
using Automation.Engine.Execution;
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

    internal static async Task SavePrototypeDraftAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await new SqliteWorkflowRepository(Database).SaveDraftAsync(CreatePrototypeWorkflow(), cancellationToken);
    }

    internal static async Task PublishPrototypeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var repository = new SqliteWorkflowRepository(Database);
        var previous = await repository.GetPublishedAsync(PrototypeWorkflowIds.WorkflowId, cancellationToken);
        var revision = (previous?.Revision ?? 0) + 1;
        await repository.PublishAsync(CreatePrototypeWorkflow() with { Enabled = true, Revision = revision }, cancellationToken);
    }

    private static WorkflowDefinition CreatePrototypeWorkflow() => new(
            PrototypeWorkflowIds.WorkflowId,
            1,
            "Mon Live Automatisation",
            false,
            new[]
            {
                new WorkflowBlock(PrototypeWorkflowIds.TriggerBlockId, "twitch.follow", 2, new JsonObject()),
                new WorkflowBlock(PrototypeWorkflowIds.CounterBlockId, "core.increment", 1, new JsonObject()),
                new WorkflowBlock(PrototypeWorkflowIds.ObsBlockId, "obs.set-scene", 1, new JsonObject { ["scene"] = "Merci" })
            },
            new[]
            {
                new WorkflowConnection(PrototypeWorkflowIds.TriggerBlockId, "next", PrototypeWorkflowIds.CounterBlockId),
                new WorkflowConnection(PrototypeWorkflowIds.CounterBlockId, "next", PrototypeWorkflowIds.ObsBlockId)
            });

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
