using System.Text.Json.Nodes;
using Automation.Domain.Workflows;
using Automation.Engine.Blocks;
using Automation.Engine.Workflows;

namespace Automation.Tests;

public sealed class WorkflowValidatorTests
{
    [Fact]
    public void Validate_ReportsAnUnknownBlockAndMissingTrigger()
    {
        var blockId = Guid.NewGuid();
        var workflow = new WorkflowDefinition(
            Guid.NewGuid(),
            1,
            "Automatisation invalide",
            false,
            [new WorkflowBlock(blockId, "unknown.block", 1, new JsonObject())],
            []);

        var result = new WorkflowValidator(new InitialBlockCatalog()).Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "block.type.unknown");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "workflow.trigger.count");
    }
}
