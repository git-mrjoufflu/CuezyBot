using Automation.Contracts.Execution;
using Automation.Domain.Execution;
using Automation.Domain.Workflows;

namespace Automation.Engine.Execution;

public sealed class WorkflowExecutionRunner(IExecutionStore store,
    Func<Guid, int, CancellationToken, Task<WorkflowDefinition?>> resolve,
    Func<WorkflowBlock, CancellationToken, Task<string>> execute,
    Func<WorkflowBlock, CancellationToken, Task<string?>>? chooseOutput = null)
{
    public async Task<ExecutionStatus?> RunNextAsync(CancellationToken cancellationToken = default, Guid? requestId = null)
    {
        var request = requestId is { } id ? await store.TryClaimAsync(id, cancellationToken) : await store.TryClaimNextAsync(cancellationToken);
        if (request is null) return null;
        var failedStatus = ExecutionStatus.Failed;
        try
        {
            var workflow = await resolve(request.WorkflowId, request.WorkflowRevision, cancellationToken)
                ?? throw new InvalidOperationException("Published workflow revision unavailable.");
            var sequence = 0;
            var outputs = new Dictionary<Guid, string?>();
            await new WorkflowGraphRunner().RunAsync(workflow, async (block, token) =>
            {
                var started = DateTimeOffset.UtcNow;
                var step = ++sequence;
                try
                {
                    var message = await execute(block, token);
                    outputs[block.Id] = chooseOutput is null ? null : await chooseOutput(block, token);
                    if (outputs[block.Id] is { } branch) message += $": {branch}";
                    await store.RecordStepAsync(new(request.Id, block.Id, step, ExecutionStatus.Succeeded, message, started, DateTimeOffset.UtcNow), token);
                }
                catch (Exception exception)
                {
                    var external = block.Type.StartsWith("obs.", StringComparison.Ordinal) ||
                        (block.Type.StartsWith("twitch.", StringComparison.Ordinal) && block.Type != "twitch.follow.received" && block.Type != "twitch.get-user");
                    failedStatus = external && exception is (TimeoutException or HttpRequestException or System.Net.WebSockets.WebSocketException or OperationCanceledException)
                        ? ExecutionStatus.UnknownOutcome
                        : exception is OperationCanceledException ? ExecutionStatus.Cancelled : ExecutionStatus.Failed;
                    await store.RecordStepAsync(new(request.Id, block.Id, step,
                        failedStatus,
                        exception.Message, started, DateTimeOffset.UtcNow), CancellationToken.None);
                    throw;
                }
            }, cancellationToken, (block, _) => Task.FromResult(outputs.GetValueOrDefault(block.Id)));
            await store.CompleteAsync(request.Id, ExecutionStatus.Succeeded, null, cancellationToken);
            return ExecutionStatus.Succeeded;
        }
        catch (Exception exception)
        {
            var status = failedStatus == ExecutionStatus.UnknownOutcome ? failedStatus
                : exception is OperationCanceledException ? ExecutionStatus.Cancelled : ExecutionStatus.Failed;
            await store.CompleteAsync(request.Id, status, exception.Message, CancellationToken.None);
            return status;
        }
    }
}
