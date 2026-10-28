using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;
using Microsoft.Data.Sqlite;

namespace Automation.Tests;

public sealed class SqliteBackupTests
{
    [Fact]
    public async Task RestoreKeepsSafetyCopyAndRefusesAnActiveApplication()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cuezy-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(directory);
            var database = new SqliteDatabase(paths);
            await database.InitializeAsync(TestContext.Current.CancellationToken);
            using (var live = database.OpenConnection())
            using (var write = live.CreateCommand())
            {
                write.CommandText = """
                    INSERT INTO application_settings VALUES ('saved', 'old', 'now');
                    INSERT INTO workflows (id,name,draft_json,is_active,updated_at) VALUES ('workflow','Test','{}',1,'now');
                    INSERT INTO event_inbox VALUES ('event','test','local','message','manual.run',1,'now','{}');
                    INSERT INTO execution_requests (id,event_id,workflow_id,workflow_revision,status,created_at) VALUES ('request','event','workflow',1,'Pending','now');
                    """;
                write.ExecuteNonQuery();
            }
            var backup = SqliteBackups.Create(paths);
            using (var live = database.OpenConnection())
            using (var write = live.CreateCommand())
            {
                write.CommandText = "UPDATE application_settings SET value = 'new' WHERE key = 'saved';";
                write.ExecuteNonQuery();
            }
            using (RuntimeDataLease.Acquire(paths)) Assert.Throws<InvalidOperationException>(() => SqliteBackups.RestoreOffline(paths, backup));
            var safety = SqliteBackups.RestoreOffline(paths, backup);
            Assert.NotNull(safety);
            using var liveAfter = database.OpenConnection();
            using var read = liveAfter.CreateCommand();
            read.CommandText = "SELECT value FROM application_settings WHERE key = 'saved';";
            Assert.Equal("old", read.ExecuteScalar());
            read.CommandText = "SELECT is_active FROM workflows WHERE id = 'workflow';";
            Assert.Equal(0L, read.ExecuteScalar());
            read.CommandText = "SELECT status FROM execution_requests WHERE id = 'request';";
            Assert.Equal("Expired", read.ExecuteScalar());
            using var safetyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = safety, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            safetyConnection.Open();
            using var readSafety = safetyConnection.CreateCommand();
            readSafety.CommandText = "SELECT value FROM application_settings WHERE key = 'saved';";
            Assert.Equal("new", readSafety.ExecuteScalar());
            Assert.Throws<InvalidOperationException>(() => SqliteBackups.RestoreOffline(paths, paths.DatabasePath));
        }
        finally { TestDataCleanup.Delete(directory); }
    }

    [Fact]
    public async Task InvalidBackupLeavesExistingDataUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cuezy-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(directory);
            var database = new SqliteDatabase(paths);
            await database.InitializeAsync(TestContext.Current.CancellationToken);
            var invalid = Path.Combine(paths.BackupsDirectory, "unrelated.db");
            using (var empty = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = invalid, Pooling = false }.ToString())) empty.Open();
            Assert.Throws<InvalidDataException>(() => SqliteBackups.RestoreOffline(paths, invalid));
            using var live = database.OpenConnection();
            using var check = live.CreateCommand();
            check.CommandText = "PRAGMA user_version;";
            Assert.Equal(5L, check.ExecuteScalar());
            Assert.Empty(Directory.GetFiles(paths.BackupsDirectory, "*beforeRestore*"));
        }
        finally { TestDataCleanup.Delete(directory); }
    }

    [Fact]
    public async Task BackupIncludesWalDataAndRemainsAnIndependentSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cuezy-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(directory);
            var database = new SqliteDatabase(paths);
            await database.InitializeAsync(TestContext.Current.CancellationToken);
            using var live = database.OpenConnection();
            using var command = live.CreateCommand();
            command.CommandText = "INSERT INTO application_settings VALUES ('test-language', 'English', 'now');";
            command.ExecuteNonQuery();
            var backup = SqliteBackups.Create(paths);
            command.CommandText = "UPDATE application_settings SET value = 'Français' WHERE key = 'test-language';";
            command.ExecuteNonQuery();
            using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            snapshot.Open();
            using var read = snapshot.CreateCommand();
            read.CommandText = "SELECT value FROM application_settings WHERE key = 'test-language';";
            Assert.Equal("English", read.ExecuteScalar());
            read.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", read.ExecuteScalar());
            Assert.Empty(Directory.GetFiles(paths.BackupsDirectory, "*.partial"));
            Assert.NotEqual(backup, SqliteBackups.Create(paths));
        }
        finally { TestDataCleanup.Delete(directory); }
    }

    [Fact]
    public async Task MigrationBacksUpThePreviousSchemaBeforeChangingIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cuezy-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(directory);
            var database = new SqliteDatabase(paths);
            await database.InitializeAsync(TestContext.Current.CancellationToken);
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE persistent_variables DROP COLUMN text_value; ALTER TABLE persistent_variables DROP COLUMN value_type; PRAGMA user_version = 4;";
                command.ExecuteNonQuery();
            }
            await database.InitializeAsync(TestContext.Current.CancellationToken);
            var backup = Assert.Single(Directory.GetFiles(paths.BackupsDirectory, "*.db"));
            using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            snapshot.Open();
            using var read = snapshot.CreateCommand();
            read.CommandText = "PRAGMA user_version;";
            Assert.Equal(4L, read.ExecuteScalar());
            using var current = database.OpenConnection();
            using var version = current.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(5L, version.ExecuteScalar());
        }
        finally { TestDataCleanup.Delete(directory); }
    }
}
