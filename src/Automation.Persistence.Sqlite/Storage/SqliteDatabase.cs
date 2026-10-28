using Microsoft.Data.Sqlite;

namespace Automation.Persistence.Sqlite.Storage;

public sealed class SqliteDatabase(AppDataPaths paths)
{
    private const int CurrentSchemaVersion = 5;

    public SqliteConnection OpenConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureWritable();

        await using var connection = OpenConnection();
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var rawVersion = await versionCommand.ExecuteScalarAsync(cancellationToken);
        var version = Convert.ToInt32(rawVersion, System.Globalization.CultureInfo.InvariantCulture);

        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"La base utilise le schéma {version}, mais cette version du logiciel prend en charge jusqu’au schéma {CurrentSchemaVersion}.");
        }

        if (version > 0 && version < CurrentSchemaVersion)
            SqliteBackups.Create(connection, paths.BackupsDirectory, $"beforeSchema{version}");

        if (version == 0)
        {
            await ApplyVersion1Async(connection, cancellationToken);
            version = 1;
        }

        if (version == 1)
        {
            await ApplyVersion2Async(connection, cancellationToken);
            version = 2;
        }

        if (version == 2)
        {
            await ApplyVersion3Async(connection, cancellationToken);
            version = 3;
        }

        if (version == 3)
        {
            await ApplyVersion4Async(connection, cancellationToken);
            version = 4;
        }

        if (version == 4)
        {
            await ApplyVersion5Async(connection, cancellationToken);
            version = 5;
        }

        await EnsureVariableMetadataColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureVariableMetadataColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (name, definition) in new[] { ("scope", "TEXT NOT NULL DEFAULT 'system'"), ("lifetime", "TEXT NOT NULL DEFAULT 'permanent'"), ("viewer_id", "TEXT NULL") })
        {
            await using var check = connection.CreateCommand(); check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('persistent_variables') WHERE name = $name"; check.Parameters.AddWithValue("$name", name);
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) != 0) continue;
            await using var alter = connection.CreateCommand(); alter.CommandText = $"ALTER TABLE persistent_variables ADD COLUMN {name} {definition};"; await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var index = connection.CreateCommand(); index.CommandText = "CREATE INDEX IF NOT EXISTS ix_persistent_variables_scope_viewer ON persistent_variables(scope, viewer_id, name);"; await index.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyVersion1Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE event_inbox (
                id TEXT NOT NULL PRIMARY KEY,
                provider TEXT NOT NULL,
                connection_id TEXT NOT NULL,
                provider_event_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                event_version INTEGER NOT NULL,
                received_at TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                UNIQUE(provider, connection_id, provider_event_id)
            );

            CREATE TABLE execution_requests (
                id TEXT NOT NULL PRIMARY KEY,
                event_id TEXT NOT NULL,
                workflow_id TEXT NOT NULL,
                workflow_revision INTEGER NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(event_id, workflow_id, workflow_revision),
                FOREIGN KEY(event_id) REFERENCES event_inbox(id)
            );

            CREATE TABLE persistent_variables (
                name TEXT NOT NULL PRIMARY KEY,
                integer_value INTEGER NOT NULL,
                revision INTEGER NOT NULL,
                updated_at TEXT NOT NULL
            );

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await using var walCommand = connection.CreateCommand();
        walCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;";
        await walCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyVersion2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            ALTER TABLE execution_requests ADD COLUMN started_at TEXT NULL;
            ALTER TABLE execution_requests ADD COLUMN completed_at TEXT NULL;
            ALTER TABLE execution_requests ADD COLUMN error TEXT NULL;

            CREATE TABLE execution_steps (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                execution_id TEXT NOT NULL,
                block_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                status TEXT NOT NULL,
                message TEXT NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT NOT NULL,
                UNIQUE(execution_id, sequence),
                FOREIGN KEY(execution_id) REFERENCES execution_requests(id)
            );

            PRAGMA user_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersion3Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE workflows (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                draft_json TEXT NOT NULL,
                published_revision INTEGER NULL,
                is_active INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE workflow_revisions (
                workflow_id TEXT NOT NULL,
                revision INTEGER NOT NULL,
                definition_json TEXT NOT NULL,
                published_at TEXT NOT NULL,
                PRIMARY KEY(workflow_id, revision),
                FOREIGN KEY(workflow_id) REFERENCES workflows(id)
            );

            PRAGMA user_version = 3;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersion4Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE application_settings (
                key TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            PRAGMA user_version = 4;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersion5Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            ALTER TABLE persistent_variables ADD COLUMN value_type TEXT NOT NULL DEFAULT 'integer';
            ALTER TABLE persistent_variables ADD COLUMN text_value TEXT NULL;
            PRAGMA user_version = 5;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersion6Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "ALTER TABLE persistent_variables ADD COLUMN scope TEXT NOT NULL DEFAULT 'system'; ALTER TABLE persistent_variables ADD COLUMN lifetime TEXT NOT NULL DEFAULT 'permanent'; ALTER TABLE persistent_variables ADD COLUMN viewer_id TEXT NULL; CREATE INDEX IF NOT EXISTS ix_persistent_variables_scope_viewer ON persistent_variables(scope, viewer_id, name); PRAGMA user_version = 6;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
