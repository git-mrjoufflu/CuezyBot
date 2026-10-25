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
            var rows = entries.Select(ToRow).ToArray();
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
            english ? $"New follower: {userName}" : $"Nouveau follower : {userName}",
            english ? $"{entry.EventReceivedAt.ToLocalTime():g} · {entry.Steps.Count} step(s)" : $"{entry.EventReceivedAt.ToLocalTime():g} · {entry.Steps.Count} étape(s)",
            ToStatus(entry.Status, english),
            userName,
            $"@{userLogin} · ID {userId}",
            english ? $"Received {entry.EventReceivedAt.ToLocalTime():f}" : $"Reçu le {entry.EventReceivedAt.ToLocalTime():f}",
            duration is null ? (english ? "Unknown duration" : "Durée inconnue") : (english ? $"Total duration: {FormatDuration(duration.Value)}" : $"Durée totale : {FormatDuration(duration.Value)}"),
            english ? $"Execution: {entry.Id:D}" : $"Exécution : {entry.Id:D}",
            english ? $"EventSub message: {entry.ProviderEventId}" : $"Message EventSub : {entry.ProviderEventId}",
            entry.Steps.Select(step => new StepRow(
                TranslateStepMessage(step.Message, english),
                FormatDuration(step.CompletedAt - step.StartedAt))).ToArray(),
            entry.Error ?? string.Empty,
            !string.IsNullOrWhiteSpace(entry.Error));
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds >= 1
        ? $"{duration.TotalSeconds:0.00} s"
        : $"{duration.TotalMilliseconds:0} ms";

    private static string TranslateStepMessage(string message, bool english)
    {
        if (!english)
        {
            return message;
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
        bool HasError);

    private sealed record StepRow(string Message, string Duration);
}
