using System.Text.Json.Nodes;
using Automation.Connectors.Twitch;
using Automation.Domain.Workflows;
using Automation.Engine.Blocks;
using Automation.Engine.Execution;
using Automation.Engine.Workflows;

namespace Automation.Tests;

public sealed class TwitchTriggerRoutingTests
{
    [Fact]
    public void EveryCatalogEventParsesAndHasAnExecutableTrigger()
    {
        foreach (var trigger in TwitchTriggerCatalog.All)
        {
            var message = new JsonObject
            {
                ["metadata"] = new JsonObject { ["message_id"] = "unique-event", ["subscription_type"] = trigger.EventType, ["subscription_version"] = trigger.Version },
                ["payload"] = new JsonObject { ["event"] = new JsonObject { ["is_anonymous"] = true, ["bits"] = 100, ["reward"] = new JsonObject { ["title"] = "Reward" } } }
            };
            var parsed = TwitchEventParser.ParseNotification(message);
            Assert.Equal(trigger.EventType, parsed.EventType);
            Assert.Equal("unique-event", parsed.MessageId);
            Assert.Equal(100, parsed.Data["bits"]!.GetValue<int>());
            Assert.NotNull(new InitialBlockCatalog().Find(trigger.BlockType, 1));
            var start = Guid.NewGuid(); var action = Guid.NewGuid();
            var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Test", false, [new(start, trigger.BlockType, 1, new JsonObject()), new(action, "core.delay", 1, new JsonObject { ["seconds"] = 0 })], [new(start, "next", action)]);
            Assert.Empty(ExecutableWorkflowValidator.Validate(workflow, true));
            Assert.True(new WorkflowValidator(new InitialBlockCatalog()).Validate(workflow).IsValid);
        }
    }

    [Fact]
    public async Task MatchingEventRunsOnlyItsOwnBranchAndSharedDestinationOnce()
    {
        var raid = new WorkflowBlock(Guid.NewGuid(), "twitch.channel.raid.received", 1, new JsonObject());
        var cheer = new WorkflowBlock(Guid.NewGuid(), "twitch.channel.cheer.received", 1, new JsonObject());
        var raidAction = new WorkflowBlock(Guid.NewGuid(), "core.delay", 1, new JsonObject { ["seconds"] = 0 });
        var cheerAction = new WorkflowBlock(Guid.NewGuid(), "core.delay", 1, new JsonObject { ["seconds"] = 0 });
        var join = new WorkflowBlock(Guid.NewGuid(), "core.delay", 1, new JsonObject { ["seconds"] = 0 });
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Multiple events", true, [raid, cheer, raidAction, cheerAction, join],
            [new(raid.Id, "next", raidAction.Id), new(cheer.Id, "next", cheerAction.Id), new(raidAction.Id, "next", join.Id), new(cheerAction.Id, "next", join.Id)]);
        Assert.True(new WorkflowValidator(new InitialBlockCatalog()).Validate(workflow).IsValid);
        var called = new List<Guid>();
        await new WorkflowGraphRunner().RunAsync(WorkflowEventRouter.Select(workflow, raid.Type), (block, _) => { called.Add(block.Id); return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { raid.Id, raidAction.Id, join.Id }, called);
        Assert.Empty(WorkflowEventRouter.Select(workflow, "unknown").Blocks);
    }

    [Fact]
    public async Task ManualRunNeverActivatesTwitchBranches()
    {
        var manual = Guid.NewGuid(); var raid = Guid.NewGuid(); var manualAction = Guid.NewGuid(); var raidAction = Guid.NewGuid();
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Manual test", true,
            [new(manual, "manual.run", 1, new JsonObject()), new(raid, "twitch.channel.raid.received", 1, new JsonObject()), new(manualAction, "core.delay", 1, new JsonObject()), new(raidAction, "core.delay", 1, new JsonObject())],
            [new(manual, "next", manualAction), new(raid, "next", raidAction)]);
        var executed = new List<Guid>();
        await new WorkflowGraphRunner().RunAsync(WorkflowEventRouter.Select(workflow, "manual.run"), (block, _) => { executed.Add(block.Id); return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { manual, manualAction }, executed);
        Assert.Empty(WorkflowEventRouter.Select(workflow with { Blocks = workflow.Blocks.Where(block => block.Id != manual).ToArray(), Connections = workflow.Connections.Where(edge => edge.FromBlockId != manual).ToArray() }, "manual.run").Blocks);
    }

    [Fact]
    public void RaidUsesDestinationChannelAndFollowUsesModeratorCondition()
    {
        Assert.Equal("123", TwitchTriggerCatalog.All.Single(item => item.EventType == "channel.raid").Condition("123")["to_broadcaster_user_id"]!.ToString());
        Assert.Equal("123", TwitchTriggerCatalog.All.Single(item => item.EventType == "channel.follow").Condition("123")["moderator_user_id"]!.ToString());
    }
}
