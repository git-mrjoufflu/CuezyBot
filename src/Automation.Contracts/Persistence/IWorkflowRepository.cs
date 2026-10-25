using Automation.Domain.Workflows;

namespace Automation.Contracts.Persistence;

public interface IWorkflowRepository
{
    Task SaveDraftAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> GetDraftAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default);

    Task PublishAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> GetPublishedAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default);
}
