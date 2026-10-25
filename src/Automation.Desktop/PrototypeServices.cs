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
    internal static readonly Dictionary<string, bool> SourceVisibility = new(StringComparer.Ordinal);
    internal static readonly Dictionary<string, JsonObject> ActionOptions = new(StringComparer.Ordinal);

    internal static async Task<IReadOnlyList<WorkflowDefinition>> GetDraftsAsync()
    {
        await EnsureInitializedAsync(default);
        var drafts = await new SqliteWorkflowRepository(Database).GetAllDraftsAsync();
        foreach (var block in drafts.SelectMany(draft => draft.Blocks))
            if (block.Parameters["label"]?.GetValue<string>() is { } key)
                ActionOptions[key] = (JsonObject)block.Parameters.DeepClone();
        foreach (var block in drafts.SelectMany(draft => draft.Blocks).Where(block => block.Type == "obs.set-source-visible"))
            if (block.Parameters["label"]?.GetValue<string>() is { } label)
                SourceVisibility[label] = block.Parameters["visible"]?.GetValue<bool>() ?? !(label.Contains("Hide", StringComparison.OrdinalIgnoreCase) || label.Contains("Masquer", StringComparison.OrdinalIgnoreCase));
        return drafts;
    }

    internal static async Task<IReadOnlyList<Guid>> GetEnabledWorkflowIdsAsync()
    {
        await EnsureInitializedAsync(default);
        return (await new SqliteWorkflowRepository(Database).GetEnabledPublishedAsync()).Select(workflow => workflow.Id).ToArray();
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
                : diagnostic.Message).Concat(ExecutableWorkflowValidator.Validate(CreatePrototypeWorkflow(actions, links), UiLanguageState.IsEnglish).Select(diagnostic => diagnostic.Message)).ToArray();
    private static readonly AppDataPaths Paths = new();
    private static readonly SqliteDatabase Database = new(Paths);
    private static bool initialized;
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);
    private static readonly System.Net.Http.HttpClient Http = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> Running = new();

    internal static async Task<FollowerProcessingResult> ProcessFollowAsync(
        TwitchFollowEvent follow,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

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

        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
            var repository = new SqliteWorkflowRepository(Database);
            var workflows = await repository.GetEnabledPublishedAsync(cancellationToken);
            var requests = workflows.Select(workflow => new ExecutionRequest(Guid.NewGuid(), eventEnvelope.Id, workflow.Id, workflow.Revision, DateTimeOffset.UtcNow)).ToArray();
            var admission = await new SqliteEventAdmissionStore(Database).TryAdmitAsync(eventEnvelope, requests, cancellationToken);
            ExecutionStatus? status = null;
            if (admission == Automation.Contracts.Persistence.EventAdmissionResult.Admitted)
            {
                status = await DrainQueueAsync(repository, requests.Select(request => request.Id), cancellationToken);
            }
            return new FollowerProcessingResult(admission, status, await new SqlitePersistentVariableStore(Database).GetIntegerAsync("followersTraites", cancellationToken) ?? 0);
        }
        finally { ExecutionGate.Release(); }
    }

    internal static async Task<ExecutionStatus?> RunPublishedOnceAsync(CancellationToken token = default)
    {
        var workflowId = CurrentWorkflowId;
        await EnsureInitializedAsync(token);
        var repository = new SqliteWorkflowRepository(Database);
        var workflow = await repository.GetPublishedAsync(workflowId, token)
            ?? throw new InvalidOperationException(UiLanguageState.IsEnglish ? "Publish this workflow first using Start." : "Publie d’abord ce workflow avec Démarrer.");
        EnsureValid(workflow);
        var errors = ExecutableWorkflowValidator.Validate(workflow, UiLanguageState.IsEnglish);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" · ", errors.Select(error => error.Message)));
        var envelope = new EventEnvelope(Guid.NewGuid(), "manual", "local", Guid.NewGuid().ToString("N"), "manual.run", 1, DateTimeOffset.UtcNow,
            new JsonObject { ["userName"] = workflow.Name, ["workflowName"] = workflow.Name });
        await ExecutionGate.WaitAsync(token);
        try
        {
            var request = new ExecutionRequest(Guid.NewGuid(), envelope.Id, workflow.Id, workflow.Revision, DateTimeOffset.UtcNow);
            await new SqliteEventAdmissionStore(Database).TryAdmitAsync(envelope, [request], token);
            return await DrainQueueAsync(repository, [request.Id], token, workflowId);
        }
        finally { ExecutionGate.Release(); }
    }

    private static async Task<ExecutionStatus?> DrainQueueAsync(SqliteWorkflowRepository repository, IEnumerable<Guid> requestIds, CancellationToken token, Guid? manualWorkflow = null)
    {
        ExecutionStatus? result = null;
        Guid runningId = Guid.Empty;
        CancellationTokenSource? executionCancellation = null;
        var runner = new WorkflowExecutionRunner(new SqliteExecutionStore(Database), async (id, revision, ct) =>
        {
            if (id != manualWorkflow && !(await repository.GetEnabledPublishedAsync(ct)).Any(workflow => workflow.Id == id))
                throw new OperationCanceledException("Automation stopped before execution.");
            runningId = id;
            executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Running[id] = executionCancellation;
            return await repository.GetRevisionAsync(id, revision, ct);
        }, (block, ct) => ExecuteBlockAsync(block, executionCancellation?.Token ?? ct));
        foreach (var requestId in requestIds)
        {
            ExecutionStatus? next;
            try { next = await runner.RunNextAsync(token, requestId); }
            finally
            {
                if (runningId != Guid.Empty) Running.TryRemove(runningId, out _);
                executionCancellation?.Dispose(); executionCancellation = null;
            }
            if (next is null) continue;
            if (result != ExecutionStatus.Failed) result = next;
        }
        return result;
    }

    private static async Task<string> ExecuteBlockAsync(WorkflowBlock block, CancellationToken token)
    {
        if (block.Type == "twitch.follow.received") return "Follower event received";
        if (block.Parameters["label"] is null) throw new InvalidOperationException("Republish this legacy prototype workflow before running it.");
        string Required(string key) => block.Parameters[key]?.ToString() is { Length: > 0 } value ? value
            : throw new InvalidOperationException($"Missing parameter: {key}");
        var obs = ObsSessionState.Client;
        switch (block.Type)
        {
            case "obs.set-source-visible":
                if (obs?.IsConnected != true) throw new InvalidOperationException("OBS disconnected");
                var sources = block.Parameters["sources"]?.AsArray().Select(value => value!.GetValue<string>()).ToArray()
                    ?? (block.Parameters["source"] is { } source ? new[] { source.ToString() } : []);
                if (sources.Length == 0) throw new InvalidOperationException("Select at least one OBS source");
                var visible = block.Parameters["visible"]?.GetValue<bool>() ?? true;
                await obs.SetSourceVisibilityAsync(Required("scene"), sources, visible, token);
                return $"OBS: {sources.Length} source(s) {(visible ? "shown" : "hidden")}";
            case "obs.scene.set-current":
                if (obs?.IsConnected != true) throw new InvalidOperationException("OBS disconnected");
                await obs.SetCurrentProgramSceneAsync("obs-principal", Required("scene"), token);
                return $"OBS scene: {Required("scene")}";
            case "obs.replay.start":
                if (obs?.IsConnected != true) throw new InvalidOperationException("OBS disconnected");
                await obs.StartReplayBufferAsync(token);
                return "OBS replay buffer started";
            case "twitch.update-title":
                var identity = TwitchSessionState.Identity ?? throw new InvalidOperationException("Twitch disconnected");
                var credentials = TwitchSessionState.Token ?? throw new InvalidOperationException("Twitch disconnected");
                await new TwitchChannelClient(Http).SetTitleAsync(identity, credentials, Required("title"), token);
                return "Twitch title updated";
            case "twitch.send-message":
            case "twitch.create-clip":
            case "twitch.get-user":
                var twitchIdentity = TwitchSessionState.Identity ?? throw new InvalidOperationException("Twitch disconnected");
                var twitchToken = TwitchSessionState.Token ?? throw new InvalidOperationException("Twitch disconnected");
                var twitch = new TwitchChannelClient(Http);
                if (block.Type == "twitch.send-message")
                {
                    await twitch.SendMessageAsync(twitchIdentity, twitchToken, Required("message"), token);
                    return "Twitch message sent";
                }
                if (block.Type == "twitch.create-clip") return "Twitch clip: " + await twitch.CreateClipAsync(twitchIdentity, twitchToken, token);
                return "Twitch user: " + await twitch.GetUserAsync(twitchIdentity, twitchToken, Required("login"), token);
            case "core.delay":
                if (!double.TryParse(Required("seconds"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > 3600)
                    throw new InvalidOperationException("Delay must be between 0 and 3600 seconds");
                await Task.Delay(TimeSpan.FromSeconds(seconds), token);
                return $"Wait: {seconds} s";
            case "variables.set-integer":
            case "variables.increment":
                var name = Required("name");
                var value = long.Parse(Required("value"), System.Globalization.CultureInfo.InvariantCulture);
                var variables = new SqlitePersistentVariableStore(Database);
                if (block.Type == "variables.increment") value = await variables.IncrementAsync(name, value, token);
                else await variables.SetIntegerAsync(name, value, token);
                return $"Variable {name}: {value}";
            default: throw new NotSupportedException($"Action not implemented: {block.Type}");
        }
    }

    internal static async Task SavePrototypeDraftAsync(IReadOnlyCollection<string>? selectedActions = null, IReadOnlyCollection<(string From, string To)>? manualLinks = null, CancellationToken cancellationToken = default)
    {
        var workflow = CreatePrototypeWorkflow(selectedActions, manualLinks);
        await EnsureInitializedAsync(cancellationToken);
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
        var workflows = await new SqliteWorkflowRepository(Database).GetEnabledPublishedAsync(cancellationToken);
        return workflows.FirstOrDefault(workflow => workflow.Id == CurrentWorkflowId)?.Revision;
    }

    internal static async Task DisablePrototypeAsync(CancellationToken cancellationToken = default)
    {
        var id = CurrentWorkflowId;
        await EnsureInitializedAsync(cancellationToken);
        await new SqliteWorkflowRepository(Database).SetEnabledAsync(id, false, cancellationToken);
        if (Running.TryGetValue(id, out var running))
            try { running.Cancel(); } catch (ObjectDisposedException) { }
    }

    internal static async Task PublishPrototypeAsync(IReadOnlyCollection<string>? selectedActions = null, IReadOnlyCollection<(string From, string To)>? manualLinks = null, CancellationToken cancellationToken = default)
    {
        var workflow = CreatePrototypeWorkflow(selectedActions, manualLinks);
        await EnsureInitializedAsync(cancellationToken);
        var repository = new SqliteWorkflowRepository(Database);
        var previous = await repository.GetPublishedAsync(workflow.Id, cancellationToken);
        var revision = (previous?.Revision ?? 0) + 1;
        workflow = workflow with { Enabled = true, Revision = revision };
        EnsureValid(workflow);
        var executionErrors = ExecutableWorkflowValidator.Validate(workflow, UiLanguageState.IsEnglish);
        if (executionErrors.Count > 0) throw new InvalidOperationException(string.Join(" · ", executionErrors.Select(error => error.Message)));
        if (workflow.Blocks.Any(block => block.Type == "twitch.update-title") && TwitchSessionState.Identity?.Scopes.Contains("channel:manage:broadcast") != true)
            throw new InvalidOperationException(UiLanguageState.IsEnglish ? "Reconnect Twitch to grant permission to edit the title." : "Reconnecte Twitch pour autoriser la modification du titre.");
        foreach (var (type, scope) in new[] { ("twitch.send-message", "user:write:chat"), ("twitch.create-clip", "clips:edit") })
            if (workflow.Blocks.Any(block => block.Type == type) && TwitchSessionState.Identity?.Scopes.Contains(scope) != true)
                throw new InvalidOperationException(UiLanguageState.IsEnglish ? $"Reconnect Twitch to grant {scope}." : $"Reconnecte Twitch pour autoriser {scope}.");
        await repository.PublishAsync(workflow, cancellationToken);
    }

    private static WorkflowDefinition CreatePrototypeWorkflow(IReadOnlyCollection<string>? selectedActions, IReadOnlyCollection<(string From, string To)>? manualLinks) 
    {
        var blocks = new List<WorkflowBlock>
        {
            new(PrototypeWorkflowIds.TriggerBlockId, "twitch.follow.received", 1, new JsonObject())
        };
        var connections = new List<WorkflowConnection>
        {
        };
        var actionIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in selectedActions ?? Array.Empty<string>())
        {
            var instanceParts = label.Split('\u001f');
            var id = instanceParts.Length > 1 && Guid.TryParse(instanceParts[1], out var instanceId) ? instanceId : Guid.NewGuid();
            actionIds[label] = id;
            var parameters = ActionOptions.TryGetValue(label, out var options) ? (JsonObject)options.DeepClone() : new JsonObject();
            parameters["label"] = label;
            if (ActionTypeFor(label) == "obs.set-source-visible")
                parameters["visible"] = SourceVisibility.GetValueOrDefault(label, true);
            var type = ActionTypeFor(label);
            if (type == "variables.set-integer" && parameters["operation"]?.ToString() == "increment") type = "variables.increment";
            blocks.Add(new WorkflowBlock(id, type, 1, parameters));
        }

        foreach (var (from, to) in manualLinks ?? Array.Empty<(string From, string To)>())
        {
            if (actionIds.TryGetValue(from, out var fromId) && actionIds.TryGetValue(to, out var toId) &&
                !connections.Any(connection => connection.FromBlockId == fromId && connection.ToBlockId == toId))
                connections.Add(new WorkflowConnection(fromId, "next", toId));
        }

        // The follower entry point feeds roots only. User cards are connected
        // exclusively by the manual links, never by their insertion order.
        var incoming = connections.Select(connection => connection.ToBlockId).ToHashSet();
        foreach (var root in actionIds.Values.Where(id => !incoming.Contains(id)))
            connections.Add(new WorkflowConnection(PrototypeWorkflowIds.TriggerBlockId, "next", root));

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
        var value when value.Contains("utilisateur") || value.Contains("user") => "twitch.get-user",
        var value when value.Contains("capture") || value.Contains("screenshot") => "obs.screenshot",
        var value when value.Contains("source") => "obs.set-source-visible",
        var value when value.Contains("scène") || value.Contains("scene") => "obs.scene.set-current",
        var value when value.Contains("replay") => "obs.replay.start",
        var value when value.Contains("attendre") || value.Contains("wait") => "core.delay",
        var value when value.Contains("condition") => "core.condition",
        var value when value.Contains("variable") => "variables.set-integer",
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
