namespace Automation.Domain.Execution;

public sealed record ExecutionRequest(
    Guid Id,
    Guid EventId,
    Guid WorkflowId,
    int WorkflowRevision,
    DateTimeOffset CreatedAt);
