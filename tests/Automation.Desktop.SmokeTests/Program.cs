using System.Reflection;
using System.Text.Json.Nodes;
using Automation.Desktop;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

AppBuilder.Configure<SmokeApp>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}
void Invoke(MainWindow window, string method, params object?[] args) =>
    typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);

var window = new MainWindow(false);
window.Show();
Dispatcher.UIThread.RunJobs();
var canvas = window.FindControl<Canvas>("WorkflowCanvas")!;
Check(!canvas.Children.OfType<Border>().Any(border => border.Classes.Contains("node") && border.IsVisible), "Demo nodes must be hidden");
window.UpdateIntegrationCount(true, false);
Dispatcher.UIThread.RunJobs();
var library = window.FindControl<StackPanel>("ObsActions")!;
var libraryText = ((Border)library.Children[0]).Child!;
var clickPoint = libraryText.TranslatePoint(new Point(40, 10), window)!.Value;
for (var i = 0; i < 2; i++)
{
    window.MouseDown(clickPoint, MouseButton.Left);
    window.MouseUp(clickPoint, MouseButton.Left);
}
var cards = canvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray();
Check(cards.Length == 2, "Clicking action text twice must add two cards");
Check(!Equals(cards[0].Tag, cards[1].Tag), "Action instances must be distinct");

