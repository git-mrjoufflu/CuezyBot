using Automation.Contracts.Persistence;
using Automation.Persistence.Sqlite.Storage;

namespace Automation.Persistence.Sqlite.Stores;

public sealed class SqlitePersistentVariableStore(SqliteDatabase database) : IPersistentVariableStore
{
    public async Task SetIntegerAsync(string name, long value, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO persistent_variables (name, integer_value, revision, updated_at)
            VALUES ($name, $value, 1, $updatedAt)
            ON CONFLICT(name) DO UPDATE SET integer_value = excluded.integer_value,
                revision = revision + 1, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
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
            INSERT INTO persistent_variables (name, integer_value, revision, updated_at)
            VALUES ($name, $amount, 1, $updatedAt)
            ON CONFLICT(name) DO UPDATE SET
                integer_value = integer_value + excluded.integer_value,
                revision = revision + 1,
                updated_at = excluded.updated_at
            RETURNING integer_value;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<long?> GetIntegerAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);

        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT integer_value FROM persistent_variables WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Le nom de variable est obligatoire.", nameof(name));
        }
    }
}
