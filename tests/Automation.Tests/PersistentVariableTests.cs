using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Tests;

public sealed class PersistentVariableTests
{
    [Fact]
    public async Task SetAndIncrement_SurviveReopeningAndDoNotChangeOtherVariables()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"variable-test-{Guid.NewGuid():N}");
        try
        {
            var token = TestContext.Current.CancellationToken;
            var database = new SqliteDatabase(new AppDataPaths(directory)); await database.InitializeAsync(token);
            var store = new SqlitePersistentVariableStore(database);
            await store.SetIntegerAsync("first", 10, token);
            await store.SetIntegerAsync("second", 99, token);
            await store.SetIntegerAsync("first", -5, token);
            Assert.Equal(-2, await store.IncrementAsync("first", 3, token));
            var reopened = new SqlitePersistentVariableStore(new SqliteDatabase(new AppDataPaths(directory)));
            Assert.Equal(-2, await reopened.GetIntegerAsync("first", token));
            Assert.Equal(99, await reopened.GetIntegerAsync("second", token));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
