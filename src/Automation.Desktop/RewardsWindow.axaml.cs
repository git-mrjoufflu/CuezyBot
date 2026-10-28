using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Automation.Desktop;

public sealed partial class RewardsWindow : Window
{
    private readonly bool english;
    private string? editingId;
    public RewardsWindow() : this(UiLanguageState.IsEnglish) { }
    public RewardsWindow(bool english)
    {
        this.english = english;
        InitializeComponent();
        Title = T("Récompenses — CuezyBot", "Rewards — CuezyBot");
        Heading.Text = T("Récompenses de points de chaîne", "Channel point rewards");
        Description.Text = T("Crée et modifie les récompenses Twitch dans le formulaire ci-dessous.", "Create and edit Twitch rewards using the form below.");
        NameLabel.Text = T("Nom", "Name"); CostLabel.Text = T("Coût", "Cost");
        CooldownLabel.Text = T("Cooldown (secondes)", "Cooldown (seconds)"); ScopeLabel.Text = T("Portée", "Scope");
        NameInput.PlaceholderText = T("ex. Défi", "e.g. Challenge");
        SaveButton.Content = T("Ajouter", "Add"); RefreshButton.Content = T("Actualiser", "Refresh"); CancelButton.Content = T("Annuler", "Cancel");
        Opened += async (_, _) => await RefreshAsync();
    }
    private string T(string fr, string en) => english ? en : fr;
    private void Error(Exception ex) { StatusText.Text = ex.Message; StatusText.Foreground = new SolidColorBrush(Color.Parse("#FF7777")); }
    private void Reset()
    {
        editingId = null; NameInput.Text = ""; CostInput.Text = "100"; CooldownInput.Text = "0";
        SaveButton.Content = T("Ajouter", "Add"); CancelButton.IsVisible = false;
    }
    private void OnCancelClick(object? sender, RoutedEventArgs args) => Reset();
    private async void OnRefreshClick(object? sender, RoutedEventArgs args) => await RefreshAsync();
    private async void OnSaveClick(object? sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text) || !int.TryParse(CostInput.Text, out var cost) || cost < 1 || !int.TryParse(CooldownInput.Text, out var cooldown) || cooldown < 0)
        {
            StatusText.Text = T("Indique un nom, un coût supérieur à zéro et un cooldown positif ou nul.", "Enter a name, a cost greater than zero and a non-negative cooldown.");
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#FF9B72")); return;
        }
        SaveButton.IsEnabled = false;
        try
        {
            var id = editingId;
            if (id is null) id = await PrototypeServices.CreateTwitchRewardAsync(NameInput.Text.Trim(), cost);
            await PrototypeServices.UpdateTwitchRewardAsync(id, NameInput.Text.Trim(), cost, cooldown);
            Reset(); await RefreshAsync(T("Récompense enregistrée.", "Reward saved."));
        }
        catch (Exception ex) { Error(ex); }
        finally { SaveButton.IsEnabled = true; }
    }
    private async Task RefreshAsync(string? message = null)
    {
        try
        {
            var rewards = await PrototypeServices.GetTwitchRewardsAsync();
            RewardRows.Children.Clear();
            foreach (var reward in rewards)
            {
                var edit = new Button { Content = T("Modifier", "Edit") };
                edit.Click += (_, _) => { editingId = reward.Id; NameInput.Text = reward.Title; CostInput.Text = reward.Cost.ToString(); CooldownInput.Text = reward.Cooldown.ToString(); SaveButton.Content = T("Enregistrer", "Save"); CancelButton.IsVisible = true; NameInput.Focus(); };
                var delete = new Button { Content = T("Supprimer", "Delete") };
                delete.Click += async (_, _) =>
                {
                    var dialog = new Window { Title = T("Supprimer la récompense", "Delete reward"), Width = 390, Height = 180, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
                    var cancel = new Button { Content = T("Annuler", "Cancel") }; var confirm = new Button { Content = T("Supprimer", "Delete") };
                    cancel.Click += (_, _) => dialog.Close(false); confirm.Click += (_, _) => dialog.Close(true);
                    dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { new TextBlock { Text = T($"Supprimer « {reward.Title} » sur Twitch ?", $"Delete “{reward.Title}” on Twitch?"), TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, confirm } } } };
                    if (!await dialog.ShowDialog<bool>(this)) return;
                    try { await PrototypeServices.DeleteTwitchRewardAsync(reward.Id); if (editingId == reward.Id) Reset(); await RefreshAsync(T("Récompense supprimée.", "Reward deleted.")); } catch (Exception ex) { Error(ex); }
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("1.6*,1.2*,1*,1.2*,Auto,Auto"), ColumnSpacing = 8 };
                var values = new[] { reward.Title, $"{reward.Cost} points", reward.Cooldown > 0 ? $"{reward.Cooldown} s" : T("Aucun", "None"), "Global" };
                for (var i = 0; i < values.Length; i++)
                {
                    var text = new TextBlock { Text = values[i], VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Color.Parse(i == 1 ? "#7DBEFF" : "#CDD0E3")), FontWeight = i == 0 ? FontWeight.SemiBold : FontWeight.Normal };
                    Grid.SetColumn(text, i); row.Children.Add(text);
                }
                Grid.SetColumn(edit, 4); row.Children.Add(edit); Grid.SetColumn(delete, 5); row.Children.Add(delete);
                RewardRows.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#141A34")), BorderBrush = new SolidColorBrush(Color.Parse("#33406A")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 10), Child = row });
            }
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#9DA8CB")); StatusText.Text = message ?? T($"{rewards.Count} récompense(s).", $"{rewards.Count} reward(s).");
        }
        catch (Exception ex) { Error(ex); }
    }
}
