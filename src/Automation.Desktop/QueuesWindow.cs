using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System.Text.Json.Nodes;

namespace Automation.Desktop;

public sealed class QueuesWindow : Window
{
    public QueuesWindow(bool english, IReadOnlyDictionary<string, Guid>? workflows = null)
    {
        string T(string fr, string en) => english ? en : fr;
        Title = T("Files d’alertes — CuezyBot", "Alert queues — CuezyBot"); Width = 1000; Height = 700; Background = Brush.Parse("#0B1024"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 900; MinHeight = 520;
        Styles.Add(new Style(selector => selector.OfType<TextBlock>()) { Setters = { new Setter(TextBlock.ForegroundProperty, Brush.Parse("#F4F3FF")), new Setter(TextBlock.FontFamilyProperty, new FontFamily("Inter")) } });
        Styles.Add(new Style(selector => selector.OfType<Button>()) { Setters = { new Setter(Button.CornerRadiusProperty, new CornerRadius(7)), new Setter(Button.PaddingProperty, new Thickness(12, 8)) } });
        Styles.Add(new Style(selector => selector.OfType<Button>().Class("primary")) { Setters = { new Setter(Button.BackgroundProperty, Brush.Parse("#245A91")), new Setter(Button.ForegroundProperty, Brushes.White) } });
        Styles.Add(new Style(selector => selector.OfType<TextBox>()) { Setters = { new Setter(TextBox.HeightProperty, 38d), new Setter(TextBox.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center) } });
        Styles.Add(new Style(selector => selector.OfType<ComboBox>()) { Setters = { new Setter(ComboBox.HeightProperty, 38d), new Setter(ComboBox.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center) } });
        var panel = new StackPanel { Margin = new Thickness(28), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = T("Files d’alertes", "Alert queues"), FontSize = 25, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
        var status = new TextBlock { Foreground = Brush.Parse("#9DA8CB"), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var name = new TextBox { PlaceholderText = T("ex. Sons", "e.g. Sounds") }; var add = new Button { Classes = { "primary" } }; ManagementButtons.Add(add, english);
        var categoryInput = new AutoCompleteBox { PlaceholderText = T("Sans catégorie", "Uncategorized"), MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains, Height = 38 };
        var stateInput = new ComboBox { ItemsSource = new[] { T("Prête", "Ready"), T("En pause", "Paused") }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var cancel = new Button { Content = T("Annuler", "Cancel"), IsVisible = false };
        var intervalInput = new TextBox { Text = "0" };
        var blockingInput = new ComboBox { ItemsSource = new[] { T("Une à la fois", "One at a time"), T("En parallèle", "In parallel") }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        blockingInput.SelectionChanged += (_, _) => intervalInput.IsEnabled = blockingInput.SelectedIndex == 0;
        ToolTip.SetTip(blockingInput, T("Une à la fois : attend la fin de l’alerte précédente. En parallèle : plusieurs alertes simultanées.", "One at a time: waits for the previous alert to finish. In parallel: alerts can run simultaneously."));
        string? editingName = null;
        void Reset() { editingName = null; name.Text = ""; categoryInput.Text = ""; intervalInput.Text = "0"; stateInput.SelectedIndex = blockingInput.SelectedIndex = 0; ManagementButtons.Add(add, english); cancel.IsVisible = false; name.IsReadOnly = false; }
        cancel.Click += (_, _) => Reset();
        Width = 1150;
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,1.3*,1*,1*,1.2*,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 12, RowSpacing = 7 };
        var fields = new Control[] { name, categoryInput, stateInput, intervalInput, blockingInput, add, cancel };
        var labels = new[] { T("Nom", "Name"), T("Catégorie", "Category"), T("État", "Status"), T("Délai (s)", "Gap (s)"), T("Mode", "Mode") };
        for (var i = 0; i < fields.Length; i++) { if (i < labels.Length) { var label = new TextBlock { Text = labels[i], FontWeight = FontWeight.SemiBold }; Grid.SetColumn(label, i); form.Children.Add(label); } Grid.SetColumn(fields[i], i); Grid.SetRow(fields[i], 1); form.Children.Add(fields[i]); }
        panel.Children.Add(new Border { Background = Brush.Parse("#141A34"), BorderBrush = Brush.Parse("#33406A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(16), Child = form });
        var rows = new StackPanel { Spacing = 8 };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(panel); var scroll = new ScrollViewer { Content = rows, Margin = new Thickness(28, 0, 28, 28) }; Grid.SetRow(scroll, 1); layout.Children.Add(scroll); Content = layout;
        var refreshVersion = 0;
        var collapsedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task Refresh()
        {
            var version = ++refreshVersion;
            var config = await AutomationQueues.LoadAsync();
            categoryInput.ItemsSource = ManagementCategories.Names(config, "queueCategories");
            if (version != refreshVersion) return;
            rows.Children.Clear();
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,1*,.7*,1.5*,85,40,40"), ColumnSpacing = 8, Margin = new Thickness(14, 0) };
            var headings = new[] { T("File", "Queue"), T("État", "Status"), T("En attente", "Waiting"), T("Mode d’exécution", "Execution mode"), "Actions" };
            for (var i = 0; i < headings.Length; i++) { var text = new TextBlock { Text = headings[i], Foreground = Brush.Parse("#9DA8CB") }; Grid.SetColumn(text, i); header.Children.Add(text); } rows.Children.Add(header);
            var groups = new CategoryGroups(rows, collapsedGroups, T("Sans catégorie", "Uncategorized"));
            foreach (var item in config["names"]!.AsArray().OrderBy(item => ManagementCategories.Get(config, "queueCategories", item!.ToString()), StringComparer.OrdinalIgnoreCase).ThenBy(item => item!.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                var queueName = item!.ToString(); var queue = AutomationQueues.Get(queueName);
                var categoryName = ManagementCategories.Get(config, "queueCategories", queueName);
                var pause = new Button { Content = queue.IsPaused ? T("Reprendre", "Resume") : T("Pause", "Pause") };
                pause.Click += async (_, _) => { queue.SetPaused(!queue.IsPaused); await Refresh(); };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,1*,.7*,1.5*,85,40,40"), ColumnSpacing = 8 };
                var blocking = config["queueOptions"]?[queueName]?["blocking"]?.GetValue<bool>() ?? true;
                var values = new[] { queueName, queue.IsPaused ? T("En pause", "Paused") : queue.IsRunning ? T("En cours", "Running") : T("Prête", "Ready"), queue.PendingCount.ToString(), blocking ? T("Une à la fois", "One at a time") : T("En parallèle", "In parallel") };
                for (var i = 0; i < values.Length; i++) { var text = new TextBlock { Text = values[i], Foreground = Brushes.White, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(text, i); row.Children.Add(text); }
                Grid.SetColumn(pause, 4); row.Children.Add(pause);
                var edit = ManagementButtons.Edit(english);
                edit.Click += (_, _) => { editingName = queueName; name.Text = queueName; name.IsReadOnly = queueName == "Default"; categoryInput.Text = categoryName; intervalInput.Text = config["queueOptions"]?[queueName]?["intervalSeconds"]?.ToString() ?? "0"; blockingInput.SelectedIndex = config["queueOptions"]?[queueName]?["blocking"]?.GetValue<bool>() == false ? 1 : 0; stateInput.SelectedIndex = queue.IsPaused ? 1 : 0; ManagementButtons.Save(add, english); cancel.IsVisible = true; name.Focus(); };
                var delete = ManagementButtons.Delete(english); delete.IsEnabled = queueName != "Default";
                delete.Click += async (_, _) =>
                {
                    var confirmation = new Window { Title = T("Supprimer la file", "Delete queue"), Width = 440, Height = 200, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
                    var yes = new Button { Content = T("Supprimer", "Delete") }; var no = new Button { Content = T("Annuler", "Cancel") };
                    yes.Click += (_, _) => confirmation.Close(true); no.Click += (_, _) => confirmation.Close(false);
                    confirmation.Content = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { new TextBlock { Text = T($"Supprimer « {queueName} » ? Ses automatisations seront affectées à Default.", $"Delete “{queueName}”? Its automations will move to Default."), TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { no, yes } } } };
                    if (!await confirmation.ShowDialog<bool>(this)) return;
                    try { await AutomationQueues.DeleteQueueAsync(queueName, english); if (editingName == queueName) Reset(); await Refresh(); } catch (Exception ex) { status.Text = ex.Message; }
                };
                Grid.SetColumn(edit, 5); row.Children.Add(edit); Grid.SetColumn(delete, 6); row.Children.Add(delete);
                groups.Add(categoryName, new Border { Padding = new Thickness(14, 10), Background = Brush.Parse("#141A34"), BorderBrush = Brush.Parse("#33406A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = row });
            }
            status.Text = T("Une à la fois : attend la fin de l’alerte précédente. En parallèle : autorise les alertes simultanées. Pause : les suivantes attendent.", "One at a time: waits for the previous alert. In parallel: allows concurrent alerts. Pause: upcoming alerts wait.");
        }
        add.Click += async (_, _) =>
        {
            try
            {
                var value = name.Text?.Trim() ?? "";
                if (value.Length == 0 || value.Length > 48) throw new InvalidOperationException(T("Utilise un nom de 1 à 48 caractères.", "Use a name of 1–48 characters."));
                add.IsEnabled = false;
                var interval = 0;
                if (blockingInput.SelectedIndex == 0 && !int.TryParse(intervalInput.Text, out interval)) throw new InvalidOperationException(T("Indique un délai en secondes de 0 à 3600.", "Enter an interval from 0 to 3600 seconds."));
                await AutomationQueues.SaveQueueAsync(editingName, value, categoryInput.Text?.Trim() ?? "", english, interval, blockingInput.SelectedIndex == 0);
                AutomationQueues.Get(value).SetPaused(stateInput.SelectedIndex == 1);
                Reset(); await Refresh();
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { add.IsEnabled = true; }
        };
        Opened += async (_, _) => { try { await Refresh(); } catch (Exception ex) { status.Text = ex.Message; } };
    }
}
