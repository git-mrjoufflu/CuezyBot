using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Automation.Contracts.Persistence;
using Automation.Domain.Execution;
using Automation.Domain.Workflows;
using Automation.Engine.Execution;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;
using Microsoft.Data.Sqlite;

var minutes = args.Length > 0 ? double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 1;
if (!double.IsFinite(minutes) || minutes <= 0 || minutes > 120) throw new ArgumentOutOfRangeException(nameof(minutes));
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
var token = cancellation.Token;
var directory = Path.Combine(Path.GetTempPath(), $"cuezybot-soak-{Guid.NewGuid():N}");
var paths = new AppDataPaths(directory);
RuntimeDataLease? lease = null;
var started = Stopwatch.StartNew();
var completed = 0;
var revision = 0;
var requests = 0;
var duplicateEvents = 0;
var fakeObsCalls = 0;
var fakeTwitchCalls = 0;
var peakMemory = 0L;
try
{
    lease = RuntimeDataLease.Acquire(paths);
    var database = new SqliteDatabase(paths); await database.InitializeAsync(token);
    var repository = new SqliteWorkflowRepository(database);
    var admissions = new SqliteEventAdmissionStore(database);
    var executions = new SqliteExecutionStore(database);
    var variables = new SqlitePersistentVariableStore(database);
    var triggerId = Guid.NewGuid(); var conditionId = Guid.NewGuid(); var obsId = Guid.NewGuid(); var twitchId = Guid.NewGuid(); var counterId = Guid.NewGuid();
    var workflowId = Guid.NewGuid();
    var nextReport = TimeSpan.FromSeconds(30);
    while (started.Elapsed < TimeSpan.FromMinutes(minutes))
    {
        token.ThrowIfCancellationRequested();
        if (completed % 100 == 0)
        {
            revision++;
            var workflow = new WorkflowDefinition(workflowId, revision, "Soak workflow", true,
                [new(triggerId, "manual.run", 1, new JsonObject()), new(conditionId, "core.condition", 1, new JsonObject()),
                 new(obsId, "obs.fake", 1, new JsonObject { ["revision"] = revision }),
                 new(twitchId, "twitch.fake", 1, new JsonObject { ["revision"] = revision }),
                 new(counterId, "variables.increment", 1, new JsonObject())],
                [new(triggerId, "next", conditionId), new(conditionId, "yes", obsId), new(conditionId, "no", twitchId),
                 new(obsId, "next", counterId), new(twitchId, "next", counterId)]);
            await repository.PublishAsync(workflow, token);
            await repository.SaveDraftAsync(workflow with { Name = "Different draft", Enabled = false }, token);
            if ((await repository.GetEnabledPublishedAsync(token)).Count != 1) throw new InvalidOperationException("Draft save disabled published workflow.");
        }
        var envelope = new EventEnvelope(Guid.NewGuid(), "fake", "isolated-soak", $"soak-{completed}", "manual.run", 1, DateTimeOffset.UtcNow, new JsonObject());
        var request = new ExecutionRequest(Guid.NewGuid(), envelope.Id, workflowId, revision, DateTimeOffset.UtcNow);
        if (await admissions.TryAdmitAsync(envelope, [request], token) != EventAdmissionResult.Admitted) throw new InvalidOperationException("Unexpected duplicate.");
        requests++;
        var expectedRevision = revision;
        var chooseYes = completed % 2 == 0;
        var runner = new WorkflowExecutionRunner(executions, repository.GetRevisionAsync, async (block, ct) =>
        {
            if (block.Type is "obs.fake" or "twitch.fake")
            {
                if (block.Parameters["revision"]!.GetValue<int>() != expectedRevision) throw new InvalidOperationException("Wrong published revision.");
                if (block.Type == "obs.fake") fakeObsCalls++; else fakeTwitchCalls++;
            }
            if (block.Type == "variables.increment") await variables.IncrementAsync("soak-counter", 1, ct);
            return block.Type;
        }, (block, _) => Task.FromResult<string?>(block.Id == conditionId ? (chooseYes ? "yes" : "no") : null));
        if (await runner.RunNextAsync(token, request.Id) != ExecutionStatus.Succeeded) throw new InvalidOperationException("Soak execution failed.");
        if (await admissions.TryAdmitAsync(envelope, [request], token) != EventAdmissionResult.Duplicate) throw new InvalidOperationException("Deduplication failed.");
        duplicateEvents++;
        if (await runner.RunNextAsync(token, request.Id) is not null) throw new InvalidOperationException("Completed request replayed.");
        completed++;
        if (await variables.GetIntegerAsync("soak-counter", token) != completed) throw new InvalidOperationException("Counter diverged.");
        if (fakeObsCalls + fakeTwitchCalls != completed) throw new InvalidOperationException("Wrong branch execution count.");
        peakMemory = Math.Max(peakMemory, Process.GetCurrentProcess().WorkingSet64);
        if (started.Elapsed >= nextReport)
        {
            Console.WriteLine($"SOAK {started.Elapsed:mm\\:ss}: executions={completed}, duplicates={duplicateEvents}, revisions={revision}, memoryMiB={peakMemory / 1048576}");
            nextReport += TimeSpan.FromSeconds(30);
        }
        await Task.Delay(100, token);
    }
    var history = await new SqliteExecutionHistoryReader(database).GetRecentAsync(Math.Min(50, completed), token);
    if (history.Any(entry => entry.Status != ExecutionStatus.Succeeded || entry.Steps.Count != 4)) throw new InvalidOperationException("History inconsistent.");
    var result = new { status = "passed", durationSeconds = started.Elapsed.TotalSeconds, completed, requests, duplicateEvents, revision, fakeObsCalls, fakeTwitchCalls, peakMemoryMiB = peakMemory / 1048576 };
    Console.WriteLine("SOAK PASSED " + JsonSerializer.Serialize(result));
    if (args.Length > 1) await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), token);
}
finally
{
    lease?.Dispose();
    if (File.Exists(paths.DatabasePath))
        using (var connection = new SqliteDatabase(paths).OpenConnection()) SqliteConnection.ClearPool(connection);
    if (Directory.Exists(directory)) Directory.Delete(directory, true);
}
