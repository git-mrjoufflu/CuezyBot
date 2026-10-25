namespace Automation.Domain.Execution;

public sealed record ExecutionStepResult(
    Guid ExecutionId,
    Guid BlockId,
    int Sequence,
    ExecutionStatus Status,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
