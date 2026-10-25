using Automation.Contracts.Blocks;
using Automation.Contracts.Workflows;
using Automation.Domain.Workflows;

namespace Automation.Engine.Workflows;

public sealed class WorkflowValidator(IBlockCatalog catalog) : IWorkflowValidator
{
    public WorkflowValidationResult Validate(WorkflowDefinition workflow)
    {
        var diagnostics = new List<WorkflowDiagnostic>();

        if (string.IsNullOrWhiteSpace(workflow.Name))
        {
            diagnostics.Add(new("workflow.name.required", "Donnez un nom à l’automatisation."));
        }

        if (workflow.Revision < 1)
        {
            diagnostics.Add(new("workflow.revision.invalid", "La révision doit être supérieure ou égale à 1."));
        }

        var duplicatedIds = workflow.Blocks
            .GroupBy(block => block.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var blockId in duplicatedIds)
        {
            diagnostics.Add(new("block.id.duplicate", "Deux cartes utilisent le même identifiant.", blockId));
        }

        var knownBlocks = new Dictionary<Guid, BlockDescriptor>();
        foreach (var block in workflow.Blocks)
        {
            var descriptor = catalog.Find(block.Type, block.Version);
            if (descriptor is null)
            {
                diagnostics.Add(new(
                    "block.type.unknown",
                    $"Le bloc « {block.Type} » version {block.Version} n’est pas disponible.",
                    block.Id));
                continue;
            }

            knownBlocks.TryAdd(block.Id, descriptor);
        }

        var triggers = knownBlocks.Where(pair => pair.Value.Kind == BlockKind.Trigger).ToArray();
        if (triggers.Length != 1)
        {
            diagnostics.Add(new(
                "workflow.trigger.count",
                "L’automatisation doit contenir exactement un déclencheur."));
        }

        ValidateConnections(workflow, knownBlocks, diagnostics);
        ValidateReachability(workflow, triggers, diagnostics);
        ValidateCycles(workflow, diagnostics);

        return new WorkflowValidationResult(diagnostics);
    }

    private static void ValidateCycles(WorkflowDefinition workflow, ICollection<WorkflowDiagnostic> diagnostics)
    {
        var edges = workflow.Connections.GroupBy(link => link.FromBlockId)
            .ToDictionary(group => group.Key, group => group.Select(link => link.ToBlockId).Distinct().ToArray());
        var states = new Dictionary<Guid, int>();
        bool Visit(Guid id)
        {
            if (states.TryGetValue(id, out var state)) return state == 1;
            states[id] = 1;
            if (edges.TryGetValue(id, out var targets))
                foreach (var target in targets)
                    if (Visit(target)) return true;
            states[id] = 2;
            return false;
        }
        if (workflow.Blocks.Any(block => Visit(block.Id)))
            diagnostics.Add(new("workflow.cycle", "Le parcours contient une boucle. Retire une connexion pour éviter une exécution sans fin."));
    }

    private static void ValidateConnections(
        WorkflowDefinition workflow,
        IReadOnlyDictionary<Guid, BlockDescriptor> knownBlocks,
        ICollection<WorkflowDiagnostic> diagnostics)
    {
        var blockIds = workflow.Blocks.Select(block => block.Id).ToHashSet();

        foreach (var connection in workflow.Connections)
        {
            if (!blockIds.Contains(connection.FromBlockId))
            {
                diagnostics.Add(new(
                    "connection.source.missing",
                    "La carte de départ de cette connexion n’existe plus.",
                    connection.FromBlockId));
                continue;
            }

            if (!blockIds.Contains(connection.ToBlockId))
            {
                diagnostics.Add(new(
                    "connection.target.missing",
                    "La carte d’arrivée de cette connexion n’existe plus.",
                    connection.ToBlockId));
            }

            if (knownBlocks.TryGetValue(connection.FromBlockId, out var descriptor) &&
                !descriptor.Outputs.Contains(connection.Output))
            {
                diagnostics.Add(new(
                    "connection.output.invalid",
                    $"La sortie « {connection.Output} » n’existe pas sur « {descriptor.DisplayName} ».",
                    connection.FromBlockId));
            }
        }

        foreach (var duplicate in workflow.Connections.GroupBy(connection =>
                     (connection.FromBlockId, connection.Output, connection.ToBlockId)).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new(
                "connection.duplicate",
                "Cette connexion est présente plusieurs fois.",
                duplicate.Key.FromBlockId));
        }
    }

    private static void ValidateReachability(
        WorkflowDefinition workflow,
        IReadOnlyCollection<KeyValuePair<Guid, BlockDescriptor>> triggers,
        ICollection<WorkflowDiagnostic> diagnostics)
    {
        if (triggers.Count != 1)
        {
            return;
        }

        var reachable = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(triggers.Single().Key);

        while (pending.TryPop(out var blockId))
        {
            if (!reachable.Add(blockId))
            {
                continue;
            }

            foreach (var next in workflow.Connections
                         .Where(connection => connection.FromBlockId == blockId)
                         .Select(connection => connection.ToBlockId))
            {
                pending.Push(next);
            }
        }

        foreach (var block in workflow.Blocks.Where(block => !reachable.Contains(block.Id)))
        {
            diagnostics.Add(new(
                "block.unreachable",
                "Cette carte n’est reliée à aucun chemin partant du déclencheur.",
                block.Id));
        }
    }
}
