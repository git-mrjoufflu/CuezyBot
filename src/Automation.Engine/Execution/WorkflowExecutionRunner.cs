using Automation.Contracts.Execution;
using Automation.Domain.Execution;
using Automation.Domain.Workflows;

namespace Automation.Engine.Execution;

public sealed class WorkflowExecutionRunner(IExecutionStore store,
    Func<Guid, int, CancellationToken, Task<WorkflowDefinition?>> resolve,
    Func<WorkflowBlock, CancellationToken, Task<string>> execute)
{
    public async Task<ExecutionStatus?> RunNextAsync(CancellationToken cancellationToken = default, Guid? requestId = null)
    {
        var request = requestId is { } id ? await store.TryClaimAsync(id, cancellationToken) : await store.TryClaimNextAsync(cancellationToken);
        if (request is null) return null;
        try
        {
            var workflow = await resolve(request.WorkflowId, request.WorkflowRevision, cancellationToken)
                ?? throw new InvalidOperationException("Published workflow revision unavailable.");
            var sequence = 0;
            await new WorkflowGraphRunner().RunAsync(workflow, async (block, token) =>
            {
                var started = DateTimeOffset.UtcNow;
                var step = ++sequence;
                try
                {
                    var message = await execute(block, token);
                    await store.RecordStepAsync(new(request.Id, block.Id, step, ExecutionStatus.Succeeded, message, started, DateTimeOffset.UtcNow), token);
                }
                catch (Exception exception)
                {
                    await store.RecordStepAsync(new(request.Id, block.Id, step,
                        exception is OperationCanceledException ? ExecutionStatus.Cancelled : ExecutionStatus.Failed,
                        exception.Message, started, DateTimeOffset.UtcNow), CancellationToken.None);
                    throw;
                }
            }, cancellationToken);
            await store.CompleteAsync(request.Id, ExecutionStatus.Succeeded, null, cancellationToken);
            return ExecutionStatus.Succeeded;
        }
        catch (Exception exception)
        {
            var status = exception is OperationCanceledException ? ExecutionStatus.Cancelled : ExecutionStatus.Failed;
            await store.CompleteAsync(request.Id, status, exception.Message, CancellationToken.None);
            return status;
        }
    }
}
