using System.Text.Json.Nodes;

namespace Automation.Domain.Workflows;

public sealed record WorkflowBlock(
    Guid Id,
    string Type,
    int Version,
    JsonObject Parameters);
