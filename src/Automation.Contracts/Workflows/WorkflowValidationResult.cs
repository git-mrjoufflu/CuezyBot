namespace Automation.Contracts.Workflows;

public sealed record WorkflowValidationResult(
    IReadOnlyList<WorkflowDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
}
