using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Automation.Desktop;

public sealed class CommandsWindow : Window
{
    public CommandsWindow(bool english)
    {
        string T(string fr, string en) => english ? en : fr;
        Title = T("Commandes — CuezyBot", "Commands — CuezyBot"); Width = 1040; Height = 680; MinWidth = 900; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush.Parse("#0B1024");
        Styles.Add(new Style(selector => selector.OfType<TextBlock>()) { Setters = { new Setter(TextBlock.ForegroundProperty, Brush.Parse("#F4F3FF")), new Setter(TextBlock.FontFamilyProperty, new FontFamily("Inter")) } });
        Styles.Add(new Style(selector => selector.OfType<Button>()) { Setters = { new Setter(Button.FontFamilyProperty, new FontFamily("Inter")), new Setter(Button.CornerRadiusProperty, new CornerRadius(7)), new Setter(Button.PaddingProperty, new Thickness(12, 8)) } });
        Styles.Add(new Style(selector => selector.OfType<Button>().Class("primary")) { Setters = { new Setter(Button.BackgroundProperty, Brush.Parse("#245A91")), new Setter(Button.ForegroundProperty, Brushes.White) } });
        Styles.Add(new Style(selector => selector.OfType<TextBox>()) { Setters = { new Setter(TextBox.FontFamilyProperty, new FontFamily("Inter")), new Setter(TextBox.HeightProperty, 38d), new Setter(TextBox.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center) } });
        Styles.Add(new Style(selector => selector.OfType<ComboBox>()) { Setters = { new Setter(ComboBox.HeightProperty, 38d), new Setter(ComboBox.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center) } });
        var root = new Grid { Margin = new Thickness(28), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"), RowSpacing = 18 };
        root.Children.Add(new StackPanel { Children = { new TextBlock { Text = T("Commandes", "Commands"), FontSize = 25, FontWeight = FontWeight.Bold }, new TextBlock { Text = T("Gère les commandes Twitch utilisées par tes événements.", "Manage Twitch commands used by your events."), Foreground = Brush.Parse("#9DA8CB") } } });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#9DA8CB") }; Grid.SetRow(status, 1); root.Children.Add(status);
        var name = new TextBox { PlaceholderText = "!test" }; var seconds = new TextBox { Text = "0" };
        var scope = new ComboBox { ItemsSource = new[] { "Global", "Viewer" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var permission = new ComboBox { ItemsSource = english ? new[] { "Everyone", "Moderators", "VIP", "Viewer" } : new[] { "Tout le monde", "Modérateurs", "VIP", "Viewer" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var viewer = new TextBox();
        var save = new Button { Classes = { "primary" } }; ManagementButtons.Add(save, english); var cancel = new Button { Content = T("Annuler", "Cancel"), IsVisible = false };
        string? editing = null;
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("1.2*,.8*,1*,1.2*,1*,Auto"), ColumnSpacing = 12, RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 7 };
        var controls = new Control[] { name, seconds, scope, permission, viewer, save };
        var captions = new[] { T("Commande", "Command"), "Cooldown (s)", T("Portée", "Scope"), T("Accès", "Access"), "Viewer" };
        var viewerLabel = new TextBlock { Text = "Viewer", FontWeight = FontWeight.SemiBold };
        for (var i = 0; i < controls.Length; i++)
        {
            if (i < captions.Length) { var label = i == 4 ? viewerLabel : new TextBlock { Text = captions[i], FontWeight = FontWeight.SemiBold }; Grid.SetColumn(label, i); form.Children.Add(label); }
            Grid.SetColumn(controls[i], i); Grid.SetRow(controls[i], 1); form.Children.Add(controls[i]);
        }
        void ApplyPermission()
        {
            var visible = permission.SelectedIndex == 3;
            viewer.IsVisible = viewerLabel.IsVisible = visible;
            form.ColumnDefinitions[4].Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }
        permission.SelectionChanged += (_, _) => ApplyPermission(); ApplyPermission();
        var frame = new Border { Background = Brush.Parse("#141A34"), BorderBrush = Brush.Parse("#33406A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(16), Child = form }; Grid.SetRow(frame, 2); root.Children.Add(frame);
        var categoryTools = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel } };
        Grid.SetRow(categoryTools, 3); root.Children.Add(categoryTools);
        var collapsedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refreshVersion = 0;
        var rows = new StackPanel { Spacing = 8 }; var scroll = new ScrollViewer { Content = rows };
        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        var headerHost = new StackPanel(); table.Children.Add(headerHost); Grid.SetRow(scroll, 1); table.Children.Add(scroll); Grid.SetRow(table, 4); root.Children.Add(table); Content = root;
        void Reset() { editing = null; name.Text = ""; seconds.Text = "0"; viewer.Text = ""; scope.SelectedIndex = permission.SelectedIndex = 0; ManagementButtons.Add(save, english); cancel.IsVisible = false; }
        void Error(Exception ex) { status.Text = ex.Message; status.Foreground = Brush.Parse("#FF7777"); }
        async Task Refresh()
        {
            var version = ++refreshVersion;
            var commands = await ChatCommands.ListAsync();
            var categories = await AutomationQueues.LoadAsync();
            if (version != refreshVersion) return;
            rows.Children.Clear();
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("1.4*,1*,1*,1.2*,1.3*,40,40"), ColumnSpacing = 8, Margin = new Thickness(14, 0) };
            var headings = new[] { T("Commande", "Command"), "Cooldown", T("Portée", "Scope"), T("Accès", "Access"), T("Catégorie", "Category"), "Actions" };
            for (var i = 0; i < headings.Length; i++) { var text = new TextBlock { Text = headings[i], FontWeight = FontWeight.SemiBold }; Grid.SetColumn(text, i); header.Children.Add(text); } headerHost.Children.Clear(); headerHost.Children.Add(header);
            var groups = new CategoryGroups(rows, collapsedGroups, T("Sans catégorie", "Uncategorized"));
            foreach (var item in commands.OrderBy(item => ManagementCategories.Get(categories, "commandCategories", item["id"]!.ToString()), StringComparer.OrdinalIgnoreCase).ThenBy(item => item["command"]?.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                var categoryName = ManagementCategories.Get(categories, "commandCategories", item["id"]!.ToString());
                var edit = ManagementButtons.Edit(english); var delete = ManagementButtons.Delete(english);
                edit.Click += (_, _) => { editing = item["id"]!.ToString(); name.Text = item["command"]?.ToString(); seconds.Text = item["cooldownSeconds"]?.ToString(); scope.SelectedIndex = item["cooldownScope"]?.ToString() == "viewer" ? 1 : 0; permission.SelectedIndex = item["permission"]?.ToString() switch { "moderator" => 1, "vip" => 2, "viewer" => 3, _ => 0 }; viewer.Text = item["allowedUser"]?.ToString(); ManagementButtons.Save(save, english); cancel.IsVisible = true; };
                delete.Click += async (_, _) => { try { await ChatCommands.SaveAsync(item, true); if (editing == item["id"]?.ToString()) Reset(); await Refresh(); } catch (Exception ex) { Error(ex); } };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("1.4*,1*,1*,1.2*,1.3*,40,40"), ColumnSpacing = 8 };
                var access = item["permission"]?.ToString() switch { "moderator" => T("Modérateurs", "Moderators"), "vip" => "VIP", "viewer" => item["allowedUser"]?.ToString(), _ => T("Tout le monde", "Everyone") };
                var values = new[] { item["command"]?.ToString(), item["cooldownSeconds"] + " s", item["cooldownScope"]?.ToString() == "viewer" ? "Viewer" : "Global", access };
                for (var i = 0; i < values.Length; i++) { var text = new TextBlock { Text = values[i], Foreground = Brush.Parse(i == 1 ? "#7DBEFF" : "#CDD0E3"), FontWeight = i == 0 ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(text, i); row.Children.Add(text); }
                var category = new Button { Content = categoryName.Length > 0 ? categoryName : T("Sans catégorie", "Uncategorized") };
                ToolTip.SetTip(category, T("Changer la catégorie", "Change category"));
                category.Click += async (_, _) => { try { if (await ManagementCategories.EditAsync(this, english, "commandCategories", item["id"]!.ToString())) await Refresh(); } catch (Exception ex) { Error(ex); } };
                Grid.SetColumn(category, 4); row.Children.Add(category);
                Grid.SetColumn(edit, 5); row.Children.Add(edit); Grid.SetColumn(delete, 6); row.Children.Add(delete);
                groups.Add(categoryName, new Border { Background = Brush.Parse("#141A34"), BorderBrush = Brush.Parse("#33406A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 10), Child = row });
            }
            status.Text = T($"{commands.Count} commande(s).", $"{commands.Count} command(s)."); status.Foreground = Brush.Parse("#9DA8CB");
        }
        save.Click += async (_, _) =>
        {
            try
            {
                var command = name.Text?.Trim() ?? "";
                if (!command.StartsWith('!') || command.Length < 2 || command.Any(char.IsWhiteSpace) || !int.TryParse(seconds.Text, out var delay) || delay < 0 || delay > 86400 || (permission.SelectedIndex == 3 && string.IsNullOrWhiteSpace(viewer.Text))) throw new InvalidOperationException(T("Indique une commande !nom, un cooldown de 0 à 86400 et un viewer si nécessaire.", "Enter !command, a cooldown from 0 to 86400 and a viewer when required."));
                if ((await ChatCommands.ListAsync()).Any(item => item["id"]?.ToString() != editing && string.Equals(item["command"]?.ToString(), command, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(T("Cette commande existe déjà.", "This command already exists."));
                await ChatCommands.SaveAsync(new JsonObject { ["id"] = editing ?? Guid.NewGuid().ToString(), ["command"] = command, ["cooldownSeconds"] = delay.ToString(), ["cooldownScope"] = scope.SelectedIndex == 1 ? "viewer" : "global", ["permission"] = permission.SelectedIndex switch { 1 => "moderator", 2 => "vip", 3 => "viewer", _ => "everyone" }, ["allowedUser"] = permission.SelectedIndex == 3 ? viewer.Text?.Trim().TrimStart('@') : "" }); Reset(); await Refresh();
            }
            catch (Exception ex) { Error(ex); }
        };
        cancel.Click += (_, _) => Reset(); Opened += async (_, _) => { try { await Refresh(); } catch (Exception ex) { Error(ex); } };
    }
}
