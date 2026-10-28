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
            await store.SetTextAsync("greeting", "Bonjour tout le monde", token);
            await store.SetIntegerAsync("first", -5, token);
            Assert.Equal(-2, await store.IncrementAsync("first", 3, token));
            var reopened = new SqlitePersistentVariableStore(new SqliteDatabase(new AppDataPaths(directory)));
            Assert.Equal(-2, await reopened.GetIntegerAsync("first", token));
            Assert.Equal(99, await reopened.GetIntegerAsync("second", token));
            Assert.Equal("Bonjour tout le monde", await reopened.GetTextAsync("greeting", token));
            Assert.Equal("Bonjour tout le monde", await reopened.GetValueAsync("greeting", token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.IncrementAsync("greeting", 1, token));
            var values = await reopened.ListAsync(token);
            Assert.Equal(new[] { "first", "greeting", "second" }, values.Select(value => value.Name));
            Assert.Equal("-2", values[0].Value);
            Assert.Equal("text", values[1].Type);
            await reopened.DeleteAsync("first", token);
            Assert.Null(await reopened.GetIntegerAsync("first", token));
            Assert.Equal(2, (await reopened.ListAsync(token)).Count);
        }
        finally { TestDataCleanup.Delete(directory); }
    }
}
