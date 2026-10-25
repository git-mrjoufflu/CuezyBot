using Automation.Persistence.Sqlite.Storage;
using Microsoft.Data.Sqlite;

namespace Automation.Tests;

internal static class TestDataCleanup
{
    internal static void Delete(string directory)
    {
        if (!Directory.Exists(directory)) return;
        // Windows retains the file handle while SQLite connections sit in a pool.
        // Clear only this fixture's pool; other parallel tests keep their pools.
        using (var connection = new SqliteDatabase(new AppDataPaths(directory)).OpenConnection())
            SqliteConnection.ClearPool(connection);
        Directory.Delete(directory, recursive: true);
    }
}
