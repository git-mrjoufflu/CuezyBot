using Automation.Domain.Workflows;

namespace Automation.Engine.Execution;

/// <summary>Runs a DAG in dependency order. A shared destination executes once.</summary>
public sealed class WorkflowGraphRunner
{
    public async Task RunAsync(WorkflowDefinition workflow,
        Func<WorkflowBlock, CancellationToken, Task> execute,
        CancellationToken cancellationToken = default,
        Func<WorkflowBlock, CancellationToken, Task<string?>>? chooseOutput = null)
    {
        var blocks = workflow.Blocks.ToDictionary(block => block.Id);
        var edges = workflow.Connections.Distinct().ToArray();
        if (edges.Any(edge => !blocks.ContainsKey(edge.FromBlockId) || !blocks.ContainsKey(edge.ToBlockId)))
            throw new InvalidOperationException("workflow.connection.missing");
        var incoming = blocks.Keys.ToDictionary(id => id, id => edges.Count(edge => edge.ToBlockId == id));
        var ready = new Queue<Guid>(workflow.Blocks.Where(block => incoming[block.Id] == 0).Select(block => block.Id));
        var active = ready.ToHashSet();
        var ordered = new List<WorkflowBlock>();
        while (ready.TryDequeue(out var id))
        {
            ordered.Add(blocks[id]);
            foreach (var edge in edges.Where(edge => edge.FromBlockId == id))
                if (--incoming[edge.ToBlockId] == 0) ready.Enqueue(edge.ToBlockId);
        }
        if (ordered.Count != blocks.Count)
        {
            await RunLoopEnabledAsync(workflow, blocks, edges, execute, chooseOutput, cancellationToken);
            return;
        }
        foreach (var block in ordered)
        {
            if (!active.Contains(block.Id)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            await execute(block, cancellationToken);
            var output = chooseOutput is null ? null : await chooseOutput(block, cancellationToken);
            foreach (var edge in edges.Where(edge => edge.FromBlockId == block.Id && (output is null || edge.Output == output)))
                active.Add(edge.ToBlockId);
        }
    }

    private static async Task RunLoopEnabledAsync(
        WorkflowDefinition workflow,
        IReadOnlyDictionary<Guid, WorkflowBlock> blocks,
        IReadOnlyCollection<WorkflowConnection> edges,
        Func<WorkflowBlock, CancellationToken, Task> execute,
        Func<WorkflowBlock, CancellationToken, Task<string?>>? chooseOutput,
        CancellationToken cancellationToken)
    {
        const int maximumSteps = 1000;
        var roots = workflow.Blocks.Where(block => edges.All(edge => edge.ToBlockId != block.Id)).Select(block => block.Id).ToArray();
        if (roots.Length == 0) throw new InvalidOperationException("workflow.loop.no-entry");
        var pending = new Queue<Guid>(roots);
        var steps = 0;
        while (pending.TryDequeue(out var id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++steps > maximumSteps)
                throw new InvalidOperationException("workflow.loop.limit: 1000");
            var block = blocks[id];
            await execute(block, cancellationToken);
            var output = chooseOutput is null ? null : await chooseOutput(block, cancellationToken);
            foreach (var edge in edges.Where(edge => edge.FromBlockId == id && (output is null || edge.Output == output)))
                pending.Enqueue(edge.ToBlockId);
        }
    }
}
