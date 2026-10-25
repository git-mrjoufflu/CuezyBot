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
        ,new("twitch.create-clip", 1, BlockCategory.Twitch, BlockKind.Action, "Créer un clip", "Crée un clip Twitch.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("twitch.update-title", 1, BlockCategory.Twitch, BlockKind.Action, "Modifier un titre", "Modifie le titre du stream.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("twitch.send-message", 1, BlockCategory.Twitch, BlockKind.Action, "Envoyer un message", "Envoie un message Twitch.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("twitch.get-user", 1, BlockCategory.Twitch, BlockKind.Action, "Récupérer un utilisateur", "Lit les informations publiques d’un utilisateur Twitch.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("obs.screenshot", 1, BlockCategory.Obs, BlockKind.Action, "Prendre une capture", "Capture la sortie OBS.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("obs.replay.start", 1, BlockCategory.Obs, BlockKind.Action, "Démarrer un replay", "Démarre le tampon de replay OBS.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("obs.set-source-visible", 1, BlockCategory.Obs, BlockKind.Action, "Afficher une source", "Modifie la visibilité d’une source OBS.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("core.delay", 1, BlockCategory.Core, BlockKind.Action, "Attendre", "Attend avant de poursuivre.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("core.action", 1, BlockCategory.Core, BlockKind.Action, "Action", "Exécute une action générique.", new HashSet<string>(StringComparer.Ordinal) { "next" })
        ,new("variables.set-integer", 1, BlockCategory.Core, BlockKind.Action, "Modifier une variable", "Définit une variable entière persistante.", new HashSet<string>(StringComparer.Ordinal) { "next" })
    ];

    public IReadOnlyCollection<BlockDescriptor> GetAll() => Blocks;

    public BlockDescriptor? Find(string type, int version) =>
        Blocks.FirstOrDefault(block =>
            block.Type.Equals(type, StringComparison.Ordinal) && block.Version == version);
}
