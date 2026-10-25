namespace Automation.Domain.Execution;

public enum ExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Expired,
    Skipped,
    Interrupted,
    UnknownOutcome
}
