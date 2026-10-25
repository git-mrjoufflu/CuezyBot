using Automation.Contracts.Persistence;
using Automation.Domain.Execution;

namespace Automation.Application.Follows;

public sealed record FollowerProcessingResult(
    EventAdmissionResult Admission,
    ExecutionStatus? ExecutionStatus,
    long CounterValue);
