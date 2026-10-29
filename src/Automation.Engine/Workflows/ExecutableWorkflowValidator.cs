using System.Globalization;
using Automation.Engine.Variables;
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
                case "twitch.channel.chat.message.received":
                    Required("command", "Indique une commande, par exemple !test.", "Enter a command, for example !test.");
                    if (block.Parameters["cooldownSeconds"] is { } cooldown && (!double.TryParse(cooldown.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var cooldownValue) || cooldownValue < 0 || cooldownValue > 86400))
                        Error("command.cooldown.invalid", "Le cooldown doit être compris entre 0 et 86400 secondes.", "Cooldown must be between 0 and 86400 seconds.");
                    break;
                case "twitch.create-reward":
                    Required("title", "Indique le nom de la récompense.", "Enter the reward title.");
                    Required("cost", "Indique le coût en points.", "Enter the reward cost.");
                    break;
                case "manual.run": break;
                case "core.timer":
                    if (!double.TryParse(block.Parameters["seconds"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var timerSeconds) || !double.IsFinite(timerSeconds) || timerSeconds < 1 || timerSeconds > 86400)
                        Error("timer.invalid", "L’intervalle doit être compris entre 1 et 86400 secondes.", "Interval must be between 1 and 86400 seconds.");
                    break;
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
                case "obs.output.record":
                case "obs.output.replay":
                case "obs.output.virtual-camera":
                    var operation = block.Parameters["operation"]?.ToString() ?? "start";
                    var allowed = block.Type == "obs.output.record" ? new[] { "start", "stop", "pause", "resume" }
                        : block.Type == "obs.output.replay" ? new[] { "start", "stop", "save" } : new[] { "start", "stop" };
                    if (!allowed.Contains(operation)) Error("obs.operation.invalid", "Choisis une opération OBS valide.", "Choose a valid OBS operation.");
                    break;
                case "audio.play":
                    if (block.Parameters["file"]?.ToString() is not { Length: > 0 } file || !File.Exists(file))
                        Error("audio.file.missing", "Choisis un fichier audio existant.", "Choose an existing audio file.");
                    else if (!file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                        Error("audio.file.format", "Le fichier doit être au format MP3 ou WAV.", "The file must be MP3 or WAV.");
                    break;
                case "twitch.create-clip": break;
                case "twitch.get-user": break;
                case "twitch.send-message":
                    var message = block.Parameters["message"]?.ToString();
                    if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
                        Error("message.invalid", "Le message doit contenir de 1 à 500 caractères.", "The message must contain 1–500 characters.");
                    break;
                case "variables.set-integer":
                case "variables.increment":
                    Required("name", "Indique le nom de la variable.", "Enter a variable name.");
                    if (!long.TryParse(block.Parameters["value"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        Error("variable.value.invalid", "La valeur doit être un nombre entier.", "The value must be an integer.");
                    break;
                case "core.condition":
                    Required("name", "Indique le nom de la variable.", "Enter a variable name.");
                    if ((block.Parameters["comparison"]?.ToString() ?? "equals") != "equals" && !long.TryParse(block.Parameters["value"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        Error("condition.value.invalid", "Cette comparaison exige un nombre entier.", "This comparison requires an integer.");
                    break;
                case "variables.set-text":
                    Required("name", "Indique le nom de la variable.", "Enter a variable name.");
                    if (block.Parameters["value"] is null)
                        Error("variable.value.invalid", "Indique le texte de la variable.", "Enter the variable text.");
                    break;
                case "variables.number-expression":
                    Required("name", "Indique le nom de la variable.", "Enter a variable name.");
                    if (!NumericVariableExpression.TryApply(block.Parameters["value"]?.ToString(), 10, out _))
                        Error("variable.expression.invalid", "Utilise une valeur, +5, -2, x2 ou /2.", "Use a value, +5, -2, x2 or /2.");
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
        if (workflow.Blocks.All(block => block.Type is "twitch.follow.received" or "manual.run" or "core.timer"))
            errors.Add(new("workflow.empty", english ? "Add at least one action." : "Ajoute au moins une action."));
        return errors;
    }
}
