using System.Text.Json.Nodes;
using Automation.Domain.Execution;
using Automation.Domain.Workflows;
using Automation.Engine.Execution;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Tests;

public sealed class PublishedWorkflowExecutionTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"published-execution-{Guid.NewGuid():N}");
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() { TestDataCleanup.Delete(directory); return ValueTask.CompletedTask; }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Runner_UsesRequestedRevisionAndRecordsConnectorResult(bool fail, bool timeout)
    {
        var token = TestContext.Current.CancellationToken;
        var database = new SqliteDatabase(new AppDataPaths(directory));
        await database.InitializeAsync(token);
        var repository = new SqliteWorkflowRepository(database);
        var trigger = new WorkflowBlock(Guid.NewGuid(), "twitch.follow.received", 1, new JsonObject());
        var action = new WorkflowBlock(Guid.NewGuid(), "obs.set-source-visible", 1, new JsonObject { ["scene"] = "Original", ["sources"] = new JsonArray("Camera", "Logo"), ["visible"] = false });
        var original = new WorkflowDefinition(Guid.NewGuid(), 1, "Original", true, [trigger, action], [new(trigger.Id, "next", action.Id)]);
        await repository.PublishAsync(original, token);
        await repository.PublishAsync(original with { Revision = 2, Blocks = [trigger, action with { Parameters = new JsonObject { ["scene"] = "Changed" } }] }, token);
        var envelope = new EventEnvelope(Guid.NewGuid(), "twitch", "test", "message", "channel.follow", 2, DateTimeOffset.UtcNow, new JsonObject { ["userName"] = "Test" });
        var request = new ExecutionRequest(Guid.NewGuid(), envelope.Id, original.Id, 1, DateTimeOffset.UtcNow);
        var admission = new SqliteEventAdmissionStore(database);
        await admission.TryAdmitAsync(envelope, [request], token);
        var calls = new List<string>();
        var runner = new WorkflowExecutionRunner(new SqliteExecutionStore(database), repository.GetRevisionAsync, (block, _) =>
        {
            if (block.Type != "twitch.follow.received")
            {
                calls.Add(block.Parameters["scene"]!.GetValue<string>());
                if (timeout) throw new TimeoutException("OBS response lost after request");
                if (fail) throw new IOException("Simulated OBS failure");
            }
            return Task.FromResult("Executed");
        });
        var expected = timeout ? ExecutionStatus.UnknownOutcome : fail ? ExecutionStatus.Failed : ExecutionStatus.Succeeded;
        Assert.Equal(expected, await runner.RunNextAsync(token));
        Assert.Equal(new[] { "Original" }, calls);
        Assert.Null(await runner.RunNextAsync(token));
        var history = Assert.Single(await new SqliteExecutionHistoryReader(database).GetRecentAsync(50, token));
        Assert.Equal(2, history.Steps.Count);
        Assert.Equal(expected, history.Steps.Last().Status);
    }

    [Fact]
    public async Task CancellationDuringDelay_IsRecordedAndStopsDownstream()
    {
        var token = TestContext.Current.CancellationToken;
        var database = new SqliteDatabase(new AppDataPaths(directory)); await database.InitializeAsync(token);
        var repository = new SqliteWorkflowRepository(database);
        var first = new WorkflowBlock(Guid.NewGuid(), "core.delay", 1, new JsonObject());
        var last = new WorkflowBlock(Guid.NewGuid(), "obs.set-source-visible", 1, new JsonObject());
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Cancel", true, [first, last], [new(first.Id, "next", last.Id)]);
        await repository.PublishAsync(workflow, token);
        var envelope = new EventEnvelope(Guid.NewGuid(), "test", "test", "cancel", "manual.run", 1, DateTimeOffset.UtcNow, new JsonObject());
        var request = new ExecutionRequest(Guid.NewGuid(), envelope.Id, workflow.Id, 1, DateTimeOffset.UtcNow);
        await new SqliteEventAdmissionStore(database).TryAdmitAsync(envelope, [request], token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var runner = new WorkflowExecutionRunner(new SqliteExecutionStore(database), repository.GetRevisionAsync, async (_, ct) =>
        {
            calls++; entered.TrySetResult();
            await Task.Delay(TimeSpan.FromHours(1), ct);
            return "Unexpected completion";
        });
        var running = runner.RunNextAsync(cancel.Token, request.Id);
        await entered.Task.WaitAsync(token);
        cancel.Cancel();
        Assert.Equal(ExecutionStatus.Cancelled, await running);
        Assert.Equal(1, calls);
        var history = Assert.Single(await new SqliteExecutionHistoryReader(database).GetRecentAsync(50, token));
        Assert.Equal(ExecutionStatus.Cancelled, history.Status);
        Assert.Equal(ExecutionStatus.Cancelled, Assert.Single(history.Steps).Status);
    }

    [Fact]
    public async Task StopAndDraftSave_PreservePublishedActivationState()
    {
        var token = TestContext.Current.CancellationToken;
        var database = new SqliteDatabase(new AppDataPaths(directory)); await database.InitializeAsync(token);
        var repository = new SqliteWorkflowRepository(database);
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Active", true, [], []);
        await repository.PublishAsync(workflow, token);
        await repository.SaveDraftAsync(workflow with { Enabled = false, Name = "Draft" }, token);
        Assert.Single(await repository.GetEnabledPublishedAsync(token));
        await repository.SetEnabledAsync(workflow.Id, false, token);
        Assert.Empty(await repository.GetEnabledPublishedAsync(token));
        Assert.NotNull(await repository.GetRevisionAsync(workflow.Id, 1, token));
    }
}
