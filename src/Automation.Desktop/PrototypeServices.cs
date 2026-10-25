using System.Text.Json.Nodes;
using Automation.Application.Follows;
using Automation.Connectors.Twitch;
using Automation.Domain.Execution;
using Automation.Domain.Workflows;
using Automation.Engine.Execution;
using Automation.Engine.Blocks;
using Automation.Engine.Workflows;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Desktop;

internal static class PrototypeServices
{
    internal static Guid CurrentWorkflowId { get; set; } = PrototypeWorkflowIds.WorkflowId;
    internal static string CurrentWorkflowName { get; set; } = "Mon Live Automatisation";

    internal static async Task<IReadOnlyList<WorkflowDefinition>> GetDraftsAsync()
    {
        await EnsureInitializedAsync(default);
        return await new SqliteWorkflowRepository(Database).GetAllDraftsAsync();
    }

    internal static async Task DeleteWorkflowAsync(Guid id)
    {
        await EnsureInitializedAsync(default);
        await new SqliteWorkflowRepository(Database).DeleteAsync(id);
    }

    internal static IReadOnlyList<string> ValidateEditor(IReadOnlyCollection<string> actions, IReadOnlyCollection<(string From, string To)> links) =>
        new WorkflowValidator(new InitialBlockCatalog()).Validate(CreatePrototypeWorkflow(actions, links)).Diagnostics
            .Select(diagnostic => diagnostic.Code == "workflow.cycle" && UiLanguageState.IsEnglish
                ? "The workflow contains a loop. Remove a connection to avoid endless execution."
                : diagnostic.Message).ToArray();
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

    internal static async Task SavePrototypeDraftAsync(IReadOnlyCollection<string>? selectedActions = null, IReadOnlyCollection<(string From, string To)>? manualLinks = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var workflow = CreatePrototypeWorkflow(selectedActions, manualLinks);
        await new SqliteWorkflowRepository(Database).SaveDraftAsync(workflow, cancellationToken);
    }

    internal static async Task<bool> HasPrototypeDraftAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await new SqliteWorkflowRepository(Database)
            .GetDraftAsync(CurrentWorkflowId, cancellationToken) is not null;
    }

    internal static async Task<IReadOnlyList<string>> GetDraftActionLabelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var workflow = await new SqliteWorkflowRepository(Database).GetDraftAsync(CurrentWorkflowId, cancellationToken);
        return workflow?.Blocks
            .Skip(3)
            .Select(block => block.Parameters["label"]?.GetValue<string>())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Cast<string>()
            .ToArray() ?? [];
    }

    internal static async Task<IReadOnlyList<(string From, string To)>> GetDraftManualLinksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var workflow = await new SqliteWorkflowRepository(Database).GetDraftAsync(CurrentWorkflowId, cancellationToken);
        if (workflow is null) return [];
        var labels = workflow.Blocks.ToDictionary(
            block => block.Id,
            block => block.Parameters["label"]?.GetValue<string>());
        return workflow.Connections
            .Where(connection => labels.TryGetValue(connection.FromBlockId, out var from) &&
                                 labels.TryGetValue(connection.ToBlockId, out var to) &&
                                 !string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
            .Select(connection => (labels[connection.FromBlockId]!, labels[connection.ToBlockId]!))
            .ToArray();
    }

    internal static async Task<int?> GetPublishedRevisionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var workflow = await new SqliteWorkflowRepository(Database)
            .GetPublishedAsync(CurrentWorkflowId, cancellationToken);
        return workflow?.Enabled == true ? workflow.Revision : null;
    }

    internal static async Task DisablePrototypeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await new SqliteWorkflowRepository(Database).SetEnabledAsync(CurrentWorkflowId, false, cancellationToken);
    }

    internal static async Task PublishPrototypeAsync(IReadOnlyCollection<string>? selectedActions = null, IReadOnlyCollection<(string From, string To)>? manualLinks = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var repository = new SqliteWorkflowRepository(Database);
        var previous = await repository.GetPublishedAsync(CurrentWorkflowId, cancellationToken);
        var revision = (previous?.Revision ?? 0) + 1;
        var workflow = CreatePrototypeWorkflow(selectedActions, manualLinks) with { Enabled = true, Revision = revision };
        EnsureValid(workflow);
        await repository.PublishAsync(workflow, cancellationToken);
    }

    private static WorkflowDefinition CreatePrototypeWorkflow(IReadOnlyCollection<string>? selectedActions, IReadOnlyCollection<(string From, string To)>? manualLinks) 
    {
        var blocks = new List<WorkflowBlock>
        {
            new(PrototypeWorkflowIds.TriggerBlockId, "twitch.follow.received", 1, new JsonObject()),
            new(PrototypeWorkflowIds.CounterBlockId, "variables.increment", 1, new JsonObject()),
            new(PrototypeWorkflowIds.ObsBlockId, "obs.scene.set-current", 1, new JsonObject { ["scene"] = "Merci" })
        };
        var connections = new List<WorkflowConnection>
        {
            new(PrototypeWorkflowIds.TriggerBlockId, "next", PrototypeWorkflowIds.CounterBlockId),
            new(PrototypeWorkflowIds.CounterBlockId, "next", PrototypeWorkflowIds.ObsBlockId)
        };
        var previous = PrototypeWorkflowIds.ObsBlockId;
        var actionIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in selectedActions ?? Array.Empty<string>())
        {
            var id = Guid.NewGuid();
            actionIds[label] = id;
            blocks.Add(new WorkflowBlock(id, ActionTypeFor(label), 1, new JsonObject { ["label"] = label }));
            connections.Add(new WorkflowConnection(previous, "next", id));
            previous = id;
        }

        foreach (var (from, to) in manualLinks ?? Array.Empty<(string From, string To)>())
        {
            if (actionIds.TryGetValue(from, out var fromId) && actionIds.TryGetValue(to, out var toId) &&
                !connections.Any(connection => connection.FromBlockId == fromId && connection.ToBlockId == toId))
                connections.Add(new WorkflowConnection(fromId, "next", toId));
        }

        return new WorkflowDefinition(
            CurrentWorkflowId,
            1,
            CurrentWorkflowName,
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

    private static void EnsureValid(WorkflowDefinition workflow)
    {
        var result = new WorkflowValidator(new InitialBlockCatalog()).Validate(workflow);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
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
