using System.Text.Json.Nodes;
using Automation.Domain.Workflows;
using Automation.Engine.Blocks;
using Automation.Engine.Workflows;

namespace Automation.Tests;

public sealed class WorkflowValidatorTests
{
    [Fact]
    public void Validate_RejectsCycleButAcceptsSharedDestination()
    {
        var trigger = Guid.NewGuid();
        var action = Guid.NewGuid();
        var other = Guid.NewGuid();
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Parcours", false,
            [new(trigger, "twitch.follow.received", 1, new JsonObject()),
             new(action, "core.delay", 1, new JsonObject()),
             new(other, "core.delay", 1, new JsonObject())],
            [new(trigger, "next", action), new(trigger, "next", other), new(other, "next", action)]);
        var validator = new WorkflowValidator(new InitialBlockCatalog());
        Assert.True(validator.Validate(workflow).IsValid);
        var cyclic = workflow with { Connections = workflow.Connections.Append(new WorkflowConnection(action, "next", other)).ToArray() };
        Assert.Contains(validator.Validate(cyclic).Diagnostics, diagnostic => diagnostic.Code == "workflow.cycle");
    }

    [Fact]
    public void Validate_AcceptsInitialTwitchObsChain()
    {
        var trigger = Guid.NewGuid();
        var counter = Guid.NewGuid();
        var scene = Guid.NewGuid();
        var workflow = new WorkflowDefinition(
            Guid.NewGuid(), 1, "Follow vers OBS", false,
            [
                new WorkflowBlock(trigger, "twitch.follow.received", 1, new JsonObject()),
                new WorkflowBlock(counter, "variables.increment", 1, new JsonObject()),
                new WorkflowBlock(scene, "obs.scene.set-current", 1, new JsonObject { ["scene"] = "Merci" })
            ],
            [
                new WorkflowConnection(trigger, "next", counter),
                new WorkflowConnection(counter, "next", scene)
            ]);

        var result = new WorkflowValidator(new InitialBlockCatalog()).Validate(workflow);

        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

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
