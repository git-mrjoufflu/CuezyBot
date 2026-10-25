namespace Automation.Domain.Execution;

public sealed record ExecutionHistoryEntry(
    Guid Id,
    Guid EventId,
    Guid WorkflowId,
    int WorkflowRevision,
    ExecutionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    string ProviderEventId,
    string EventType,
    DateTimeOffset EventReceivedAt,
    string EventPayloadJson,
    IReadOnlyList<ExecutionStepResult> Steps);
