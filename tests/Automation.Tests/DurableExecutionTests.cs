using System.Text.Json.Nodes;
using Automation.Contracts.Execution;
using Automation.Contracts.Obs;
using Automation.Contracts.Persistence;
using Automation.Domain.Execution;
using Automation.Engine.Execution;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Tests;

public sealed class DurableExecutionTests : IAsyncLifetime
{
    private readonly string applicationDirectory = Path.Combine(
        Path.GetTempPath(),
        $"automation-tests-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(applicationDirectory))
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task DuplicateEvent_DoesNotRunOrIncrementTwice()
    {
        var database = new SqliteDatabase(new AppDataPaths(applicationDirectory));
        await database.InitializeAsync(TestContext.Current.CancellationToken);

        IEventAdmissionStore admissions = new SqliteEventAdmissionStore(database);
        IPersistentVariableStore variables = new SqlitePersistentVariableStore(database);
        IExecutionStore executions = new SqliteExecutionStore(database);
        var obs = new FakeObsController();

        var eventEnvelope = CreateEvent();
        var request = CreateRequest(eventEnvelope.Id);

        var firstAdmission = await admissions.TryAdmitAsync(
            eventEnvelope,
            [request],
            TestContext.Current.CancellationToken);
        var firstExecution = await new PrototypeExecutionRunner(executions, variables, obs)
            .RunNextAsync(TestContext.Current.CancellationToken);

        var duplicateAdmission = await admissions.TryAdmitAsync(
            eventEnvelope,
            [request],
            TestContext.Current.CancellationToken);
        var duplicateExecution = await new PrototypeExecutionRunner(executions, variables, obs)
            .RunNextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EventAdmissionResult.Admitted, firstAdmission);
        Assert.Equal(ExecutionStatus.Succeeded, firstExecution);
        Assert.Equal(EventAdmissionResult.Duplicate, duplicateAdmission);
        Assert.Null(duplicateExecution);
        Assert.Equal(1, await variables.GetIntegerAsync(
            "followersTraites",
            TestContext.Current.CancellationToken));
        Assert.Equal(1, obs.RequestCount);
    }

    private static EventEnvelope CreateEvent()
    {
        return new EventEnvelope(
            Guid.NewGuid(),
            "twitch",
            "test-connection",
            "message-1",
            "channel.follow",
            2,
            DateTimeOffset.UtcNow,
            new JsonObject { ["userName"] = "Test" });
    }

    private static ExecutionRequest CreateRequest(Guid eventId)
    {
        return new ExecutionRequest(
            Guid.NewGuid(),
            eventId,
            PrototypeWorkflowIds.WorkflowId,
            1,
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeObsController : IObsController
    {
        public int RequestCount { get; private set; }

        public Task SetCurrentProgramSceneAsync(
            string connectionId,
            string sceneName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.CompletedTask;
        }
    }
}
