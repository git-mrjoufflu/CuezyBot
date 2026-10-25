using Automation.Contracts.Execution;
using Automation.Domain.Execution;
using Automation.Persistence.Sqlite.Storage;

namespace Automation.Persistence.Sqlite.Stores;

public sealed class SqliteExecutionHistoryReader(SqliteDatabase database) : IExecutionHistoryReader
{
    public async Task<IReadOnlyList<ExecutionHistoryEntry>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Le nombre d’exécutions doit être compris entre 1 et 500.");
        }

        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT request.id, request.event_id, request.workflow_id,
                   request.workflow_revision, request.status, request.created_at,
                   request.started_at, request.completed_at, request.error,
                   event.provider_event_id, event.event_type, event.received_at,
                   event.payload_json
            FROM execution_requests AS request
            INNER JOIN event_inbox AS event ON event.id = request.event_id
            ORDER BY request.rowid DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        var entries = new List<ExecutionHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var executionId = Guid.Parse(reader.GetString(0));
            entries.Add(new ExecutionHistoryEntry(
                executionId,
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                Enum.Parse<ExecutionStatus>(reader.GetString(4)),
                ParseDate(reader.GetString(5)),
                reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
                reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                ParseDate(reader.GetString(11)),
                reader.GetString(12),
                []));
        }

        await reader.DisposeAsync();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            entries[index] = entry with
            {
                Steps = await ReadStepsAsync(connection, entry.Id, cancellationToken)
            };
        }

        return entries;
    }

    private static async Task<IReadOnlyList<ExecutionStepResult>> ReadStepsAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT block_id, sequence, status, message, started_at, completed_at
            FROM execution_steps
            WHERE execution_id = $executionId
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$executionId", executionId.ToString("D"));

        var steps = new List<ExecutionStepResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            steps.Add(new ExecutionStepResult(
                executionId,
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                Enum.Parse<ExecutionStatus>(reader.GetString(2)),
                reader.GetString(3),
                ParseDate(reader.GetString(4)),
                ParseDate(reader.GetString(5))));
        }

        return steps;
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
