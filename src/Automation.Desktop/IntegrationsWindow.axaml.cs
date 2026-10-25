using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Automation.Desktop;

public sealed partial class IntegrationsWindow : Window
{
    public IntegrationsWindow() => InitializeComponent();

    private async void OnObsClick(object? sender, RoutedEventArgs eventArgs) =>
        await new ObsConnectionWindow().ShowDialog(this);

    private async void OnTwitchClick(object? sender, RoutedEventArgs eventArgs) =>
        await new TwitchConnectionWindow().ShowDialog(this);

    private void OnCloseClick(object? sender, RoutedEventArgs eventArgs) => Close();
}
