using System.Text.Json.Nodes;
using Automation.Application.Follows;
using Automation.Connectors.Twitch;
using Automation.Domain.Execution;
using Automation.Domain.Workflows;
using Automation.Engine.Execution;
using Automation.Engine.Blocks;
using Automation.Engine.Workflows;
using Automation.Engine.Variables;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;
using System.Text.RegularExpressions;

namespace Automation.Desktop;

internal static class PrototypeServices
{
    internal static Guid CurrentWorkflowId { get; set; } = PrototypeWorkflowIds.WorkflowId;
    internal static string CurrentWorkflowName { get; set; } = "Mon Live Automatisation";
    internal static string CurrentTriggerType { get; set; } = "";
    internal static List<string> CurrentTriggerTypes { get; } = new();
    internal static readonly Dictionary<string, bool> SourceVisibility = new(StringComparer.Ordinal);
    internal static readonly Dictionary<string, JsonObject> ActionOptions = new(StringComparer.Ordinal);
    internal static readonly Dictionary<string, JsonObject> TriggerOptions = new(StringComparer.Ordinal);

    internal static async Task<IReadOnlyList<WorkflowDefinition>> GetDraftsAsync()
    {
        await EnsureInitializedAsync(default);
        var drafts = await new SqliteWorkflowRepository(Database).GetAllDraftsAsync();
        foreach (var draft in drafts)
        {
            var triggers = draft.Blocks.Where(block => IsTriggerType(block.Type)).ToArray();
            for (var index = 0; index < triggers.Length; index++)
            {
                var key = index == 0 ? "__trigger__" : $"__trigger__:{triggers[index].Type}";
                triggers[index].Parameters["triggerKey"] = key;
                TriggerOptions[TriggerOptionStorageKey(draft.Id, key)] = (JsonObject)triggers[index].Parameters.DeepClone();
            }
        }
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
        if (Running.TryGetValue(id, out var running))
            try { running.Cancel(); } catch (ObjectDisposedException) { }
        await new SqliteWorkflowRepository(Database).DeleteAsync(id);
    }

    internal static IReadOnlyList<Automation.Contracts.Workflows.WorkflowDiagnostic> ValidateEditorDetails(IReadOnlyCollection<string> actions, IReadOnlyCollection<(string From, string To)> links)
    {
        var workflow = CreatePrototypeWorkflow(actions, links);
        return new WorkflowValidator(new InitialBlockCatalog()).Validate(workflow).Diagnostics
            .Select(diagnostic => diagnostic.Code == "workflow.cycle" && UiLanguageState.IsEnglish
                ? diagnostic with { Message = "The workflow contains a loop. Remove a connection to avoid endless execution." }
                : diagnostic).Concat(ExecutableWorkflowValidator.Validate(workflow, UiLanguageState.IsEnglish)).ToArray();
    }

