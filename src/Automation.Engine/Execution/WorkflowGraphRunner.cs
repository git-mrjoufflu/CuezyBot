using Automation.Domain.Workflows;

namespace Automation.Engine.Execution;

/// <summary>Runs a DAG in dependency order. A shared destination executes once.</summary>
public sealed class WorkflowGraphRunner
{
    public async Task RunAsync(WorkflowDefinition workflow,
        Func<WorkflowBlock, CancellationToken, Task> execute,
        CancellationToken cancellationToken = default)
    {
        var blocks = workflow.Blocks.ToDictionary(block => block.Id);
        var edges = workflow.Connections.Distinct().ToArray();
        if (edges.Any(edge => !blocks.ContainsKey(edge.FromBlockId) || !blocks.ContainsKey(edge.ToBlockId)))
            throw new InvalidOperationException("workflow.connection.missing");
        var incoming = blocks.Keys.ToDictionary(id => id, id => edges.Count(edge => edge.ToBlockId == id));
        var ready = new Queue<Guid>(workflow.Blocks.Where(block => incoming[block.Id] == 0).Select(block => block.Id));
        var ordered = new List<WorkflowBlock>();
        while (ready.TryDequeue(out var id))
        {
            ordered.Add(blocks[id]);
            foreach (var edge in edges.Where(edge => edge.FromBlockId == id))
                if (--incoming[edge.ToBlockId] == 0) ready.Enqueue(edge.ToBlockId);
        }
        if (ordered.Count != blocks.Count) throw new InvalidOperationException("workflow.cycle");
        foreach (var block in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await execute(block, cancellationToken);
        }
    }
}
