using Automation.Domain.Workflows;
using Automation.Engine.Blocks;

namespace Automation.Engine.Execution;

public static class WorkflowEventRouter
{
    public static WorkflowDefinition Select(WorkflowDefinition workflow, string triggerType, bool previewAll = false)
    {
        var catalog = new InitialBlockCatalog();
        var triggers = workflow.Blocks.Where(block => catalog.Find(block.Type, block.Version)?.Kind == BlockKind.Trigger).ToArray();
        var reachable = triggers.Where(block => block.Type == triggerType || previewAll).Select(block => block.Id).ToHashSet();
        var pending = new Queue<Guid>(reachable);
        while (pending.TryDequeue(out var source))
            foreach (var edge in workflow.Connections.Where(edge => edge.FromBlockId == source))
                if (reachable.Add(edge.ToBlockId)) pending.Enqueue(edge.ToBlockId);
        return workflow with
        {
            Blocks = workflow.Blocks.Where(block => reachable.Contains(block.Id)).ToArray(),
            Connections = workflow.Connections.Where(edge => reachable.Contains(edge.FromBlockId) && reachable.Contains(edge.ToBlockId)).ToArray()
        };
    }
}
