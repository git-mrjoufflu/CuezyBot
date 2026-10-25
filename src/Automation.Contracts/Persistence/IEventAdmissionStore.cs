using Automation.Domain.Execution;

namespace Automation.Contracts.Persistence;

public interface IEventAdmissionStore
{
    Task<EventAdmissionResult> TryAdmitAsync(
        EventEnvelope eventEnvelope,
        IReadOnlyCollection<ExecutionRequest> requests,
        CancellationToken cancellationToken = default);
}
