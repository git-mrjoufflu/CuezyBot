using Automation.Contracts.Execution;
using Automation.Contracts.Obs;
using Automation.Contracts.Persistence;
using Automation.Domain.Execution;

namespace Automation.Engine.Execution;

public sealed class PrototypeExecutionRunner(
    IExecutionStore executions,
    IPersistentVariableStore variables,
    IObsController obs)
{
    public async Task<ExecutionStatus?> RunNextAsync(CancellationToken cancellationToken = default)
    {
        var request = await executions.TryClaimNextAsync(cancellationToken);
        if (request is null)
        {
            return null;
        }

        try
        {
            var counterStartedAt = DateTimeOffset.UtcNow;
            var counter = await variables.IncrementAsync("followersTraites", 1, cancellationToken);
            await executions.RecordStepAsync(
                new ExecutionStepResult(
                    request.Id,
                    PrototypeWorkflowIds.CounterBlockId,
                    1,
                    ExecutionStatus.Succeeded,
                    $"Compteur followersTraites : {counter}",
                    counterStartedAt,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            var obsStartedAt = DateTimeOffset.UtcNow;
            await obs.SetCurrentProgramSceneAsync("obs-principal", "Merci", cancellationToken);
            await executions.RecordStepAsync(
                new ExecutionStepResult(
                    request.Id,
                    PrototypeWorkflowIds.ObsBlockId,
                    2,
                    ExecutionStatus.Succeeded,
                    "Scène OBS « Merci » sélectionnée.",
                    obsStartedAt,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            await executions.CompleteAsync(
                request.Id,
                ExecutionStatus.Succeeded,
                null,
                cancellationToken);
            return ExecutionStatus.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await executions.CompleteAsync(
                request.Id,
                ExecutionStatus.Cancelled,
                "Exécution annulée.",
                CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await executions.CompleteAsync(
                request.Id,
                ExecutionStatus.Failed,
                exception.Message,
                CancellationToken.None);
            return ExecutionStatus.Failed;
        }
    }
}
