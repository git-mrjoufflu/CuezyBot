using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Automation.Desktop;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(string language)
    {
        InitializeComponent();
        LanguageCombo.SelectedIndex = language == "English" ? 1 : 0;
    }

    public string? SavedLanguage { get; private set; }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        SavedLanguage = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Français";
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
