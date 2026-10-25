using Automation.Contracts.Execution;
using Automation.Domain.Execution;
using Automation.Persistence.Sqlite.Storage;
using Microsoft.Data.Sqlite;

namespace Automation.Persistence.Sqlite.Stores;

public sealed class SqliteExecutionStore(SqliteDatabase database) : IExecutionStore
{
    public async Task<int> RecoverAbandonedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE execution_requests SET
                status = CASE WHEN status = 'Running' THEN 'Interrupted' ELSE 'Expired' END,
                completed_at = $completedAt,
                error = 'Previous session ended before completion. Not replayed automatically; external effects may already have occurred.'
            WHERE status IN ('Running', 'Pending');
            """;
        command.Parameters.AddWithValue("$completedAt", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public Task<ExecutionRequest?> TryClaimNextAsync(CancellationToken cancellationToken = default) => ClaimAsync(null, cancellationToken);

    public Task<ExecutionRequest?> TryClaimAsync(Guid requestId, CancellationToken cancellationToken = default) => ClaimAsync(requestId, cancellationToken);

    private async Task<ExecutionRequest?> ClaimAsync(Guid? requestId, CancellationToken cancellationToken)
    {
        await using var connection = database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = connection.CreateCommand();
        select.Transaction = (SqliteTransaction)transaction;
        select.CommandText = """
            SELECT id, event_id, workflow_id, workflow_revision, created_at
            FROM execution_requests
            WHERE status = 'Pending' AND ($requestId IS NULL OR id = $requestId)
            ORDER BY rowid
            LIMIT 1;
            """;
        select.Parameters.AddWithValue("$requestId", requestId is { } id ? id.ToString("D") : DBNull.Value);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var request = new ExecutionRequest(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetInt32(3),
            DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture));
        await reader.DisposeAsync();

        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = """
            UPDATE execution_requests
            SET status = 'Running', started_at = $startedAt
            WHERE id = $id AND status = 'Pending';
            """;
        update.Parameters.AddWithValue("$id", request.Id.ToString("D"));
        update.Parameters.AddWithValue("$startedAt", DateTimeOffset.UtcNow.ToString("O"));
        var claimed = await update.ExecuteNonQueryAsync(cancellationToken);

        if (claimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return request;
    }

    public async Task RecordStepAsync(
        ExecutionStepResult step,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO execution_steps (
                execution_id, block_id, sequence, status, message, started_at, completed_at)
            VALUES ($executionId, $blockId, $sequence, $status, $message, $startedAt, $completedAt);
            """;
        command.Parameters.AddWithValue("$executionId", step.ExecutionId.ToString("D"));
        command.Parameters.AddWithValue("$blockId", step.BlockId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", step.Sequence);
        command.Parameters.AddWithValue("$status", step.Status.ToString());
        command.Parameters.AddWithValue("$message", step.Message);
        command.Parameters.AddWithValue("$startedAt", step.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", step.CompletedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        Guid executionId,
        ExecutionStatus status,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE execution_requests
            SET status = $status, completed_at = $completedAt, error = $error
            WHERE id = $id AND status = 'Running';
            """;
        command.Parameters.AddWithValue("$id", executionId.ToString("D"));
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$completedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed != 1)
        {
            throw new InvalidOperationException("L’exécution n’était plus dans l’état Running.");
        }
    }
}
