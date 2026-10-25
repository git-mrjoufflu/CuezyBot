using Automation.Contracts.Execution;
using Automation.Contracts.Obs;
using Automation.Contracts.Persistence;
using Automation.Domain.Execution;
using Automation.Engine.Execution;

namespace Automation.Application.Follows;

public sealed class FollowerAutomationService(
    IEventAdmissionStore admissions,
    IExecutionStore executions,
    IPersistentVariableStore variables,
    IObsController obs)
{
    public async Task<FollowerProcessingResult> ProcessAsync(
        EventEnvelope eventEnvelope,
        CancellationToken cancellationToken = default)
    {
        if (!eventEnvelope.EventType.Equals("channel.follow", StringComparison.Ordinal) ||
            eventEnvelope.EventVersion != 2)
        {
            throw new ArgumentException(
                "Ce service accepte uniquement les événements channel.follow version 2.",
                nameof(eventEnvelope));
        }

        var request = new ExecutionRequest(
            Guid.NewGuid(),
            eventEnvelope.Id,
            PrototypeWorkflowIds.WorkflowId,
            1,
            DateTimeOffset.UtcNow);
        var admission = await admissions.TryAdmitAsync(
            eventEnvelope,
            [request],
            cancellationToken);

        ExecutionStatus? executionStatus = null;
        if (admission == EventAdmissionResult.Admitted)
        {
            executionStatus = await new PrototypeExecutionRunner(executions, variables, obs)
                .RunNextAsync(cancellationToken);
        }

        var counter = await variables.GetIntegerAsync("followersTraites", cancellationToken) ?? 0;
        return new FollowerProcessingResult(admission, executionStatus, counter);
    }
}
