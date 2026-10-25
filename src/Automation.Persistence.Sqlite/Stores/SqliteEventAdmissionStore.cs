using Automation.Contracts.Persistence;
using Automation.Domain.Execution;
using Automation.Persistence.Sqlite.Storage;
using Microsoft.Data.Sqlite;

namespace Automation.Persistence.Sqlite.Stores;

public sealed class SqliteEventAdmissionStore(SqliteDatabase database) : IEventAdmissionStore
{
    public async Task<EventAdmissionResult> TryAdmitAsync(
        EventEnvelope eventEnvelope,
        IReadOnlyCollection<ExecutionRequest> requests,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var eventCommand = connection.CreateCommand();
        eventCommand.Transaction = (SqliteTransaction)transaction;
        eventCommand.CommandText = """
            INSERT INTO event_inbox (
                id, provider, connection_id, provider_event_id, event_type,
                event_version, received_at, payload_json)
            VALUES (
                $id, $provider, $connectionId, $providerEventId, $eventType,
                $eventVersion, $receivedAt, $payloadJson)
            ON CONFLICT(provider, connection_id, provider_event_id) DO NOTHING;
            """;
        eventCommand.Parameters.AddWithValue("$id", eventEnvelope.Id.ToString("D"));
        eventCommand.Parameters.AddWithValue("$provider", eventEnvelope.Provider);
        eventCommand.Parameters.AddWithValue("$connectionId", eventEnvelope.ConnectionId);
        eventCommand.Parameters.AddWithValue("$providerEventId", eventEnvelope.ProviderEventId);
        eventCommand.Parameters.AddWithValue("$eventType", eventEnvelope.EventType);
        eventCommand.Parameters.AddWithValue("$eventVersion", eventEnvelope.EventVersion);
        eventCommand.Parameters.AddWithValue("$receivedAt", eventEnvelope.ReceivedAt.ToString("O"));
        eventCommand.Parameters.AddWithValue("$payloadJson", eventEnvelope.Data.ToJsonString());

        var inserted = await eventCommand.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EventAdmissionResult.Duplicate;
        }

        foreach (var request in requests)
        {
            await InsertRequestAsync(connection, (SqliteTransaction)transaction, request, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return EventAdmissionResult.Admitted;
    }

    private static async Task InsertRequestAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO execution_requests (
                id, event_id, workflow_id, workflow_revision, status, created_at)
            VALUES ($id, $eventId, $workflowId, $workflowRevision, 'Pending', $createdAt);
            """;
        command.Parameters.AddWithValue("$id", request.Id.ToString("D"));
        command.Parameters.AddWithValue("$eventId", request.EventId.ToString("D"));
        command.Parameters.AddWithValue("$workflowId", request.WorkflowId.ToString("D"));
        command.Parameters.AddWithValue("$workflowRevision", request.WorkflowRevision);
        command.Parameters.AddWithValue("$createdAt", request.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
