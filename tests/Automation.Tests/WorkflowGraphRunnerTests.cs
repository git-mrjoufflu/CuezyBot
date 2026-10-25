using Automation.Domain.Workflows;
using Automation.Engine.Execution;
using System.Text.Json.Nodes;

namespace Automation.Tests;

public sealed class WorkflowGraphRunnerTests
{
    private static WorkflowBlock Block(string name) => new(Guid.NewGuid(), name, 1, new JsonObject());

    [Fact]
    public async Task Condition_ExecutesChosenBranchAndSharedContinuationOnlyOnce()
    {
        var condition = Block("condition"); var yes = Block("yes"); var no = Block("no"); var join = Block("join");
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Branches", true, [condition, yes, no, join],
            [new(condition.Id, "yes", yes.Id), new(condition.Id, "no", no.Id), new(yes.Id, "next", join.Id), new(no.Id, "next", join.Id)]);
        var calls = new List<string>();
        await new WorkflowGraphRunner().RunAsync(workflow, (block, _) => { calls.Add(block.Type); return Task.CompletedTask; },
            TestContext.Current.CancellationToken, (block, _) => Task.FromResult<string?>(block.Id == condition.Id ? "no" : null));
        Assert.Equal(new[] { "condition", "no", "join" }, calls);
    }

    [Fact]
    public async Task SharedDestination_RunsOnceAfterBothSources()
    {
        var a = Block("a"); var b = Block("b"); var c = Block("c");
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Graph", true, [c, b, a], [new(a.Id, "next", c.Id), new(b.Id, "next", c.Id)]);
        var called = new List<string>();
        await new WorkflowGraphRunner().RunAsync(workflow, (block, _) => { called.Add(block.Type); return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        Assert.Equal(3, called.Count);
        Assert.Equal("c", called.Last());
    }

    [Fact]
    public async Task Cycle_IsRejectedBeforeAnyEffect()
    {
        var a = Block("a"); var b = Block("b");
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Cycle", true, [a, b], [new(a.Id, "next", b.Id), new(b.Id, "next", a.Id)]);
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowGraphRunner().RunAsync(workflow, (_, _) => { calls++; return Task.CompletedTask; }, TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Failure_StopsDownstreamActions()
    {
        var a = Block("a"); var b = Block("b");
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Failure", true, [a, b], [new(a.Id, "next", b.Id)]);
        var calls = new List<string>();
        await Assert.ThrowsAsync<IOException>(() => new WorkflowGraphRunner().RunAsync(workflow, (block, _) =>
        {
            calls.Add(block.Type);
            throw new IOException("Fake connector failed");
        }, TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "a" }, calls);
    }

    [Fact]
    public async Task Cancellation_StopsBeforeFirstAction()
    {
        var workflow = new WorkflowDefinition(Guid.NewGuid(), 1, "Cancel", true, [Block("a")], []);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorkflowGraphRunner().RunAsync(workflow, (_, _) => throw new Exception("Must not execute"), cancellation.Token));
    }
}
