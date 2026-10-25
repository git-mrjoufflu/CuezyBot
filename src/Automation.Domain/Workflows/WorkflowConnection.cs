namespace Automation.Domain.Workflows;

public sealed record WorkflowConnection(
    Guid FromBlockId,
    string Output,
    Guid ToBlockId);
