namespace Automation.Domain.Workflows;

public sealed record WorkflowDefinition(
    Guid Id,
    int Revision,
    string Name,
    bool Enabled,
    IReadOnlyList<WorkflowBlock> Blocks,
    IReadOnlyList<WorkflowConnection> Connections);