var key = "▻ Visibilité d’une source\u001f" + Guid.NewGuid().ToString("N");
PrototypeServices.ActionOptions[key] = new JsonObject { ["scene"] = "Studio", ["sources"] = new JsonArray("Camera", "Logo") };
PrototypeServices.SourceVisibility[key] = false;
Invoke(window, "AddActionNode", key);
Dispatcher.UIThread.RunJobs();
var source = canvas.Children.OfType<Border>().Single(border => Equals(border.Tag, key));
var scenePicker = source.GetVisualDescendants().OfType<ComboBox>().Single(picker => picker.Tag is ValueTuple<string, string>);
Check(Equals(scenePicker.SelectedItem, "Studio"), "Saved scene must show while OBS is offline");
var checkboxes = source.GetVisualDescendants().OfType<CheckBox>().ToArray();
Check(checkboxes.Length == 2 && checkboxes.All(box => box.IsChecked == true), "Saved sources must restore checked while OBS is offline");
checkboxes[0].IsChecked = false;
Check(PrototypeServices.ActionOptions[key]["sources"]!.AsArray().Count == 1, "Checkbox edit must update saved parameters");
window.ApplyLanguage(true);
Dispatcher.UIThread.RunJobs();
var visibility = source.GetVisualDescendants().OfType<ComboBox>().Single(picker => Equals(picker.Tag, "visibilityOptions"));
Check(visibility.ItemsSource!.Cast<string>().SequenceEqual(new[] { "Show", "Hide" }), "Visibility choices must switch to English");
Check(visibility.SelectedIndex == 1 && !PrototypeServices.SourceVisibility[key], "Language changes must preserve Hide selection");
Check(source.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Sources to change"), "Permanent labels must switch language");
window.ApplyLanguage(false);
Check(visibility.SelectedIndex == 1 && !PrototypeServices.SourceVisibility[key], "French switch must preserve Hide selection");
var originalLeft = Canvas.GetLeft(cards[0]);
var headerPoint = cards[0].TranslatePoint(new Point(20, 10), window)!.Value;
window.MouseDown(headerPoint, MouseButton.Left);
window.MouseMove(headerPoint + new Vector(50, -100));
window.MouseUp(headerPoint + new Vector(50, -100), MouseButton.Left);
Check(Math.Abs(Canvas.GetLeft(cards[0]) - originalLeft - 50) < 1, "Dragging must keep the card under the pointer");
foreach (var card in cards)
{
    var point = card.TranslatePoint(new Point(20, 10), window)!.Value;
    window.MouseDown(point, MouseButton.Left);
    window.MouseUp(point, MouseButton.Left);
}
Check(canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Count() == 1, "Clicking two cards must create one manual arrow");
var movedPosition = new Point(Canvas.GetLeft(cards[0]), Canvas.GetTop(cards[0]));
window.FindControl<ComboBox>("WorkflowTriggerSelector")!.SelectedIndex = 1;
Check(PrototypeServices.CurrentTriggerType == "twitch.follow.received", "Follower trigger must be explicit");
Invoke(window, "OnNewWorkflowClick", null, new RoutedEventArgs());
Check(!canvas.Children.OfType<Border>().Any(border => border.Classes.Contains("dynamicAction")), "New automation must have an empty canvas");
Check(PrototypeServices.CurrentTriggerType == "manual.run", "New automation must default to manual activation");
window.FindControl<ComboBox>("WorkflowSelector")!.SelectedIndex = 0;
Dispatcher.UIThread.RunJobs();
var restoredCards = canvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray();
Check(restoredCards.Length == 2, "Switching back must restore the original cards");
Check(PrototypeServices.CurrentTriggerType == "twitch.follow.received", "Switching back must restore the trigger kind");
Check(new Point(Canvas.GetLeft(restoredCards[0]), Canvas.GetTop(restoredCards[0])) == movedPosition, "Switching back must preserve positions");
Check(canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Count() == 1, "Switching back must restore manual arrows");
Invoke(window, "DeleteActionNode", restoredCards[0]);
Check(canvas.Children.OfType<Border>().Count(border => border.Classes.Contains("dynamicAction")) == 1, "Delete must remove only one instance");
Invoke(window, "OnUndoClick", null, new RoutedEventArgs());
Check(canvas.Children.OfType<Border>().Count(border => border.Classes.Contains("dynamicAction")) == 2, "Undo deletion must restore the deleted card");
Check(canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Count() == 1, "Undo deletion must restore its arrows");
Invoke(window, "OnRedoClick", null, new RoutedEventArgs());
Check(canvas.Children.OfType<Border>().Count(border => border.Classes.Contains("dynamicAction")) == 1, "Redo deletion must remove the same card again");
Invoke(window, "OnZoomInClick", null, new RoutedEventArgs());
Check(window.FindControl<TextBlock>("CanvasZoomLabel")!.Text == "110 %", "Zoom control must update the scale");
Invoke(window, "OnCenterCanvasClick", null, new RoutedEventArgs());
Check(!ReferenceEquals(window.FindControl<Border>("WorkflowToolbar")!.Parent, canvas), "Toolbar must stay fixed outside the zoomed canvas");
window.FindControl<TextBox>("ActionSearch")!.Text = "scène";
Dispatcher.UIThread.RunJobs();
Check(library.Children.OfType<Border>().Count(item => item.IsVisible) == 1, "Search must filter the OBS library");
window.FindControl<TextBox>("ActionSearch")!.Text = "";
Dispatcher.UIThread.RunJobs();
Check(library.Children.OfType<Border>().All(item => item.IsVisible), "Clearing search must restore the library");
if (args.Length > 0)
{
    foreach (var card in canvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray()) card.IsVisible = false;
    Invoke(window, "AddActionNode", key);
    var previewSource = canvas.Children.OfType<Border>().Last(border => Equals(border.Tag, key));
    Canvas.SetLeft(previewSource, 80); Canvas.SetTop(previewSource, 150);
    var titleKey = "✎ Modifier un titre\u001f" + Guid.NewGuid().ToString("N");
    PrototypeServices.ActionOptions[titleKey] = new JsonObject { ["title"] = "Bienvenue sur le live !" };
    Invoke(window, "AddActionNode", titleKey);
    var titleCard = canvas.Children.OfType<Border>().Last(border => Equals(border.Tag, titleKey));
    Canvas.SetLeft(titleCard, 550); Canvas.SetTop(titleCard, 190);
    canvas.RenderTransform = null;
    Dispatcher.UIThread.RunJobs();
    foreach (var card in new[] { previewSource, titleCard })
    {
        var point = card.TranslatePoint(new Point(20, 10), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    window.CaptureRenderedFrame()!.Save(args[0], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
}
window.Close();
Console.WriteLine($"Desktop smoke tests passed: {assertions} assertions; no account connected and no live action executed.");

public sealed class SmokeApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}
