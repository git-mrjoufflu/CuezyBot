using Automation.Contracts.Persistence;
using Automation.Persistence.Sqlite.Storage;

namespace Automation.Persistence.Sqlite.Stores;

public sealed class SqlitePersistentVariableStore(SqliteDatabase database) : IPersistentVariableStore
{
    public async Task<IReadOnlyList<PersistentVariable>> ListAsync(CancellationToken cancellationToken = default)
    {
        var values = new List<PersistentVariable>();
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, value_type, integer_value, text_value, revision, updated_at, scope, lifetime, viewer_id FROM persistent_variables ORDER BY name COLLATE NOCASE;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(1);
            var value = type == "text" ? reader.GetString(3) : reader.GetInt64(2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            values.Add(new(DisplayName(reader.GetString(0)), type, value, reader.GetInt64(4), DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture), reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
        return values;
    }

    public async Task SetIntegerAsync(string name, long value, CancellationToken cancellationToken = default)
        => await SetIntegerAsync(name, value, "system", "permanent", null, cancellationToken);

    public async Task SetIntegerAsync(string name, long value, string scope, string lifetime, string? viewerId, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO persistent_variables (name, integer_value, value_type, text_value, revision, updated_at, scope, lifetime, viewer_id)
            VALUES ($storageName, $value, 'integer', NULL, 1, $updatedAt, $scope, $lifetime, $viewerId)
            ON CONFLICT(name) DO UPDATE SET integer_value = excluded.integer_value,
                value_type = 'integer', text_value = NULL, revision = revision + 1, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$storageName", StorageName(name, scope, viewerId));
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$lifetime", lifetime); command.Parameters.AddWithValue("$viewerId", (object?)viewerId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetTextAsync(string name, string value, CancellationToken cancellationToken = default)
        => await SetTextAsync(name, value, "system", "permanent", null, cancellationToken);

    public async Task SetTextAsync(string name, string value, string scope, string lifetime, string? viewerId, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO persistent_variables (name, integer_value, value_type, text_value, revision, updated_at, scope, lifetime, viewer_id)
            VALUES ($storageName, 0, 'text', $value, 1, $updatedAt, $scope, $lifetime, $viewerId)
            ON CONFLICT(name) DO UPDATE SET value_type = 'text', text_value = excluded.text_value,
                revision = revision + 1, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$storageName", StorageName(name, scope, viewerId));
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$lifetime", lifetime); command.Parameters.AddWithValue("$viewerId", (object?)viewerId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<long> IncrementAsync(
        string name,
        long amount,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);

        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO persistent_variables (name, integer_value, value_type, text_value, revision, updated_at)
            VALUES ($name, $amount, 'integer', NULL, 1, $updatedAt)
            ON CONFLICT(name) DO UPDATE SET
                integer_value = integer_value + excluded.integer_value,
                revision = revision + 1,
                updated_at = excluded.updated_at
            WHERE persistent_variables.value_type = 'integer'
            RETURNING integer_value;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull) throw new InvalidOperationException($"Variable is not numeric: {name}");
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task DeleteTemporaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection(); await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM persistent_variables WHERE lifetime = 'temporary';";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long?> GetIntegerAsync(
        string name,
        CancellationToken cancellationToken = default)
        => await GetIntegerAsync(name, "system", null, cancellationToken);

    public async Task<long?> GetIntegerAsync(string name, string scope, string? viewerId, CancellationToken cancellationToken = default)
    {
        ValidateName(name);

        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT integer_value FROM persistent_variables WHERE name = $name AND value_type = 'integer';";
        command.Parameters.AddWithValue("$name", StorageName(name, scope, viewerId));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<string?> GetTextAsync(string name, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT text_value FROM persistent_variables WHERE name = $name AND value_type = 'text';";
        command.Parameters.AddWithValue("$name", name);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default)
        => await GetValueAsync(name, "system", null, cancellationToken);

    public async Task<string?> GetValueAsync(string name, string scope, string? viewerId, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN value_type = 'text' THEN text_value ELSE CAST(integer_value AS TEXT) END FROM persistent_variables WHERE name = $name;";
        command.Parameters.AddWithValue("$name", StorageName(name, scope, viewerId));
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
        => await DeleteAsync(name, "system", null, cancellationToken);

    public async Task DeleteAsync(string name, string scope, string? viewerId, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM persistent_variables WHERE name = $name;";
        command.Parameters.AddWithValue("$name", StorageName(name, scope, viewerId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteForAllViewersAsync(string name, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM persistent_variables WHERE name LIKE $prefix;";
        command.Parameters.AddWithValue("$prefix", "viewer:%:" + name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Le nom de variable est obligatoire.", nameof(name));
        }
    }

    private static string StorageName(string name, string scope, string? viewerId) => scope == "viewer" ? $"viewer:{viewerId ?? "unknown"}:{name}" : name;
    private static string DisplayName(string storageName) => storageName.StartsWith("viewer:", StringComparison.Ordinal) && storageName.IndexOf(':', 7) is var separator && separator >= 0 ? storageName[(separator + 1)..] : storageName;
}
