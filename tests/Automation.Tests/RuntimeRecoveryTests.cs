using System.Text.Json.Nodes;
using Automation.Domain.Execution;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Tests;

public sealed class RuntimeRecoveryTests
{
    [Fact]
    public void DataLease_PreventsTwoOwnersAndReleasesOnDispose()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"data-lease-{Guid.NewGuid():N}");
        var paths = new AppDataPaths(directory);
        try
        {
            using (var first = RuntimeDataLease.Acquire(paths))
                Assert.Throws<InvalidOperationException>(() => RuntimeDataLease.Acquire(paths));
            using var reopened = RuntimeDataLease.Acquire(paths);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Recovery_InterruptsRunningAndExpiresPendingWithoutReplay()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"recovery-test-{Guid.NewGuid():N}");
        try
        {
            var token = TestContext.Current.CancellationToken;
            var database = new SqliteDatabase(new AppDataPaths(directory)); await database.InitializeAsync(token);
            var envelope = new EventEnvelope(Guid.NewGuid(), "test", "local", "recovery", "manual.run", 1, DateTimeOffset.UtcNow, new JsonObject());
            var first = new ExecutionRequest(Guid.NewGuid(), envelope.Id, Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
            var second = first with { Id = Guid.NewGuid(), WorkflowId = Guid.NewGuid() };
            await new SqliteEventAdmissionStore(database).TryAdmitAsync(envelope, [first, second], token);
            var executions = new SqliteExecutionStore(database);
            await executions.TryClaimAsync(first.Id, token);
            Assert.Equal(2, await executions.RecoverAbandonedAsync(token));
            Assert.Equal(0, await executions.RecoverAbandonedAsync(token));
            Assert.Null(await executions.TryClaimNextAsync(token));
            var history = await new SqliteExecutionHistoryReader(database).GetRecentAsync(50, token);
            Assert.Equal(ExecutionStatus.Interrupted, history.Single(entry => entry.Id == first.Id).Status);
            Assert.Equal(ExecutionStatus.Expired, history.Single(entry => entry.Id == second.Id).Status);
        }
        finally { TestDataCleanup.Delete(directory); }
    }
}
