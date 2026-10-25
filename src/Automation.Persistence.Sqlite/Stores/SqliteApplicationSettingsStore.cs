using Automation.Contracts.Persistence;
using Automation.Persistence.Sqlite.Storage;

namespace Automation.Persistence.Sqlite.Stores;

public sealed class SqliteApplicationSettingsStore(SqliteDatabase database) : IApplicationSettingsStore
{
    public async Task SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO application_settings (key, value, updated_at)
            VALUES ($key, $value, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM application_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM application_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("La clé du réglage est obligatoire.", nameof(key));
        }
    }
}
