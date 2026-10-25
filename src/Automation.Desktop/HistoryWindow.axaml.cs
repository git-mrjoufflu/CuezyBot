using System.Text.Json.Nodes;
using Automation.Domain.Execution;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;
using Avalonia.Controls;
using Avalonia.Interactivity;

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
            HistoryStatus.Text = rows.Length == 0
                ? "Aucun événement enregistré pour le moment."
                : $"{rows.Length} événement(s) récent(s). Clique sur une alerte pour l’ouvrir.";
        }
        catch (Exception exception)
        {
            HistoryStatus.Text = $"Impossible de lire l’historique : {exception.Message}";
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

        return new HistoryRow(
            $"Nouveau follower : {userName}",
            $"{entry.EventReceivedAt.ToLocalTime():g} · {entry.Steps.Count} étape(s)",
            ToFrenchStatus(entry.Status),
            userName,
            $"@{userLogin} · ID {userId}",
            $"Reçu le {entry.EventReceivedAt.ToLocalTime():f}",
            duration is null ? "Durée inconnue" : $"Durée totale : {FormatDuration(duration.Value)}",
            $"Exécution : {entry.Id:D}",
            $"Message EventSub : {entry.ProviderEventId}",
            entry.Steps.Select(step => new StepRow(
                step.Message,
                FormatDuration(step.CompletedAt - step.StartedAt))).ToArray(),
            entry.Error ?? string.Empty,
            !string.IsNullOrWhiteSpace(entry.Error));
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds >= 1
        ? $"{duration.TotalSeconds:0.00} s"
        : $"{duration.TotalMilliseconds:0} ms";

    private static string ToFrenchStatus(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Succeeded => "Réussie",
        ExecutionStatus.Failed => "Échouée",
        ExecutionStatus.Running => "En cours",
        ExecutionStatus.Pending => "En attente",
        ExecutionStatus.Cancelled => "Annulée",
        ExecutionStatus.Interrupted => "Interrompue",
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