    internal static bool HasCycle(IReadOnlyCollection<string> actions, IReadOnlyCollection<(string From, string To)> links) =>
        ValidateEditorDetails(actions, links).Any(diagnostic => diagnostic.Code == "workflow.cycle");
    private static readonly AppDataPaths Paths = new();
    private static readonly SqliteDatabase Database = new(Paths);
    private static bool initialized;
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    internal static AlertExecutionQueue ExecutionGate => AutomationQueues.Get("Default");
    private static readonly System.Net.Http.HttpClient Http = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> Running = new();
    private static readonly Regex VariableToken = new("%([A-Za-z_][A-Za-z0-9_.-]{0,63})%", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly AsyncLocal<string?> CurrentViewerId = new();
    private static readonly AsyncLocal<string?> CurrentViewerLogin = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> CommandCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private sealed record TimerSchedule(int Revision, double Seconds, DateTimeOffset NextRun);
    private static readonly Dictionary<(Guid Workflow, Guid Trigger), TimerSchedule> TimerSchedules = new();
    private static int processingTimers;

    internal static bool IsTriggerType(string type) => type is "manual.run" or "core.timer" || TwitchTriggerCatalog.FindBlock(type) is not null;
    internal static string TriggerOptionStorageKey(Guid workflowId, string canvasKey) => $"{workflowId:D}:{canvasKey}";
    internal static JsonObject GetOrCreateTriggerOptions(string canvasKey, Func<JsonObject>? create = null)
    {
        var key = TriggerOptionStorageKey(CurrentWorkflowId, canvasKey);
        if (!TriggerOptions.TryGetValue(key, out var options)) TriggerOptions[key] = options = create?.Invoke() ?? new JsonObject();
        return options;
    }

    internal static async Task<IReadOnlyList<(string Id, string Title, int Cost, int Cooldown)>> GetTwitchRewardsAsync(CancellationToken token = default)
    {
        var identity = TwitchSessionState.Identity ?? throw new InvalidOperationException("Twitch disconnected");
        var twitchToken = TwitchSessionState.Token ?? throw new InvalidOperationException("Twitch disconnected");
        return await new TwitchChannelClient(Http).GetCustomRewardsAsync(identity, twitchToken, token);
    }
    internal static async Task<string> CreateTwitchRewardAsync(string title, int cost, CancellationToken token = default)
    {
        var identity = TwitchSessionState.Identity ?? throw new InvalidOperationException("Twitch disconnected");
        var twitchToken = TwitchSessionState.Token ?? throw new InvalidOperationException("Twitch disconnected");
        return await new TwitchChannelClient(Http).CreateCustomRewardAsync(identity, twitchToken, title, cost, token);
    }
    internal static async Task UpdateTwitchRewardAsync(string id, string title, int cost, int cooldown, CancellationToken token = default)
    {
        var identity = TwitchSessionState.Identity ?? throw new InvalidOperationException("Twitch disconnected");
        var twitchToken = TwitchSessionState.Token ?? throw new InvalidOperationException("Twitch disconnected");
        await new TwitchChannelClient(Http).UpdateCustomRewardAsync(identity, twitchToken, id, title, cost, cooldown, token);
    }
    internal static async Task<System.Text.Json.Nodes.JsonObject> GetRewardCooldownsAsync()
    {
        await EnsureInitializedAsync(default);
        var json = await new SqliteApplicationSettingsStore(Database).GetAsync("twitch.rewardCooldowns");
        return json is null ? new JsonObject() : JsonNode.Parse(json)!.AsObject();
    }
    internal static async Task SaveRewardCooldownAsync(string id, int seconds, string scope)
    {
        var settings = await GetRewardCooldownsAsync();
        settings[id] = new JsonObject { ["seconds"] = seconds, ["scope"] = scope };
        await new SqliteApplicationSettingsStore(Database).SetAsync("twitch.rewardCooldowns", settings.ToJsonString());
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> RewardViewerCooldowns = new();
    internal static async Task DeleteTwitchRewardAsync(string id, CancellationToken token = default)
    {
        var identity = TwitchSessionState.Identity ?? throw new InvalidOperationException("Twitch disconnected");
        var twitchToken = TwitchSessionState.Token ?? throw new InvalidOperationException("Twitch disconnected");
        await new TwitchChannelClient(Http).DeleteCustomRewardAsync(identity, twitchToken, id, token);
    }

    internal static async Task ProcessTimersAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref processingTimers, 1) != 0) return;
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var repository = new SqliteWorkflowRepository(Database);
            var workflows = await repository.GetEnabledPublishedAsync(cancellationToken);
            var activeKeys = new HashSet<(Guid Workflow, Guid Trigger)>();
            var now = DateTimeOffset.UtcNow;
            var due = new List<(WorkflowDefinition Workflow, WorkflowBlock Trigger)>();
            foreach (var workflow in workflows)
            foreach (var trigger in workflow.Blocks.Where(block => block.Type == "core.timer"))
            {
                if (!double.TryParse(trigger.Parameters["seconds"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)) continue;
                seconds = Math.Clamp(seconds, 1, 86400);
                var key = (workflow.Id, trigger.Id);
                activeKeys.Add(key);
                if (!TimerSchedules.TryGetValue(key, out var schedule) || schedule.Revision != workflow.Revision || Math.Abs(schedule.Seconds - seconds) > 0.001)
                {
                    TimerSchedules[key] = new TimerSchedule(workflow.Revision, seconds, now.AddSeconds(seconds));
                    continue;
                }
                if (now < schedule.NextRun) continue;
                TimerSchedules[key] = schedule with { NextRun = now.AddSeconds(seconds) };
                due.Add((workflow, trigger));
            }
            foreach (var stale in TimerSchedules.Keys.Where(key => !activeKeys.Contains(key)).ToArray()) TimerSchedules.Remove(stale);
            await Task.WhenAll(due.Select(async entry =>
            {
                var workflow = entry.Workflow;
                var envelope = new EventEnvelope(Guid.NewGuid(), "core", "timer", Guid.NewGuid().ToString("N"), "core.timer", 1, now,
                    new JsonObject { ["workflowName"] = workflow.Name, ["interval"] = workflow.Blocks.First(block => block.Type == "core.timer").Parameters["seconds"]?.DeepClone() });
                var request = new ExecutionRequest(Guid.NewGuid(), envelope.Id, workflow.Id, workflow.Revision, now);

                try
                {
                    if (await new SqliteEventAdmissionStore(Database).TryAdmitAsync(envelope, [request], cancellationToken) == Automation.Contracts.Persistence.EventAdmissionResult.Admitted)
                        await DrainQueueAsync(repository, [request.Id], cancellationToken, eventTrigger: "core.timer");
                }
                finally { }
            }));
        }
        finally { Volatile.Write(ref processingTimers, 0); }
    }

    internal static async Task<IReadOnlyList<Automation.Contracts.Persistence.PersistentVariable>> GetVariablesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await new SqlitePersistentVariableStore(Database).ListAsync(cancellationToken);
    }

    internal static async Task SetVariableAsync(string name, long value, string scope = "system", string lifetime = "permanent", string? viewerId = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await new SqlitePersistentVariableStore(Database).SetIntegerAsync(name, value, scope, lifetime, viewerId ?? CurrentViewerId.Value, cancellationToken);
    }

    internal static async Task SetTextVariableAsync(string name, string value, string scope = "system", string lifetime = "permanent", string? viewerId = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await new SqlitePersistentVariableStore(Database).SetTextAsync(name, value, scope, lifetime, viewerId ?? CurrentViewerId.Value, cancellationToken);
    }

    internal static async Task DeleteVariableAsync(string name, string scope = "system", string? viewerId = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await new SqlitePersistentVariableStore(Database).DeleteAsync(name, scope, viewerId, cancellationToken);
    }

    internal static async Task DeleteVariablesForAllViewersAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await new SqlitePersistentVariableStore(Database).DeleteForAllViewersAsync(name, cancellationToken);
    }

    internal static async Task<string> ResolveVariablesAsync(string text, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var store = new SqlitePersistentVariableStore(Database);
        return await ReplaceVariableTokensAsync(text, name => store.GetValueAsync(name, "viewer", CurrentViewerId.Value, cancellationToken).ContinueWith(async viewer => await viewer ?? await store.GetValueAsync(name, cancellationToken), cancellationToken).Unwrap());
    }

    internal static async Task<string> ReplaceVariableTokensAsync(string text, Func<string, Task<string?>> lookup)
    {
        var matches = VariableToken.Matches(text);
        if (matches.Count == 0) return text;
        var result = text;
        foreach (var name in matches.Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = await lookup(name)
                ?? throw new InvalidOperationException(UiLanguageState.IsEnglish ? $"Variable not found: {name}" : $"Variable introuvable : {name}");
            result = Regex.Replace(result, $"%{Regex.Escape(name)}%", _ => value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return result;
    }

    internal static async Task ProcessNotificationAsync(TwitchNotification notification, CancellationToken cancellationToken = default)
    {
        var trigger = TwitchTriggerCatalog.All.FirstOrDefault(item => item.EventType == notification.EventType);
        if (trigger is null) return;
        await EnsureInitializedAsync(cancellationToken);
        var payload = (JsonObject)notification.Data.DeepClone();
        payload["userName"] = payload["user_name"]?.ToString() ?? payload["chatter_user_name"]?.ToString() ?? payload["from_broadcaster_user_name"]?.ToString() ?? payload["broadcaster_user_name"]?.ToString() ?? trigger.English;
        payload["userLogin"] = payload["user_login"]?.ToString() ?? payload["chatter_user_login"]?.ToString() ?? payload["chatter_user_name"]?.ToString() ?? payload["from_broadcaster_user_login"]?.ToString() ?? "";
        payload["userId"] = payload["user_id"]?.ToString() ?? payload["chatter_user_id"]?.ToString() ?? payload["from_broadcaster_user_id"]?.ToString() ?? "";
        var envelope = new EventEnvelope(Guid.NewGuid(), "twitch", "twitch-principal", notification.MessageId, notification.EventType, notification.Version, DateTimeOffset.UtcNow, payload);

        try
        {
            var repository = new SqliteWorkflowRepository(Database);
            var chatText = payload["message"]?.ToString();
            if (notification.EventType == "channel.channel_points_custom_reward_redemption.add")
            {
                var rewardId = payload["reward"]?["id"]?.ToString();
                var settings = await GetRewardCooldownsAsync();
                if (rewardId is not null && settings[rewardId] is JsonObject config && config["scope"]?.ToString() == "viewer")
                {
                    var viewerId = payload["userId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(viewerId)) return;
                    var key = rewardId + ":" + viewerId;
                    var now = DateTimeOffset.UtcNow;
                    if (RewardViewerCooldowns.TryGetValue(key, out var until) && until > now) return;
                    var delay = config["seconds"]?.GetValue<int>() ?? 0;
                    if (delay > 0) RewardViewerCooldowns[key] = now.AddSeconds(delay);
                }
            }
            if (payload["message"] is JsonObject messageObject) chatText = messageObject["text"]?.ToString();
            chatText ??= payload["message_text"]?.ToString();
            var commandText = chatText?.Split(' ', 2)[0];
            var savedCommands = notification.EventType == "channel.chat.message" ? await ChatCommands.ListAsync() : [];
            bool MatchesCommand(WorkflowDefinition workflow, WorkflowBlock block)
            {
                if (block.Parameters["commandId"] is null) return CommandAllowed(workflow.Id, block, payload, commandText);
                var configuration = savedCommands.FirstOrDefault(item => item["id"]?.ToString() == block.Parameters["commandId"]?.ToString());
                return configuration is not null && CommandAllowed(workflow.Id, block with { Parameters = configuration }, payload, commandText);
            }
            var workflows = (await repository.GetEnabledPublishedAsync(cancellationToken)).Where(workflow => workflow.Blocks.Any(block =>
                block.Type == trigger.BlockType && (block.Type switch
                {
                    "twitch.channel.chat.message.received" => MatchesCommand(workflow, block),
                    "twitch.channel.channel_points_custom_reward_redemption.add.received" => string.IsNullOrWhiteSpace(block.Parameters["reward"]?.ToString()) || string.Equals(block.Parameters["reward"]?.ToString(), payload["reward"]?["title"]?.ToString(), StringComparison.OrdinalIgnoreCase),
                    _ => true
                }))).ToArray();
            var requests = workflows.Select(workflow => new ExecutionRequest(Guid.NewGuid(), envelope.Id, workflow.Id, workflow.Revision, DateTimeOffset.UtcNow)).ToArray();
            if (await new SqliteEventAdmissionStore(Database).TryAdmitAsync(envelope, requests, cancellationToken) == Automation.Contracts.Persistence.EventAdmissionResult.Admitted)
            {
                CurrentViewerLogin.Value = payload["userLogin"]?.ToString();
                CurrentViewerId.Value = CurrentViewerLogin.Value ?? payload["userId"]?.ToString();
                try { await DrainQueueAsync(repository, requests.Select(request => request.Id), cancellationToken, eventTrigger: trigger.BlockType); }
                finally { CurrentViewerId.Value = null; CurrentViewerLogin.Value = null; }
            }
        }
        finally { }
    }

    private static bool CommandAllowed(Guid workflowId, WorkflowBlock block, JsonObject payload, string? commandText)
    {
        if (!string.Equals(block.Parameters["command"]?.ToString()?.Trim(), commandText, StringComparison.OrdinalIgnoreCase)) return false;
        var badges = payload["badges"] as JsonArray;
        bool HasBadge(string name) => badges?.Any(item => item is JsonObject badge && string.Equals(badge["set_id"]?.ToString(), name, StringComparison.OrdinalIgnoreCase)) == true;
        var isModerator = HasBadge("moderator") || HasBadge("lead_moderator") || HasBadge("broadcaster") || payload["is_mod"]?.GetValue<bool>() == true || !string.IsNullOrWhiteSpace(payload["moderator_user_id"]?.ToString());
        var isVip = HasBadge("vip") || isModerator;
        var mode = block.Parameters["permission"]?.ToString() ?? "everyone";
        var allowedUser = block.Parameters["allowedUser"]?.ToString()?.Trim();
        if (mode == "viewer" && string.IsNullOrWhiteSpace(allowedUser)) return false;
        if (mode == "viewer" && !string.IsNullOrWhiteSpace(allowedUser))
        {
            var login = payload["chatter_user_login"]?.ToString() ?? payload["user_login"]?.ToString() ?? payload["userLogin"]?.ToString();
            if (!string.Equals(login, allowedUser.TrimStart('@'), StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (mode == "moderator" && !isModerator) return false;
        if (mode == "vip" && !isVip) return false;
        if (!double.TryParse(block.Parameters["cooldownSeconds"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds <= 0) return true;
        var cooldownScope = block.Parameters["cooldownScope"]?.ToString() ?? "global";
        var viewerKey = payload["chatter_user_id"]?.ToString() ?? payload["user_id"]?.ToString() ?? "unknown";
        var key = cooldownScope == "viewer"
            ? $"{workflowId:D}:{block.Id:D}:{commandText}:{viewerKey}"
            : $"{workflowId:D}:{block.Id:D}:{commandText}";
        var now = DateTimeOffset.UtcNow;
        if (CommandCooldowns.TryGetValue(key, out var until) && until > now) return false;
        CommandCooldowns[key] = now.AddSeconds(seconds);
        return true;
    }

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


        try
        {
            var repository = new SqliteWorkflowRepository(Database);
            var workflows = (await repository.GetEnabledPublishedAsync(cancellationToken))
                .Where(workflow => workflow.Blocks.Any(block => block.Type == "twitch.follow.received")).ToArray();
            var requests = workflows.Select(workflow => new ExecutionRequest(Guid.NewGuid(), eventEnvelope.Id, workflow.Id, workflow.Revision, DateTimeOffset.UtcNow)).ToArray();
            var admission = await new SqliteEventAdmissionStore(Database).TryAdmitAsync(eventEnvelope, requests, cancellationToken);
            ExecutionStatus? status = null;
            if (admission == Automation.Contracts.Persistence.EventAdmissionResult.Admitted)
            {
                CurrentViewerLogin.Value = follow.UserLogin;
                CurrentViewerId.Value = follow.UserLogin ?? follow.UserId;
                try { status = await DrainQueueAsync(repository, requests.Select(request => request.Id), cancellationToken); }
                finally { CurrentViewerId.Value = null; CurrentViewerLogin.Value = null; }
            }
            return new FollowerProcessingResult(admission, status, await new SqlitePersistentVariableStore(Database).GetIntegerAsync("followersTraites", cancellationToken) ?? 0);
        }
        finally { }
    }

    internal static async Task<ExecutionStatus?> RunPublishedOnceAsync(CancellationToken token = default)
    {
        var workflowId = CurrentWorkflowId;
        await EnsureInitializedAsync(token);
        var repository = new SqliteWorkflowRepository(Database);
        if (!(await repository.GetEnabledPublishedAsync(token)).Any(candidate => candidate.Id == workflowId))
            throw new InvalidOperationException(UiLanguageState.IsEnglish ? "Activate this automation before running it." : "Active cette automatisation avant de l’exécuter.");
        var workflow = await repository.GetPublishedAsync(workflowId, token)
            ?? throw new InvalidOperationException(UiLanguageState.IsEnglish ? "Publish this workflow first." : "Publie d’abord cette automatisation.");
        var manualTriggers = workflow.Blocks.Where(block => block.Type == "manual.run").Select(block => block.Id).ToHashSet();
        if (!workflow.Connections.Any(edge => manualTriggers.Contains(edge.FromBlockId)))
            throw new InvalidOperationException(UiLanguageState.IsEnglish ? "Connect Manual run to an action, then save and publish the workflow." : "Relie Exécution manuelle à une action, puis enregistre et publie le workflow.");
        var cyclesAllowed = workflow.Blocks.Any(block => block.Parameters["allowCycles"]?.GetValue<bool>() == true);
        EnsureValid(workflow, cyclesAllowed);
        var errors = ExecutableWorkflowValidator.Validate(workflow, UiLanguageState.IsEnglish);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" · ", errors.Select(error => error.Message)));
        var envelope = new EventEnvelope(Guid.NewGuid(), "manual", "local", Guid.NewGuid().ToString("N"), "manual.run", 1, DateTimeOffset.UtcNow,
            new JsonObject { ["userName"] = workflow.Name, ["workflowName"] = workflow.Name });

        try
        {
            var request = new ExecutionRequest(Guid.NewGuid(), envelope.Id, workflow.Id, workflow.Revision, DateTimeOffset.UtcNow);
            await new SqliteEventAdmissionStore(Database).TryAdmitAsync(envelope, [request], token);
            return await DrainQueueAsync(repository, [request.Id], token, workflowId);
        }
        finally { }
    }

    private static async Task<ExecutionStatus?> DrainQueueAsync(SqliteWorkflowRepository repository, IEnumerable<Guid> requestIds, CancellationToken token, Guid? manualWorkflow = null, string eventTrigger = "twitch.follow.received")
    {
        try
        {
            var results = await Task.WhenAll(requestIds.Select(id => DrainSingleQueueAsync(repository, [id], token, manualWorkflow, eventTrigger)));
            return results.FirstOrDefault(result => result is ExecutionStatus.Failed or ExecutionStatus.UnknownOutcome or ExecutionStatus.Cancelled) ?? results.FirstOrDefault();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Workflow execution failed safely: {0}", exception);
            return ExecutionStatus.Failed;
        }
    }

    private static async Task<ExecutionStatus?> DrainSingleQueueAsync(SqliteWorkflowRepository repository, IEnumerable<Guid> requestIds, CancellationToken token, Guid? manualWorkflow = null, string eventTrigger = "twitch.follow.received")
    {
        ExecutionStatus? result = null;
        Guid runningId = Guid.Empty;
        AlertExecutionQueue? selectedQueue = null;
        CancellationTokenSource? executionCancellation = null;
        var runner = new WorkflowExecutionRunner(new SqliteExecutionStore(Database), async (id, revision, ct) =>
        {
            if (!(await repository.GetEnabledPublishedAsync(ct)).Any(workflow => workflow.Id == id))
                throw new OperationCanceledException(UiLanguageState.IsEnglish ? "Automation disabled before execution." : "Automatisation désactivée avant l’exécution.");
            var queue = await AutomationQueues.GetConfiguredAsync(await AutomationQueues.ForWorkflowAsync(id));
            await queue.WaitAsync(ct);
            selectedQueue = queue;
            if (!(await repository.GetEnabledPublishedAsync(ct)).Any(workflow => workflow.Id == id)) throw new OperationCanceledException("Automation disabled");
            runningId = id;
            executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Running[id] = executionCancellation;
            var definition = await repository.GetRevisionAsync(id, revision, ct);
            if (definition is null) return null;
            // Only the matching trigger may activate its outgoing branch.
            var selectedTrigger = manualWorkflow is not null ? "manual.run" : eventTrigger;
            return WorkflowEventRouter.Select(definition, selectedTrigger);
        }, (block, ct) => manualWorkflow is not null && block.Type == "twitch.follow.received"
            ? Task.FromResult("Manual run") : ExecuteBlockAsync(block, executionCancellation?.Token ?? ct),
        async (block, ct) =>
        {
            if (block.Type != "core.condition") return null;
            var name = block.Parameters["name"]!.GetValue<string>();
            var actualValue = await new SqlitePersistentVariableStore(Database).GetValueAsync(name, block.Parameters["scope"]?.ToString() ?? "system", CurrentViewerId.Value, executionCancellation?.Token ?? ct)
                ?? throw new InvalidOperationException($"Variable not found: {name}");
            if ((block.Parameters["comparison"]?.ToString() ?? "equals") == "equals")
                return string.Equals(actualValue, block.Parameters["value"]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
            var actual = long.Parse(actualValue, System.Globalization.CultureInfo.InvariantCulture);
            var expected = long.Parse(block.Parameters["value"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            var result = block.Parameters["comparison"]?.ToString() switch
            {
                "greater" => actual > expected,
                "greaterOrEqual" => actual >= expected,
                "less" => actual < expected,
                "lessOrEqual" => actual <= expected,
                _ => actual == expected
            };
            return result ? "yes" : "no";
        });
        foreach (var requestId in requestIds)
        {
            ExecutionStatus? next;
            try { next = await runner.RunNextAsync(token, requestId); }
            finally
            {
                selectedQueue?.Release(); selectedQueue = null;
                if (runningId != Guid.Empty) Running.TryRemove(runningId, out _);
                executionCancellation?.Dispose(); executionCancellation = null;
            }
            if (next is null) continue;
            static int Severity(ExecutionStatus status) => status switch
            {
                ExecutionStatus.UnknownOutcome => 4, ExecutionStatus.Failed => 3, ExecutionStatus.Cancelled => 2, _ => 1
            };
            if (result is null || Severity(next.Value) > Severity(result.Value)) result = next;
        }
        return result;
    }

    private static async Task<string> ExecuteBlockAsync(WorkflowBlock block, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (block.Type == "twitch.follow.received") return "Follower event received";
        if (TwitchTriggerCatalog.FindBlock(block.Type) is { } trigger) return "Event received: " + trigger.English;
        if (block.Type == "manual.run") return "Manual run";
        if (block.Type == "core.timer") return "Timer elapsed";
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
                var resolvedScene = await ResolveVariablesAsync(Required("scene"), token);
                await obs.SetCurrentProgramSceneAsync("obs-principal", resolvedScene, token);
                return $"OBS scene: {resolvedScene}";
            case "obs.replay.start":
                if (obs?.IsConnected != true) throw new InvalidOperationException("OBS disconnected");
                await obs.StartReplayBufferAsync(token);
                return "OBS replay buffer started";
            case "obs.output.record":
            case "obs.output.replay":
            case "obs.output.virtual-camera":
                if (obs?.IsConnected != true) throw new InvalidOperationException("OBS is disconnected.");
                var output = block.Type["obs.output.".Length..];
                var operation = block.Parameters["operation"]?.ToString() ?? "start";
                await obs.ControlOutputAsync(output, operation, token);
                return $"OBS {output}: {operation}";
            case "obs.screenshot":
                if (obs?.IsConnected != true) throw new InvalidOperationException("OBS disconnected");
                var image = await obs.GetScreenshotAsync(Required("scene"), token);
                var directory = System.IO.Path.Combine(Paths.DataDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                var filename = System.IO.Path.Combine(directory, $"capture-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(filename, image, token);
                return "OBS screenshot: " + filename;
            case "twitch.update-title":
                var identity = TwitchSessionState.Identity ?? throw new InvalidOperationException("Twitch disconnected");
                var credentials = TwitchSessionState.Token ?? throw new InvalidOperationException("Twitch disconnected");
                await new TwitchChannelClient(Http).SetTitleAsync(identity, credentials, await ResolveVariablesAsync(Required("title"), token), token);
                return "Twitch title updated";
            case "twitch.send-message":
            case "twitch.create-clip":
            case "twitch.create-reward":
            case "twitch.get-user":
                var twitchIdentity = TwitchSessionState.Identity ?? throw new InvalidOperationException("Twitch disconnected");
                var twitchToken = TwitchSessionState.Token ?? throw new InvalidOperationException("Twitch disconnected");
                var twitch = new TwitchChannelClient(Http);
                if (block.Type == "twitch.send-message")
                {
                    await twitch.SendMessageAsync(twitchIdentity, twitchToken, await ResolveVariablesAsync(Required("message"), token), token);
                    return "Twitch message sent";
                }
                if (block.Type == "twitch.create-clip") return "Twitch clip: " + await twitch.CreateClipAsync(twitchIdentity, twitchToken, token);
                if (block.Type == "twitch.create-reward") return "Twitch reward: " + await twitch.CreateCustomRewardAsync(twitchIdentity, twitchToken, await ResolveVariablesAsync(Required("title"), token), int.Parse(Required("cost"), System.Globalization.CultureInfo.InvariantCulture), token);
                var requestedLogin = block.Parameters["login"]?.ToString()?.Trim();
                var userResult = !string.IsNullOrWhiteSpace(CurrentViewerLogin.Value)
                    ? CurrentViewerLogin.Value!
                    : !string.IsNullOrWhiteSpace(requestedLogin)
                        ? (await twitch.GetUserAsync(twitchIdentity, twitchToken, await ResolveVariablesAsync(requestedLogin, token), token)).Split(" · ID ", StringSplitOptions.None)[0]
                        : throw new InvalidOperationException("Cette action doit être déclenchée par un viewer Twitch.");
                if (!string.IsNullOrWhiteSpace(block.Parameters["outputVariable"]?.ToString()))
                {
                    var outputVariable = block.Parameters["outputVariable"]!.ToString();
                    await new SqlitePersistentVariableStore(Database).SetTextAsync(outputVariable, userResult, "system", "temporary", CurrentViewerId.Value, token);
                }
                return "Twitch user: " + userResult;
            case "core.delay":
                if (!double.TryParse(Required("seconds"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > 3600)
                    throw new InvalidOperationException("Delay must be between 0 and 3600 seconds");
                await Task.Delay(TimeSpan.FromSeconds(seconds), token);
                return $"Wait: {seconds} s";
            case "core.condition": return "Condition";
            case "audio.play":
                await Automation.Platform.Audio.LocalAudioPlayer.PlayAsync(Required("file"), token);
                return "Audio played";
            case "variables.set-integer":
            case "variables.increment":
                var name = Required("name");
                var value = long.Parse(Required("value"), System.Globalization.CultureInfo.InvariantCulture);
                var variables = new SqlitePersistentVariableStore(Database);
                var scope = block.Parameters["scope"]?.ToString() ?? "system";
                var lifetime = block.Parameters["lifetime"]?.ToString() ?? "permanent";
                var viewerId = CurrentViewerId.Value;
                if (scope == "viewer" && string.IsNullOrWhiteSpace(viewerId))
                    throw new InvalidOperationException(UiLanguageState.IsEnglish ? "This viewer variable requires a Twitch viewer." : "Cette variable Viewer nécessite un viewer Twitch.");
                if (block.Type == "variables.increment" && scope == "system") value = await variables.IncrementAsync(name, value, token);
                else await variables.SetIntegerAsync(name, value, scope, lifetime, viewerId, token);
                return $"Variable {name}: {value}";
            case "variables.set-text":
                var textName = Required("name");
                var textValue = block.Parameters["value"]?.ToString() ?? "";
                var textScope = block.Parameters["scope"]?.ToString() ?? "system";
                if (textScope == "viewer" && string.IsNullOrWhiteSpace(CurrentViewerId.Value))
                    throw new InvalidOperationException(UiLanguageState.IsEnglish ? "This viewer variable requires a Twitch viewer." : "Cette variable Viewer nécessite un viewer Twitch.");
                await new SqlitePersistentVariableStore(Database).SetTextAsync(textName, textValue, textScope, block.Parameters["lifetime"]?.ToString() ?? "permanent", CurrentViewerId.Value, token);
                return $"Variable {textName}: {textValue}";
            case "variables.number-expression":
                var expressionName = Required("name");
                var expression = Required("value");
                var expressionStore = new SqlitePersistentVariableStore(Database);
                var expressionScope = block.Parameters["scope"]?.ToString() ?? "system";
                var currentValue = await expressionStore.GetIntegerAsync(expressionName, expressionScope, CurrentViewerId.Value, token) ?? 0;
                if (!NumericVariableExpression.TryApply(expression, currentValue, out var expressionResult))
                    throw new InvalidOperationException(UiLanguageState.IsEnglish ? "Invalid number expression." : "Expression numérique invalide.");
                await expressionStore.SetIntegerAsync(expressionName, expressionResult, block.Parameters["scope"]?.ToString() ?? "system", block.Parameters["lifetime"]?.ToString() ?? "permanent", CurrentViewerId.Value, token);
                return $"Variable {expressionName}: {expressionResult}";
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
            block => (block.Parameters["label"] ?? block.Parameters["triggerKey"])?.GetValue<string>());
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

    internal static async Task PublishPrototypeAsync(IReadOnlyCollection<string>? selectedActions = null, IReadOnlyCollection<(string From, string To)>? manualLinks = null, bool allowCycles = false, CancellationToken cancellationToken = default)
    {
        var workflow = CreatePrototypeWorkflow(selectedActions, manualLinks);
        if (allowCycles)
            foreach (var trigger in workflow.Blocks.Where(block => IsTriggerType(block.Type)))
                trigger.Parameters["allowCycles"] = true;
        await EnsureInitializedAsync(cancellationToken);
        var repository = new SqliteWorkflowRepository(Database);
        var previous = await repository.GetPublishedAsync(workflow.Id, cancellationToken);
        var revision = (previous?.Revision ?? 0) + 1;
        workflow = workflow with { Enabled = true, Revision = revision };
        EnsureValid(workflow, allowCycles);
        var executionErrors = ExecutableWorkflowValidator.Validate(workflow, UiLanguageState.IsEnglish);
        if (executionErrors.Count > 0) throw new InvalidOperationException(string.Join(" · ", executionErrors.Select(error => error.Message)));
        foreach (var trigger in workflow.Blocks.Select(block => TwitchTriggerCatalog.FindBlock(block.Type)).OfType<TwitchTrigger>())
        {
            if (TwitchSessionState.EventSubClient?.IsConnected != true)
                throw new InvalidOperationException(UiLanguageState.IsEnglish ? "Connect Twitch before enabling this event." : "Connecte Twitch avant d’activer cet événement.");
            if (trigger.Scope.Length > 0 && TwitchSessionState.Identity?.Scopes.Contains(trigger.Scope) != true)
                throw new InvalidOperationException(UiLanguageState.IsEnglish ? $"Reconnect Twitch to grant {trigger.Scope}." : $"Reconnecte Twitch pour autoriser {trigger.Scope}.");
            if (TwitchSessionState.EventSubClient.SubscriptionErrors.Any(error => error.StartsWith(trigger.EventType + ":", StringComparison.Ordinal)))
                throw new InvalidOperationException(UiLanguageState.IsEnglish ? $"Twitch refused this event: {trigger.English}. Reconnect Twitch and check its permissions." : $"Twitch a refusé cet événement : {trigger.French}. Reconnecte Twitch et vérifie ses permissions.");
        }
        if (workflow.Blocks.Any(block => block.Type == "twitch.update-title") && TwitchSessionState.Identity?.Scopes.Contains("channel:manage:broadcast") != true)
            throw new InvalidOperationException(UiLanguageState.IsEnglish ? "Reconnect Twitch to grant permission to edit the title." : "Reconnecte Twitch pour autoriser la modification du titre.");
        foreach (var (type, scope) in new[] { ("twitch.send-message", "user:write:chat"), ("twitch.create-clip", "clips:edit"), ("twitch.create-reward", "channel:manage:redemptions"), ("twitch.channel.chat.message.received", "user:read:chat") })
            if (workflow.Blocks.Any(block => block.Type == type) && TwitchSessionState.Identity?.Scopes.Contains(scope) != true)
                throw new InvalidOperationException(UiLanguageState.IsEnglish ? $"Reconnect Twitch to grant {scope}." : $"Reconnecte Twitch pour autoriser {scope}.");
        await ValidateConnectionsAsync(workflow, cancellationToken);
        await repository.PublishAsync(workflow, cancellationToken);
    }

    private static async Task ValidateConnectionsAsync(WorkflowDefinition workflow, CancellationToken token)
    {
        string Text(string fr, string en) => UiLanguageState.IsEnglish ? en : fr;
        var obsBlocks = workflow.Blocks.Where(block => block.Type.StartsWith("obs.", StringComparison.Ordinal)).ToArray();
        if (obsBlocks.Length > 0)
        {
            var obs = ObsSessionState.Client;
            if (obs?.IsConnected != true) throw new InvalidOperationException(Text("Connecte OBS avant de publier.", "Connect OBS before publishing."));
            var scenes = await obs.GetSceneNamesAsync(token);
            foreach (var block in obsBlocks)
            {
                if (block.Type == "obs.replay.start" || block.Type.StartsWith("obs.output.", StringComparison.Ordinal)) continue;
                var scene = block.Parameters["scene"]!.ToString();
                if (!scenes.Contains(scene)) throw new InvalidOperationException(Text($"Scène OBS introuvable : {scene}", $"OBS scene not found: {scene}"));
                if (block.Type == "obs.set-source-visible")
                {
                    var available = await obs.GetSourceNamesAsync(scene, token);
                    var selected = block.Parameters["sources"]?.AsArray().Select(value => value!.GetValue<string>())
                        ?? new[] { block.Parameters["source"]!.ToString() };
                    var missing = selected.Where(name => !available.Contains(name)).ToArray();
                    if (missing.Length > 0) throw new InvalidOperationException(Text("Sources OBS introuvables : ", "OBS sources not found: ") + string.Join(", ", missing));
                }
            }
        }
        if (workflow.Blocks.Any(block => block.Type.StartsWith("twitch.", StringComparison.Ordinal) && block.Type != "twitch.follow.received")
            && (TwitchSessionState.Identity is null || TwitchSessionState.Token is null))
            throw new InvalidOperationException(Text("Connecte Twitch avant de publier.", "Connect Twitch before publishing."));
    }

    private static WorkflowDefinition CreatePrototypeWorkflow(IReadOnlyCollection<string>? selectedActions, IReadOnlyCollection<(string From, string To)>? manualLinks)
    {
        var triggerTypes = CurrentTriggerTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var blocks = triggerTypes.Select((type, index) =>
        {
            var key = index == 0 ? "__trigger__" : $"__trigger__:{type}";
            var storageKey = TriggerOptionStorageKey(CurrentWorkflowId, key);
            var parameters = TriggerOptions.TryGetValue(storageKey, out var options) ? (JsonObject)options.DeepClone() : new JsonObject();
            if (type == "core.timer" && parameters["seconds"] is null) parameters["seconds"] = "60";
            return new WorkflowBlock(index == 0 ? PrototypeWorkflowIds.TriggerBlockId : Guid.NewGuid(), type, 1, parameters);
        }).ToList();
        var connections = new List<WorkflowConnection>
        {
        };
        var actionIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            var key = block == blocks[0] ? "__trigger__" : $"__trigger__:{block.Type}";
            block.Parameters["triggerKey"] = key;
            actionIds[key] = block.Id;
        }
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
            if (type == "variables.set-integer")
                type = parameters["valueType"]?.ToString() == "text" ? "variables.set-text"
                    : "variables.number-expression";
            blocks.Add(new WorkflowBlock(id, type, 1, parameters));
        }

        foreach (var (from, to) in manualLinks ?? Array.Empty<(string From, string To)>())
        {
            if (actionIds.TryGetValue(from, out var fromId) && actionIds.TryGetValue(to, out var toId) &&
                !connections.Any(connection => connection.FromBlockId == fromId && connection.ToBlockId == toId))
            {
                var type = blocks.Single(block => block.Id == fromId).Type;
                var output = type == "core.condition"
                    ? ActionOptions.GetValueOrDefault(from)?["branches"]?[to]?.ToString() ?? "yes" : "next";
                connections.Add(new WorkflowConnection(fromId, output, toId));
            }
        }

        // The follower entry point feeds roots only. User cards are connected
        // exclusively by the manual links, never by their insertion order.
        // Trigger links are explicit, like every other canvas connection.

        return new WorkflowDefinition(
            CurrentWorkflowId,
            1,
            CurrentWorkflowName,
            false,
            blocks,
            connections);
    }

    internal static string ActionTypeFor(string label) => label.ToLowerInvariant() switch
    {
        var value when value.Contains("enregistrement obs") || value.Contains("obs recording") => "obs.output.record",
        var value when value.Contains("tampon replay") || value.Contains("replay buffer") => "obs.output.replay",
        var value when value.Contains("caméra virtuelle") || value.Contains("virtual camera") => "obs.output.virtual-camera",
        var value when value.Contains("clip") => "twitch.create-clip",
        var value when value.Contains("titre") || value.Contains("title") => "twitch.update-title",
        var value when value.Contains("message") => "twitch.send-message",
        var value when value.Contains("récompense") || value.Contains("reward") => "twitch.create-reward",
        var value when value.Contains("utilisateur") || value.Contains("user") => "twitch.get-user",
        var value when value.Contains("capture") || value.Contains("screenshot") => "obs.screenshot",
        var value when value.Contains("source") => "obs.set-source-visible",
        var value when value.Contains("scène") || value.Contains("scene") => "obs.scene.set-current",
        var value when value.Contains("replay") => "obs.replay.start",
        var value when value.Contains("attendre") || value.Contains("wait") => "core.delay",
        var value when value.Contains("condition") => "core.condition",
        var value when value.Contains("sound") || value.Contains("son") => "audio.play",
        var value when value.Contains("variable") => "variables.set-integer",
        _ => "core.action"
    };

    private static void EnsureValid(WorkflowDefinition workflow, bool allowCycles = false)
    {
        var result = new WorkflowValidator(new InitialBlockCatalog()).Validate(workflow);
        var blocking = result.Diagnostics.Where(diagnostic => !allowCycles || diagnostic.Code != "workflow.cycle").ToArray();
        if (blocking.Length > 0)
        {
            throw new InvalidOperationException(string.Join(" ", blocking.Select(diagnostic => diagnostic.Message)));
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
                await new SqlitePersistentVariableStore(Database).DeleteTemporaryAsync(cancellationToken);
                await new SqliteExecutionStore(Database).RecoverAbandonedAsync(cancellationToken);
                initialized = true;
            }
        }
        finally
        {
            InitializationGate.Release();
        }
    }
}
