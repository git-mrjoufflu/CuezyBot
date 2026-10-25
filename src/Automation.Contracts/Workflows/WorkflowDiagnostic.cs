namespace Automation.Contracts.Workflows;

public sealed record WorkflowDiagnostic(
    string Code,
    string Message,
    Guid? BlockId = null);
