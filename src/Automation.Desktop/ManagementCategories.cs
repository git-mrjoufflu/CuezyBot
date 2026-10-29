using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Automation.Desktop;

internal static class ManagementCategories
{
    internal static string Get(JsonObject config, string kind, string id) => config[kind]?[id]?.ToString() ?? "";
    private static readonly string[] Kinds = ["workflowCategories", "queueCategories", "commandCategories", "rewardCategories", "variableCategories"];
    internal static string[] Names(JsonObject config, string kind) => Kinds.SelectMany(key => (config[key] as JsonObject ?? new JsonObject()).Select(pair => pair.Value?.ToString() ?? ""))
        .Concat((config["categoryNames"] as JsonArray ?? new JsonArray()).Select(item => item?.ToString() ?? ""))
        .Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    internal static void RememberNames(JsonObject config)
    {
        config["categoryNames"] = new JsonArray(Names(config, "").Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
    }

    internal static async Task<bool> EditAsync(Window owner, bool english, string kind, string id)
    {
        string T(string fr, string en) => english ? en : fr;
        var config = await AutomationQueues.LoadAsync();
        var dialog = new Window { Title = T("Catégorie", "Category"), Width = 440, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#0B1024") };
        var input = new TextBox { Text = Get(config, kind, id), MaxLength = 48, PlaceholderText = T("ex. Alertes, Jeux, Modération…", "e.g. Alerts, Games, Moderation…") };
        var choices = new ComboBox { ItemsSource = new[] { T("Sans catégorie", "Uncategorized") }.Concat(Names(config, kind)).ToArray(), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        choices.SelectionChanged += (_, _) => { if (choices.SelectedIndex >= 0) input.Text = choices.SelectedIndex == 0 ? "" : choices.SelectedItem?.ToString(); };
        var save = new Button(); ManagementButtons.Save(save, english); var cancel = new Button { Content = T("Annuler", "Cancel") };
        save.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children =
        {
            new TextBlock { Text = T("Catégories existantes", "Existing categories"), Foreground = Brushes.White }, choices,
            new TextBlock { Text = T("Catégorie (vide pour retirer)", "Category (leave empty to remove)"), Foreground = Brushes.White }, input,
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, save } }
        }};
        if (!await dialog.ShowDialog<bool>(owner)) return false;
        await AutomationQueues.ChangeAsync(settings =>
        {
            settings[kind] ??= new JsonObject();
            var value = input.Text?.Trim() ?? "";
            var canonical = Names(settings, kind).FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) ?? value;
            if (canonical.Length == 0) settings[kind]!.AsObject().Remove(id);
            else settings[kind]![id] = canonical;
        });
        return true;
    }
}
