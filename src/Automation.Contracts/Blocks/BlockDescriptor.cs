using Automation.Domain.Workflows;

namespace Automation.Contracts.Blocks;

public sealed record BlockDescriptor(
    string Type,
    int Version,
    BlockCategory Category,
    BlockKind Kind,
    string DisplayName,
    string Description,
    IReadOnlySet<string> Outputs);
