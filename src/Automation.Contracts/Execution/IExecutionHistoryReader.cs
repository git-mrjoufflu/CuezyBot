using Automation.Domain.Execution;

namespace Automation.Contracts.Execution;

public interface IExecutionHistoryReader
{
    Task<IReadOnlyList<ExecutionHistoryEntry>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken = default);
}
