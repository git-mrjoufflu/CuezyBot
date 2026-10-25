using Automation.Domain.Execution;

namespace Automation.Contracts.Execution;

public interface IExecutionStore
{
    Task<ExecutionRequest?> TryClaimNextAsync(CancellationToken cancellationToken = default);
    Task<ExecutionRequest?> TryClaimAsync(Guid requestId, CancellationToken cancellationToken = default);

    Task RecordStepAsync(
        ExecutionStepResult step,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid executionId,
        ExecutionStatus status,
        string? error,
        CancellationToken cancellationToken = default);
}
