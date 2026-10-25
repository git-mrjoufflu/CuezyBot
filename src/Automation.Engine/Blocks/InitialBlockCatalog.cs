using Automation.Contracts.Blocks;
using Automation.Domain.Workflows;

namespace Automation.Engine.Blocks;

public sealed class InitialBlockCatalog : IBlockCatalog
{
    private static readonly IReadOnlyCollection<BlockDescriptor> Blocks =
    [
        new(
            "twitch.follow.received",
            1,
            BlockCategory.Twitch,
            BlockKind.Trigger,
            "Nouveau follower",
            "Démarre le parcours lorsqu’une personne suit la chaîne.",
            new HashSet<string>(StringComparer.Ordinal) { "next" }),
        new(
            "core.condition",
            1,
            BlockCategory.Core,
            BlockKind.Condition,
            "Si / Sinon",
            "Continue sur la branche Oui ou Non selon une condition.",
            new HashSet<string>(StringComparer.Ordinal) { "yes", "no" }),
        new(
            "variables.increment",
            1,
            BlockCategory.Variables,
            BlockKind.Action,
            "Ajouter au compteur",
            "Ajoute une valeur à un compteur persistant.",
            new HashSet<string>(StringComparer.Ordinal) { "next" }),
        new(
            "obs.scene.set-current",
            1,
            BlockCategory.Obs,
            BlockKind.Action,
            "Changer de scène",
            "Sélectionne une scène dans OBS.",
            new HashSet<string>(StringComparer.Ordinal) { "next" })
    ];

    public IReadOnlyCollection<BlockDescriptor> GetAll() => Blocks;

    public BlockDescriptor? Find(string type, int version) =>
        Blocks.FirstOrDefault(block =>
            block.Type.Equals(type, StringComparison.Ordinal) && block.Version == version);
}
