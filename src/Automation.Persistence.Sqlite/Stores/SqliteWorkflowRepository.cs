using System.Text.Json;
using Automation.Contracts.Persistence;
using Automation.Domain.Workflows;
using Automation.Persistence.Sqlite.Storage;
using Microsoft.Data.Sqlite;

namespace Automation.Persistence.Sqlite.Stores;

public sealed class SqliteWorkflowRepository(SqliteDatabase database) : IWorkflowRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveDraftAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workflows (id, name, draft_json, is_active, updated_at)
            VALUES ($id, $name, $draftJson, $isActive, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                draft_json = excluded.draft_json,
                is_active = excluded.is_active,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", workflow.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", workflow.Name);
        command.Parameters.AddWithValue("$draftJson", Serialize(workflow));
        command.Parameters.AddWithValue("$isActive", workflow.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<WorkflowDefinition?> GetDraftAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default) =>
        ReadDefinitionAsync(
            "SELECT draft_json FROM workflows WHERE id = $id;",
            workflowId,
            cancellationToken);

    public async Task PublishAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        await using var connection = database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var ensureDraft = connection.CreateCommand())
        {
            ensureDraft.Transaction = (SqliteTransaction)transaction;
            ensureDraft.CommandText = """
                INSERT INTO workflows (id, name, draft_json, is_active, updated_at)
                VALUES ($id, $name, $definition, $isActive, $publishedAt)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    draft_json = excluded.draft_json,
                    is_active = excluded.is_active,
                    updated_at = excluded.updated_at;
                """;
            AddWorkflowParameters(ensureDraft, workflow);
            await ensureDraft.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var revision = connection.CreateCommand())
        {
            revision.Transaction = (SqliteTransaction)transaction;
            revision.CommandText = """
                INSERT INTO workflow_revisions (
                    workflow_id, revision, definition_json, published_at)
                VALUES ($id, $revision, $definition, $publishedAt);
                """;
            AddWorkflowParameters(revision, workflow);
            await revision.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE workflows
                SET published_revision = $revision
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$id", workflow.Id.ToString("D"));
            update.Parameters.AddWithValue("$revision", workflow.Revision);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<WorkflowDefinition?> GetPublishedAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default) =>
        ReadDefinitionAsync(
            """
            SELECT revision.definition_json
            FROM workflows AS workflow
            INNER JOIN workflow_revisions AS revision
                ON revision.workflow_id = workflow.id
                AND revision.revision = workflow.published_revision
            WHERE workflow.id = $id;
            """,
            workflowId,
            cancellationToken);

    private async Task<WorkflowDefinition?> ReadDefinitionAsync(
        string sql,
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", workflowId.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string json ? Deserialize(json) : null;
    }

    private static void AddWorkflowParameters(
        SqliteCommand command,
        WorkflowDefinition workflow)
    {
        command.Parameters.AddWithValue("$id", workflow.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", workflow.Name);
        command.Parameters.AddWithValue("$definition", Serialize(workflow));
        command.Parameters.AddWithValue("$revision", workflow.Revision);
        command.Parameters.AddWithValue("$isActive", workflow.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$publishedAt", DateTimeOffset.UtcNow.ToString("O"));
    }

    private static string Serialize(WorkflowDefinition workflow) =>
        JsonSerializer.Serialize(workflow, SerializerOptions);

    private static WorkflowDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<WorkflowDefinition>(json, SerializerOptions)
        ?? throw new InvalidDataException("La définition d’automatisation enregistrée est vide.");
}
