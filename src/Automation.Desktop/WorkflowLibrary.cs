using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Automation.Desktop;

public sealed partial class MainWindow
{
    private async void OnWorkflowLibraryClick(object? sender, RoutedEventArgs args)
    {
        string T(string fr, string en) => _english ? en : fr;
        var dialog = new Window { Title = T("Automatisations — CuezyBot", "Automations — CuezyBot"), Width = 1000, Height = 700, MinWidth = 780, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#0B1024") };
        var root = new Grid { Margin = new Thickness(28), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"), RowSpacing = 16 };
        root.Children.Add(new TextBlock { Text = T("Automatisations", "Automations"), FontSize = 25, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
        var search = new TextBox { PlaceholderText = T("Rechercher une automatisation…", "Search automations…"), Height = 38 };
        var filter = new ComboBox { ItemsSource = new[] { T("Toutes", "All"), T("Activées", "Active"), T("Désactivées", "Inactive") }, SelectedIndex = 0, Height = 38 };
        var categoryFilter = new ComboBox { Height = 38, MinWidth = 160 };
        var add = new Button { Content = "＋", FontSize = 22, Padding = new Thickness(10, 2), MinWidth = 42, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center }; ToolTip.SetTip(add, T("Nouvelle automatisation", "New automation"));
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 12 };
        tools.Children.Add(search); Grid.SetColumn(filter, 1); tools.Children.Add(filter); Grid.SetColumn(add, 2); tools.Children.Add(add); Grid.SetRow(tools, 1); root.Children.Add(tools);
        var count = new TextBlock { Foreground = Brush.Parse("#9DA8CB") }; Grid.SetRow(count, 3); root.Children.Add(count);
        var editName = new TextBox { Height = 38 };
        var editSave = new Button(); ManagementButtons.Save(editSave, _english);
        var editCancel = new Button { Content = T("Annuler", "Cancel") };
        string? editingName = null;
        var editor = new Border { IsVisible = false, Background = Brush.Parse("#141A34"), BorderBrush = Brush.Parse("#33406A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(16) };
        var editGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 12, RowSpacing = 7 };
        editGrid.Children.Add(new TextBlock { Text = T("Nom de l’automatisation", "Automation name"), Foreground = Brushes.White, FontWeight = FontWeight.SemiBold });
        Grid.SetRow(editName, 1); editGrid.Children.Add(editName); Grid.SetRow(editSave, 1); Grid.SetColumn(editSave, 1); editGrid.Children.Add(editSave); Grid.SetRow(editCancel, 1); Grid.SetColumn(editCancel, 2); editGrid.Children.Add(editCancel); editor.Child = editGrid; Grid.SetRow(editor, 2); root.Children.Add(editor);
        editCancel.Click += (_, _) => { editingName = null; editor.IsVisible = false; };
        var rows = new StackPanel { Spacing = 8 }; var scroll = new ScrollViewer { Content = rows }; Grid.SetRow(scroll, 4); root.Children.Add(scroll); dialog.Content = root;
        var config = await AutomationQueues.LoadAsync();
        Grid.SetColumn(add, 3); Grid.SetColumn(categoryFilter, 2); tools.Children.Add(categoryFilter);
        bool loadingCategories = false;
        void LoadCategories()
        {
            loadingCategories = true;
            var selected = categoryFilter.SelectedItem as string;
            var choices = new[] { T("Toutes les catégories", "All categories"), T("Sans catégorie", "Uncategorized") }.Concat(ManagementCategories.Names(config, "workflowCategories")).ToArray();
            categoryFilter.ItemsSource = choices; categoryFilter.SelectedIndex = Math.Max(0, Array.IndexOf(choices, selected));
            loadingCategories = false;
        }
        LoadCategories();
        var collapsedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Refresh()
        {
            rows.Children.Clear();
            var names = _workflowNames.Where(name => name.Contains(search.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase) && (filter.SelectedIndex == 0 || _enabledWorkflowIds.Contains(_workflowIds[name]) == (filter.SelectedIndex == 1)) && (categoryFilter.SelectedIndex <= 0 || (categoryFilter.SelectedIndex == 1 ? ManagementCategories.Get(config, "workflowCategories", _workflowIds[name].ToString()).Length == 0 : string.Equals(ManagementCategories.Get(config, "workflowCategories", _workflowIds[name].ToString()), categoryFilter.SelectedItem?.ToString(), StringComparison.OrdinalIgnoreCase)))).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            count.Text = T($"{names.Length} / {_workflowNames.Count} automatisations", $"{names.Length} / {_workflowNames.Count} automations");
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,1.2*,Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(14, 0) };
            foreach (var (text, column) in new[] { (T("Nom", "Name"), 0), (T("État", "Status"), 1), (T("File", "Queue"), 2), (T("Catégorie", "Category"), 3) }) { var label = new TextBlock { Text = text, Foreground = Brush.Parse("#9DA8CB") }; Grid.SetColumn(label, column); header.Children.Add(label); } rows.Children.Add(header);
            var groups = new CategoryGroups(rows, collapsedGroups, T("Sans catégorie", "Uncategorized"));
            foreach (var name in names.OrderBy(name => ManagementCategories.Get(config, "workflowCategories", _workflowIds[name].ToString()), StringComparer.OrdinalIgnoreCase).ThenBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var id = _workflowIds[name]; var active = _enabledWorkflowIds.Contains(id);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,1.2*,Auto,Auto"), ColumnSpacing = 10 };
                var values = new[] { name, active ? T("Activée", "Active") : T("Désactivée", "Inactive"), config["workflows"]?[id.ToString()]?.ToString() ?? "Default" };
                for (var i = 0; i < values.Length; i++) { var text = new TextBlock { Text = values[i], TextTrimming = TextTrimming.CharacterEllipsis, Foreground = i == 1 && active ? Brush.Parse("#53D994") : Brushes.White, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }; Grid.SetColumn(text, i); row.Children.Add(text); }
                var rename = ManagementButtons.Edit(_english); var delete = ManagementButtons.Delete(_english);
                ToolTip.SetTip(rename, T("Renommer", "Rename")); ToolTip.SetTip(delete, T("Supprimer", "Delete"));
                rename.Click += (_, _) => { editingName = name; editName.Text = name; editor.IsVisible = true; editName.Focus(); };
                delete.Click += (_, _) => dialog.Close(("delete", name));
                var category = new Button { Content = ManagementCategories.Get(config, "workflowCategories", id.ToString()) is { Length: > 0 } categoryName ? categoryName : T("Sans catégorie", "Uncategorized"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                ToolTip.SetTip(category, T("Changer la catégorie", "Change category"));
                category.Click += async (_, _) => { try { if (await ManagementCategories.EditAsync(dialog, _english, "workflowCategories", id.ToString())) { config = await AutomationQueues.LoadAsync(); LoadCategories(); Refresh(); } } catch (Exception ex) { count.Text = ex.Message; } };
                Grid.SetColumn(category, 3); row.Children.Add(category);
                Grid.SetColumn(rename, 4); row.Children.Add(rename); Grid.SetColumn(delete, 5); row.Children.Add(delete);
                var entry = new Border { Background = Brush.Parse("#141A34"), BorderBrush = Brush.Parse("#33406A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 10), Child = row, Cursor = new Cursor(StandardCursorType.Hand), Focusable = true };
                bool IsRowClick(object? source) => source is not Visual visual || (visual is not Button && !visual.GetVisualAncestors().OfType<Button>().Any());
                entry.PointerReleased += (_, pointer) =>
                {
                    if (pointer.InitialPressMouseButton != MouseButton.Left || !IsRowClick(pointer.Source)) return;
                    pointer.Handled = true; dialog.Close(("open", name));
                };
                entry.KeyDown += (_, key) => { if (key.Key == Key.Enter && IsRowClick(key.Source)) { key.Handled = true; dialog.Close(("open", name)); } };
                entry.PointerEntered += (_, _) => entry.Background = Brush.Parse("#202944");
                entry.PointerExited += (_, _) => entry.Background = Brush.Parse("#141A34");
                ToolTip.SetTip(entry, T("Cliquer pour ouvrir l’automatisation", "Click to open this automation"));
                groups.Add(ManagementCategories.Get(config, "workflowCategories", id.ToString()), entry);
            }
        }
        editSave.Click += async (_, _) =>
        {
            if (editingName is null) return;
            try
            {
                var resultName = editName.Text?.Trim() ?? "";
                if (resultName.Length == 0) throw new InvalidOperationException(T("Indique un nom.", "Enter a name."));
                if (!string.Equals(editingName, resultName, StringComparison.OrdinalIgnoreCase) && _workflowNames.Contains(resultName, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException(T("Ce nom est déjà utilisé.", "This name is already used."));
                WorkflowSelector.SelectedItem = editingName;
                await RenameCurrentWorkflowAsync(resultName);
                editingName = null; editor.IsVisible = false; Refresh();
            }
            catch (Exception ex) { count.Text = ex.Message; }
        };
        search.TextChanged += (_, _) => Refresh(); filter.SelectionChanged += (_, _) => Refresh(); categoryFilter.SelectionChanged += (_, _) => { if (!loadingCategories) Refresh(); }; add.Click += (_, _) => dialog.Close(("new", "")); Refresh();
        var result = await dialog.ShowDialog<(string Action, string Name)>(this);
        if (result.Action is null) return;
        if (result.Action == "new") { OnNewWorkflowClick(this, args); return; }
        if (result.Action == "delete")
        {
            var confirm = new Window { Title = T("Supprimer l’automatisation", "Delete automation"), Width = 430, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
            var yes = new Button { Content = T("Supprimer", "Delete") }; var no = new Button { Content = T("Annuler", "Cancel") };
            yes.Click += (_, _) => confirm.Close(true); no.Click += (_, _) => confirm.Close(false);
            confirm.Content = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { new TextBlock { Text = T($"Supprimer « {result.Name} » ?", $"Delete “{result.Name}”?"), TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { no, yes } } } };
            if (!await confirm.ShowDialog<bool>(this)) return;
        }
        WorkflowSelector.SelectedItem = result.Name;
        if (result.Action == "rename") OnRenameWorkflowClick(this, args);
        if (result.Action == "delete") OnDeleteWorkflowClick(this, args);
    }

    private async Task RenameCurrentWorkflowAsync(string name)
    {
        var current = _activeWorkflowName;
        var index = _workflowNames.IndexOf(current);
        _workflowActions[current] = _selectedActions.ToList();
        _workflowLinks[current] = GetManualLinkLabels().ToList();
        _workflowPositions[current] = CaptureNodePositions();
        _switchingWorkflow = true;
        try
        {
            _workflowNames[index] = name;
            if (_workflowActions.Remove(current, out var actions)) _workflowActions[name] = actions;
            if (_workflowLinks.Remove(current, out var links)) _workflowLinks[name] = links;
            if (_workflowPositions.Remove(current, out var positions)) _workflowPositions[name] = positions;
            if (_workflowIds.Remove(current, out var id)) _workflowIds[name] = id;
            _activeWorkflowName = name; PrototypeServices.CurrentWorkflowName = name;
            WorkflowSelector.ItemsSource = _workflowNames.ToArray(); WorkflowSelector.SelectedItem = name;
        }
        finally { _switchingWorkflow = false; }
        await PrototypeServices.SavePrototypeDraftAsync(_selectedActions, GetManualLinkLabels());
        await ConnectionPersistence.SaveWorkflowPositionsAsync(name, _workflowPositions[name]);
    }
}
