using System.Text.Json.Nodes;
using Automation.Domain.Execution;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Automation.Desktop;

public sealed partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        if (UiLanguageState.IsEnglish)
        {
            Title = "History — CuezyBot";
            HistoryTitle.Text = "Event history";
            HistoryDescription.Text = "Open an alert to inspect its event and every executed step.";
            RefreshButton.Content = "Refresh";
        }
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs) => await LoadHistoryAsync();

    private async void OnRefreshClick(object? sender, RoutedEventArgs eventArgs) => await LoadHistoryAsync();

    private async Task LoadHistoryAsync()
    {
        try
        {
            HistoryStatus.Text = UiLanguageState.IsEnglish ? "Loading history…" : "Chargement de l’historique…";
            var database = new SqliteDatabase(new AppDataPaths());
            await database.InitializeAsync();
            var entries = await new SqliteExecutionHistoryReader(database).GetRecentAsync(50);
            var rows = entries.Reverse().Select(ToRow).ToArray();
            HistoryItems.ItemsSource = rows;
            ApplyStaticLanguage();
            HistoryStatus.Text = rows.Length == 0
                ? (UiLanguageState.IsEnglish ? "No events recorded yet." : "Aucun événement enregistré pour le moment.")
                : (UiLanguageState.IsEnglish
                    ? $"{rows.Length} recent event(s). Click an alert to open it."
                    : $"{rows.Length} événement(s) récent(s). Clique sur une alerte pour l’ouvrir.");
        }
        catch (Exception exception)
        {
            HistoryStatus.Text = $"Impossible de lire l’historique : {exception.Message}";
        }
    }

    private void ApplyStaticLanguage()
    {
        var labels = UiLanguageState.IsEnglish
            ? new Dictionary<string, string>
            {
                ["ÉVÉNEMENT TWITCH"] = "TWITCH EVENT",
                ["EXÉCUTION"] = "EXECUTION",
                ["ÉTAPES DU PARCOURS"] = "WORKFLOW STEPS"
            }
            : new Dictionary<string, string>
            {
                ["TWITCH EVENT"] = "ÉVÉNEMENT TWITCH",
                ["EXECUTION"] = "EXÉCUTION",
                ["WORKFLOW STEPS"] = "ÉTAPES DU PARCOURS"
            };

        foreach (var textBlock in this.GetVisualDescendants().OfType<TextBlock>())
        {
            if (textBlock.Text is { } text && labels.TryGetValue(text, out var translated))
            {
                textBlock.Text = translated;
            }
        }
    }

    private static HistoryRow ToRow(ExecutionHistoryEntry entry)
    {
        var payload = JsonNode.Parse(entry.EventPayloadJson)?.AsObject();
        var userName = payload?["userName"]?.GetValue<string>() ?? "Follower inconnu";
        var userLogin = payload?["userLogin"]?.GetValue<string>() ?? "inconnu";
        var userId = payload?["userId"]?.GetValue<string>() ?? "inconnu";
        var duration = entry.StartedAt is not null && entry.CompletedAt is not null
            ? entry.CompletedAt.Value - entry.StartedAt.Value
            : (TimeSpan?)null;

        var english = UiLanguageState.IsEnglish;
        return new HistoryRow(
            entry.EventType == "manual.run"
                ? (english ? $"Manual run: {userName}" : $"Exécution manuelle : {userName}")
                : $"{(Automation.Connectors.Twitch.TwitchTriggerCatalog.All.FirstOrDefault(item => item.EventType == entry.EventType) is { } trigger ? (english ? trigger.English : trigger.French) : entry.EventType)}: {userName}",
            english ? $"{entry.EventReceivedAt.ToLocalTime():g} · {entry.Steps.Count} step(s)" : $"{entry.EventReceivedAt.ToLocalTime():g} · {entry.Steps.Count} étape(s)",
            ToStatus(entry.Status, english),
            userName,
            entry.EventType == "manual.run" ? (english ? "Manual activation" : "Déclenchement manuel") : $"@{userLogin} · ID {userId}",
            english ? $"Received {entry.EventReceivedAt.ToLocalTime():f}" : $"Reçu le {entry.EventReceivedAt.ToLocalTime():f}",
            duration is null ? (english ? "Unknown duration" : "Durée inconnue") : (english ? $"Total duration: {FormatDuration(duration.Value)}" : $"Durée totale : {FormatDuration(duration.Value)}"),
            english ? $"Execution: {entry.Id:D}" : $"Exécution : {entry.Id:D}",
            english ? $"EventSub message: {entry.ProviderEventId}" : $"Message EventSub : {entry.ProviderEventId}",
            entry.Steps.Select(step => new StepRow(
                TranslateStepMessage(step.Message, english),
                FormatDuration(step.CompletedAt - step.StartedAt),
                step.Status == ExecutionStatus.Succeeded ? "✓" : step.Status == ExecutionStatus.Cancelled ? "■" : "✕",
                step.Status == ExecutionStatus.Succeeded ? "#53D994" : "#FF7587")).ToArray(),
            TranslateStepMessage(entry.Error ?? string.Empty, english),
            !string.IsNullOrWhiteSpace(entry.Error),
            english ? "EVENT" : "ÉVÉNEMENT",
            english ? "EXECUTION" : "EXÉCUTION",
            english ? "WORKFLOW STEPS" : "ÉTAPES DU PARCOURS",
            entry.Status == ExecutionStatus.Succeeded ? "#174B38" : entry.Status == ExecutionStatus.Failed ? "#331A25" : "#3A3020",
            entry.Status == ExecutionStatus.Succeeded ? "#72F0AA" : entry.Status == ExecutionStatus.Failed ? "#FF7587" : "#FFD081");
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds >= 1
        ? $"{duration.TotalSeconds:0.00} s"
        : $"{duration.TotalMilliseconds:0} ms";

    private static string TranslateStepMessage(string message, bool english)
    {
        if (!english)
        {
            return message.Replace("Follower event received", "Événement follower reçu", StringComparison.Ordinal)
                .Replace("Manual run", "Exécution manuelle", StringComparison.Ordinal)
                .Replace("Twitch title updated", "Titre Twitch modifié", StringComparison.Ordinal)
                .Replace("Twitch message sent", "Message Twitch envoyé", StringComparison.Ordinal)
                .Replace("Twitch user:", "Utilisateur Twitch :", StringComparison.Ordinal)
                .Replace("Twitch clip:", "Clip Twitch :", StringComparison.Ordinal)
                .Replace("Audio played", "Son joué", StringComparison.Ordinal)
                .Replace("OBS disconnected", "OBS déconnecté", StringComparison.Ordinal)
                .Replace("Twitch disconnected", "Twitch déconnecté", StringComparison.Ordinal)
                .Replace("Missing parameter:", "Paramètre manquant :", StringComparison.Ordinal)
                .Replace("Variable not found:", "Variable introuvable :", StringComparison.Ordinal)
                .Replace("Select at least one OBS source", "Coche au moins une source OBS", StringComparison.Ordinal)
                .Replace("OBS screenshot:", "Capture OBS :", StringComparison.Ordinal)
                .Replace("Previous session ended before completion. Not replayed automatically; external effects may already have occurred.", "La session précédente s’est arrêtée avant la fin. Aucun rejeu automatique ; des effets externes ont peut-être déjà été appliqués.", StringComparison.Ordinal)
                .Replace("OBS replay buffer started", "Tampon de replay OBS démarré", StringComparison.Ordinal)
                .Replace("OBS scene:", "Scène OBS :", StringComparison.Ordinal)
                .Replace("Wait:", "Attente :", StringComparison.Ordinal)
                .Replace("source(s) shown", "source(s) affichées", StringComparison.Ordinal)
                .Replace("source(s) hidden", "source(s) masquées", StringComparison.Ordinal);
        }

        if (message.StartsWith("Compteur followersTraites", StringComparison.OrdinalIgnoreCase))
        {
            return message.Replace("Compteur followersTraites", "Followers processed counter", StringComparison.OrdinalIgnoreCase);
        }

        if (message.StartsWith("Scène OBS", StringComparison.OrdinalIgnoreCase))
        {
            return message.Replace("Scène OBS", "OBS scene", StringComparison.OrdinalIgnoreCase)
                .Replace("sélectionnée", "selected", StringComparison.OrdinalIgnoreCase);
        }

        return message;
    }

    private static string ToStatus(ExecutionStatus status, bool english) => status switch
    {
        ExecutionStatus.Succeeded => english ? "Succeeded" : "Réussie",
        ExecutionStatus.Failed => english ? "Failed" : "Échouée",
        ExecutionStatus.Running => english ? "Running" : "En cours",
        ExecutionStatus.Pending => english ? "Pending" : "En attente",
        ExecutionStatus.Cancelled => english ? "Cancelled" : "Annulée",
        ExecutionStatus.Interrupted => english ? "Interrupted" : "Interrompue",
        ExecutionStatus.UnknownOutcome => english ? "Outcome uncertain" : "Résultat incertain",
        ExecutionStatus.Expired => english ? "Expired" : "Expirée",
        ExecutionStatus.Skipped => english ? "Skipped" : "Ignorée",
        _ => status.ToString()
    };

    private sealed record HistoryRow(
        string Title,
        string Summary,
        string StatusLabel,
        string FollowerName,
        string FollowerDetails,
        string ReceivedLabel,
        string DurationLabel,
        string ExecutionIdLabel,
        string MessageIdLabel,
        IReadOnlyList<StepRow> Steps,
        string Error,
        bool HasError,
        string EventHeading,
        string ExecutionHeading,
        string StepsHeading,
        string StatusBackground,
        string StatusColor);

    private sealed record StepRow(string Message, string Duration, string Icon, string Color);
}
