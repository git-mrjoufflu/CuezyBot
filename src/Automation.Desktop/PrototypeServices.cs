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

    internal static async Task SavePrototypeDraftAsync(IReadOnlyCollection<string>? selectedActions = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await new SqliteWorkflowRepository(Database).SaveDraftAsync(CreatePrototypeWorkflow(selectedActions), cancellationToken);
    }

    internal static async Task<bool> HasPrototypeDraftAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await new SqliteWorkflowRepository(Database)
            .GetDraftAsync(PrototypeWorkflowIds.WorkflowId, cancellationToken) is not null;
    }

    internal static async Task<int?> GetPublishedRevisionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var workflow = await new SqliteWorkflowRepository(Database)
            .GetPublishedAsync(PrototypeWorkflowIds.WorkflowId, cancellationToken);
        return workflow?.Enabled == true ? workflow.Revision : null;
    }

    internal static async Task DisablePrototypeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await new SqliteWorkflowRepository(Database).SetEnabledAsync(PrototypeWorkflowIds.WorkflowId, false, cancellationToken);
    }

    internal static async Task PublishPrototypeAsync(IReadOnlyCollection<string>? selectedActions = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var repository = new SqliteWorkflowRepository(Database);
        var previous = await repository.GetPublishedAsync(PrototypeWorkflowIds.WorkflowId, cancellationToken);
        var revision = (previous?.Revision ?? 0) + 1;
        await repository.PublishAsync(CreatePrototypeWorkflow(selectedActions) with { Enabled = true, Revision = revision }, cancellationToken);
    }

    private static WorkflowDefinition CreatePrototypeWorkflow(IReadOnlyCollection<string>? selectedActions) 
    {
        var blocks = new List<WorkflowBlock>
        {
            new(PrototypeWorkflowIds.TriggerBlockId, "twitch.follow", 2, new JsonObject()),
            new(PrototypeWorkflowIds.CounterBlockId, "core.increment", 1, new JsonObject()),
            new(PrototypeWorkflowIds.ObsBlockId, "obs.set-scene", 1, new JsonObject { ["scene"] = "Merci" })
        };
        var connections = new List<WorkflowConnection>
        {
            new(PrototypeWorkflowIds.TriggerBlockId, "next", PrototypeWorkflowIds.CounterBlockId),
            new(PrototypeWorkflowIds.CounterBlockId, "next", PrototypeWorkflowIds.ObsBlockId)
        };
        var previous = PrototypeWorkflowIds.ObsBlockId;
        foreach (var label in selectedActions ?? Array.Empty<string>())
        {
            var id = Guid.NewGuid();
            blocks.Add(new WorkflowBlock(id, ActionTypeFor(label), 1, new JsonObject { ["label"] = label }));
            connections.Add(new WorkflowConnection(previous, "next", id));
            previous = id;
        }

        return new WorkflowDefinition(
            PrototypeWorkflowIds.WorkflowId,
            1,
            "Mon Live Automatisation",
            false,
            blocks,
            connections);
    }

    private static string ActionTypeFor(string label) => label.ToLowerInvariant() switch
    {
        var value when value.Contains("clip") => "twitch.create-clip",
        var value when value.Contains("titre") || value.Contains("title") => "twitch.update-title",
        var value when value.Contains("message") => "twitch.send-message",
        var value when value.Contains("capture") || value.Contains("screenshot") => "obs.screenshot",
        var value when value.Contains("source") => "obs.set-source-visible",
        var value when value.Contains("scène") || value.Contains("scene") => "obs.set-scene",
        var value when value.Contains("attendre") || value.Contains("wait") => "core.delay",
        var value when value.Contains("condition") => "core.condition",
        _ => "core.action"
    };

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
