using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Automation.Desktop;

public sealed partial class VariablesWindow : Window
{
    private static readonly Regex ValidName = new("^[A-Za-z_][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);
    private readonly bool english;
    private string? editingName;
    private IReadOnlyList<Automation.Contracts.Persistence.PersistentVariable> allVariables = Array.Empty<Automation.Contracts.Persistence.PersistentVariable>();

    public VariablesWindow() : this(UiLanguageState.IsEnglish) { }

    public VariablesWindow(bool english)
    {
        this.english = english;
        InitializeComponent();
        ApplyLanguage();
        Opened += async (_, _) => await RefreshAsync();
    }

    private void ApplyLanguage()
    {
        Heading.Text = "Variables";
        Description.Text = english
            ? "Create system or viewer variables, temporary or permanent. Use them in action text with %name%."
            : "Crée des variables système ou viewer, temporaires ou permanentes. Utilise-les dans les actions avec %nom%.";
        NameLabel.Text = english ? "Name" : "Nom";
        TypeLabel.Text = english ? "Type" : "Type";
        ValueLabel.Text = english ? "Initial value" : "Valeur initiale";
        ScopeLabel.Text = english ? "Scope" : "Portée";
        LifetimeLabel.Text = english ? "Lifetime" : "Durée";
        TypeInput.ItemsSource = english ? new[] { "Number", "Text" } : new[] { "Nombre", "Texte" };
        ScopeInput.ItemsSource = english ? new[] { "System", "Viewer" } : new[] { "Système", "Viewer" };
        LifetimeInput.ItemsSource = english ? new[] { "Temporary", "Permanent" } : new[] { "Temporaire", "Permanente" };
        SystemTab.Header = english ? "System" : "Système";
        ViewerTab.Header = english ? "Viewer" : "Viewer";
        ViewerFilter.PlaceholderText = english ? "All viewers" : "Tous les viewers";
        if (TypeInput.SelectedIndex < 0) TypeInput.SelectedIndex = 0;
        NameInput.PlaceholderText = english ? "e.g. visits" : "ex. visites";
        SaveButton.Content = english ? "Add" : "Ajouter";
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs eventArgs)
    {
        var name = NameInput.Text?.Trim() ?? "";
        if (!ValidName.IsMatch(name))
        {
            StatusText.Text = english
                ? "Use 1–64 characters: letters, numbers, underscore, dot or hyphen. The first character cannot be a number."
                : "Utilise 1 à 64 caractères : lettres, chiffres, soulignement, point ou trait d’union. Le premier caractère ne peut pas être un chiffre.";
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#FF9B72"));
            return;
        }
        try
        {
            var scope = ScopeInput.SelectedIndex == 1 ? "viewer" : "system";
            var lifetime = LifetimeInput.SelectedIndex == 0 ? "temporary" : "permanent";
            if (TypeInput.SelectedIndex == 1)
                await PrototypeServices.SetTextVariableAsync(name, ValueInput.Text ?? "", scope, lifetime);
            else if (long.TryParse(ValueInput.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number))
                await PrototypeServices.SetVariableAsync(name, number, scope, lifetime);
            else
            {
                StatusText.Text = english ? "Enter a valid whole number." : "Entre un nombre entier valide.";
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#FF9B72"));
                return;
            }
            if (editingName is not null && !string.Equals(editingName, name, StringComparison.OrdinalIgnoreCase))
                await PrototypeServices.DeleteVariableAsync(editingName);
            editingName = null;
            NameInput.Text = "";
            ValueInput.Text = "0";
            TypeInput.SelectedIndex = 0;
            SaveButton.Content = english ? "Add" : "Ajouter";
            await RefreshAsync(english ? "Variable saved." : "Variable enregistrée.");
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#FF7777"));
        }
    }

    private async Task RefreshAsync(string? message = null)
    {
        try
        {
            allVariables = await PrototypeServices.GetVariablesAsync();
            var viewerIds = allVariables.Where(variable => variable.Scope == "viewer" && !string.IsNullOrWhiteSpace(variable.ViewerId)).Select(variable => variable.ViewerId!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray();
            ViewerFilter.ItemsSource = viewerIds;
            var viewerMode = ScopeTabs.SelectedIndex == 1;
            ViewerFilter.IsVisible = viewerMode;
            var selectedViewer = ViewerFilter.SelectedItem as string;
            var variables = allVariables.Where(variable => (viewerMode ? variable.Scope == "viewer" : variable.Scope != "viewer") && (!viewerMode || selectedViewer is null || variable.ViewerId == selectedViewer)).ToArray();
            VariableRows.Children.Clear();
            foreach (var variable in variables)
            {
                var edit = new Button { Content = english ? "Edit" : "Modifier" };
                edit.Click += (_, _) =>
                {
                    editingName = variable.Name;
                    NameInput.Text = variable.Name;
                    TypeInput.SelectedIndex = variable.Type == "text" ? 1 : 0;
                    ValueInput.Text = variable.Value;
                    ScopeInput.SelectedIndex = variable.Scope == "viewer" ? 1 : 0;
                    LifetimeInput.SelectedIndex = variable.Lifetime == "temporary" ? 0 : 1;
                    SaveButton.Content = english ? "Save" : "Enregistrer";
                };
                var delete = new Button { Content = english ? "Delete" : "Supprimer" };
                delete.Click += async (_, _) =>
                {
                    var confirmation = new Window { Title = english ? "Delete variable" : "Supprimer la variable", Width = 390, Height = 180, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    var cancel = new Button { Content = english ? "Cancel" : "Annuler" };
                    var confirm = new Button { Content = english ? "Delete" : "Supprimer" };
                    cancel.Click += (_, _) => confirmation.Close(false);
                    confirm.Click += (_, _) => confirmation.Close(true);
                    confirmation.Content = new StackPanel
                    {
                        Margin = new Thickness(20), Spacing = 16,
                        Children =
                        {
                            new TextBlock { Text = english ? $"Delete %{variable.Name}%? Workflows using it may fail." : $"Supprimer %{variable.Name}%? Les automatisations qui l’utilisent pourraient échouer.", TextWrapping = TextWrapping.Wrap },
                            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, confirm } }
                        }
                    };
                    if (!await confirmation.ShowDialog<bool>(this)) return;
                    await PrototypeServices.DeleteVariableAsync(variable.Name);
                    if (editingName == variable.Name) editingName = null;
                    await RefreshAsync(english ? "Variable deleted." : "Variable supprimée.");
                };
                var token = new TextBlock { Text = $"%{variable.Name}%", FontWeight = FontWeight.SemiBold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                var value = new TextBlock { Text = variable.Value, Foreground = new SolidColorBrush(Color.Parse("#7DBEFF")), FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                var type = new TextBlock { Text = variable.Type == "text" ? (english ? "Text" : "Texte") : (english ? "Number" : "Nombre"), Foreground = new SolidColorBrush(Color.Parse("#CDD0E3")), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                var lifetime = new TextBlock { Text = variable.Lifetime == "temporary" ? (english ? "Temporary" : "Temporaire") : (english ? "Permanent" : "Permanente"), Foreground = new SolidColorBrush(Color.Parse("#CDD0E3")), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.6*,1.2*,1*,1.2*,Auto,Auto"), ColumnSpacing = 8 };
                grid.Children.Add(token);
                Grid.SetColumn(value, 1); grid.Children.Add(value);
                Grid.SetColumn(type, 2); grid.Children.Add(type);
                Grid.SetColumn(lifetime, 3); grid.Children.Add(lifetime);
                Grid.SetColumn(edit, 4); grid.Children.Add(edit);
                Grid.SetColumn(delete, 5); grid.Children.Add(delete);
                VariableRows.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#141A34")), BorderBrush = new SolidColorBrush(Color.Parse("#33406A")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 10), Child = grid });
            }
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#9DA8CB"));
            StatusText.Text = message ?? (variables.Length == 0 ? (english ? "No variables in this category." : "Aucune variable dans cette catégorie.") : $"{variables.Length} variable(s).");
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#FF7777"));
        }
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (ValueInput is null || TypeInput is null) return;
        ValueInput.PlaceholderText = TypeInput.SelectedIndex == 1
            ? (english ? "Text value" : "Valeur texte")
            : (english ? "Whole number" : "Nombre entier");
        if (TypeInput.SelectedIndex == 0 && string.IsNullOrWhiteSpace(ValueInput.Text)) ValueInput.Text = "0";
    }

    private async void OnScopeChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (ScopeTabs is null || ScopeTabs.SelectedIndex < 0) return;
        ViewerFilter.IsVisible = ScopeTabs.SelectedIndex == 1;
        await RefreshAsync();
    }

    private async void OnViewerChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (ScopeTabs?.SelectedIndex == 1) await RefreshAsync();
    }
}
