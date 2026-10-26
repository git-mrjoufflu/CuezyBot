using System.Globalization;
using Automation.Contracts.Workflows;
using Automation.Domain.Workflows;

namespace Automation.Engine.Workflows;

public static class ExecutableWorkflowValidator
{
    public static IReadOnlyList<WorkflowDiagnostic> Validate(WorkflowDefinition workflow, bool english = false)
    {
        var errors = new List<WorkflowDiagnostic>();
        foreach (var block in workflow.Blocks)
        {
            void Error(string code, string fr, string en) => errors.Add(new(code, english ? en : fr, block.Id));
            void Required(string key, string fr, string en)
            {
                if (string.IsNullOrWhiteSpace(block.Parameters[key]?.ToString())) Error("parameter.required", fr, en);
            }
            switch (block.Type)
            {
                case "twitch.follow.received": break;
                case "twitch.channel.subscribe.received":
                case "twitch.channel.subscription.message.received":
                case "twitch.channel.subscription.gift.received":
                case "twitch.channel.cheer.received":
                case "twitch.channel.raid.received":
                case "twitch.channel.channel_points_custom_reward_redemption.add.received":
                case "twitch.stream.online.received":
                case "twitch.stream.offline.received":
                case "twitch.channel.update.received": break;
                case "manual.run": break;
                case "obs.scene.set-current":
                case "obs.screenshot":
                    Required("scene", "Choisis une scène OBS.", "Choose an OBS scene.");
                    break;
                case "obs.set-source-visible":
                    Required("scene", "Choisis une scène OBS.", "Choose an OBS scene.");
                    if (block.Parameters["sources"] is not System.Text.Json.Nodes.JsonArray { Count: > 0 } && string.IsNullOrWhiteSpace(block.Parameters["source"]?.ToString()))
                        Error("sources.required", "Coche au moins une source OBS.", "Select at least one OBS source.");
                    break;
                case "obs.replay.start": break;
                case "audio.play":
                    if (block.Parameters["file"]?.ToString() is not { Length: > 0 } file || !File.Exists(file))
                        Error("audio.file.missing", "Choisis un fichier audio existant.", "Choose an existing audio file.");
                    break;
                case "twitch.create-clip": break;
                case "twitch.get-user":
                    Required("login", "Indique un utilisateur Twitch.", "Enter a Twitch login.");
                    break;
                case "twitch.send-message":
                    var message = block.Parameters["message"]?.ToString();
                    if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
                        Error("message.invalid", "Le message doit contenir de 1 à 500 caractères.", "The message must contain 1–500 characters.");
                    break;
                case "variables.set-integer":
                case "variables.increment":
                case "core.condition":
                    Required("name", "Indique le nom de la variable.", "Enter a variable name.");
                    if (!long.TryParse(block.Parameters["value"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        Error("variable.value.invalid", "La valeur doit être un nombre entier.", "The value must be an integer.");
                    break;
                case "twitch.update-title":
                    var title = block.Parameters["title"]?.ToString();
                    if (string.IsNullOrWhiteSpace(title) || title.Length > 140)
                        Error("title.invalid", "Le titre Twitch doit contenir de 1 à 140 caractères.", "The Twitch title must contain 1–140 characters.");
                    break;
                case "core.delay":
                    if (!double.TryParse(block.Parameters["seconds"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > 3600)
                        Error("delay.invalid", "La durée doit être comprise entre 0 et 3600 secondes.", "Duration must be between 0 and 3600 seconds.");
                    break;
                default:
                    Error("action.not-supported", $"L’action {block.Type} n’est pas encore exécutable.", $"Action {block.Type} is not executable yet.");
                    break;
            }
        }
        if (workflow.Blocks.All(block => block.Type is "twitch.follow.received" or "manual.run"))
            errors.Add(new("workflow.empty", english ? "Add at least one action." : "Ajoute au moins une action."));
        return errors;
    }
}
