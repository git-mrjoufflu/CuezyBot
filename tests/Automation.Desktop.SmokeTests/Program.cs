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
var addAction = "◉  Changer de scène";
window.AddCanvasAction(addAction);
window.AddCanvasAction(addAction);
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
Check(PrototypeServices.CurrentTriggerTypes.Count == 0 && !canvas.Children.OfType<Border>().Any(border => Equals(border.Tag, "__trigger__")), "New automation must not insert a manual event");
var emptyDefinition = (Automation.Domain.Workflows.WorkflowDefinition)typeof(PrototypeServices).GetMethod("CreatePrototypeWorkflow", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { Array.Empty<string>(), Array.Empty<(string From, string To)>() })!;
Check(emptyDefinition.Blocks.Count == 0, "An empty draft must not serialize a hidden manual trigger");
window.AddCanvasEvent("core.timer");
Dispatcher.UIThread.RunJobs();
var timerCard = canvas.Children.OfType<Border>().Single(border => Equals(border.Tag, "__trigger__"));
var timerSeconds = timerCard.GetVisualDescendants().OfType<TextBox>().Single(text => Equals(text.Tag, "timerSeconds"));
timerSeconds.Text = "7"; Dispatcher.UIThread.RunJobs();
var timerDefinition = (Automation.Domain.Workflows.WorkflowDefinition)typeof(PrototypeServices).GetMethod("CreatePrototypeWorkflow", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { Array.Empty<string>(), Array.Empty<(string From, string To)>() })!;
Check(timerDefinition.Blocks.Single().Type == "core.timer" && timerDefinition.Blocks.Single().Parameters["seconds"]!.ToString() == "7", "Timer interval must persist in the workflow definition");
window.DeleteAdditionalTrigger(timerCard);
window.AddCanvasEvent("manual.run");
Check(canvas.Children.OfType<Border>().Count(border => Equals(border.Tag, "__trigger__")) == 1, "The first explicitly added event must become the primary trigger");
window.FindControl<ComboBox>("WorkflowSelector")!.SelectedIndex = 0;
Dispatcher.UIThread.RunJobs();
var restoredCards = canvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray();
Check(restoredCards.Length == 2, "Switching back must restore the original cards");
Check(PrototypeServices.CurrentTriggerType == "twitch.follow.received", "Switching back must restore the trigger kind");
Check(new Point(Canvas.GetLeft(restoredCards[0]), Canvas.GetTop(restoredCards[0])) == movedPosition, "Switching back must preserve positions");
Check(canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Count() == 1, "Switching back must restore manual arrows");
Invoke(window, "DeleteActionNode", restoredCards[0]);
Check(canvas.Children.OfType<Border>().Count(border => border.Classes.Contains("dynamicAction")) == 1, "Delete removes one instance");
Invoke(window, "OnUndoClick", null, new RoutedEventArgs());
Check(canvas.Children.OfType<Border>().Count(border => border.Classes.Contains("dynamicAction")) == 2, "Undo restores both cards");
Check(canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Count() == 1, "Undo restores the arrow");
Invoke(window, "OnRedoClick", null, new RoutedEventArgs());
Check(canvas.Children.OfType<Border>().Count(border => border.Classes.Contains("dynamicAction")) == 1, "Redo removes the same card");
Invoke(window, "OnZoomInClick", null, new RoutedEventArgs());
Check(window.FindControl<TextBlock>("CanvasZoomLabel")!.Text == "110 %", "Zoom control must update the scale");
Invoke(window, "OnCenterCanvasClick", null, new RoutedEventArgs());
Check(!ReferenceEquals(window.FindControl<Border>("WorkflowToolbar")!.Parent, canvas), "Toolbar must stay fixed outside the zoomed canvas");
window.FindControl<TextBox>("ActionSearch")!.Text = "scène";
Dispatcher.UIThread.RunJobs();
Check(window.FindControl<Button>("AddCanvasItemButton") is not null, "Canvas add menu must be available");
Check(window.FindControl<Button>("AddCanvasSystemButton") is not null, "System functions must have a dedicated canvas menu");
window.FindControl<TextBox>("ActionSearch")!.Text = "";
Dispatcher.UIThread.RunJobs();
Check(window.FindControl<Button>("AddCanvasItemButton")!.IsVisible, "Canvas add menu must remain visible");
Check(window.FindControl<Button>("AddCanvasSystemButton")!.IsVisible, "System menu must remain visible");
Check(window.FindControl<Button>("VariablesButton") is not null, "Variable management must be accessible from navigation");
var activationSwitch = window.FindControl<ToggleSwitch>("StartWorkflowButton")!;
var runOnceButton = window.FindControl<Button>("RunOnceButton")!;
Invoke(window, "SetActivationSwitch", true);
Check(activationSwitch.IsChecked == true && runOnceButton.IsEnabled, "Activating a workflow must enable manual execution immediately");
Invoke(window, "SetActivationSwitch", false);
Check(activationSwitch.IsChecked == false && !runOnceButton.IsEnabled, "Deactivating a workflow must disable manual execution immediately");
var variablesWindow = new VariablesWindow(false);
Check(variablesWindow.FindControl<TextBox>("NameInput") is not null && variablesWindow.FindControl<Button>("SaveButton")?.Content?.ToString() == "Ajouter", "French variable manager must initialize all named controls");
variablesWindow.Close();
var variablesWindowEnglish = new VariablesWindow(true);
Check(variablesWindowEnglish.FindControl<TextBox>("NameInput")?.PlaceholderText == "e.g. visits" && variablesWindowEnglish.FindControl<Button>("SaveButton")?.Content?.ToString() == "Add", "English variable manager must initialize and translate all named controls");
variablesWindowEnglish.Close();
var resolvedTemplate = PrototypeServices.ReplaceVariableTokensAsync("Visitors: %visits% / %VISITS% — %message%", name => Task.FromResult<string?>(name.Equals("visits", StringComparison.OrdinalIgnoreCase) ? "12" : name == "message" ? "Bienvenue $1" : null)).GetAwaiter().GetResult();
Check(resolvedTemplate == "Visitors: 12 / 12 — Bienvenue $1", "Action text must resolve numeric and literal text variable tokens case-insensitively");
window.AddCanvasEvent("twitch.channel.raid.received");
window.AddCanvasEvent("twitch.channel.cheer.received");
Dispatcher.UIThread.RunJobs();
var triggerCards = canvas.Children.OfType<Border>().Where(border => (border.Tag as string)?.StartsWith("__trigger__:", StringComparison.Ordinal) == true).ToArray();
Check(triggerCards.Length == 2, "Raid and cheer must create distinct event cards");
window.AddCanvasEvent("twitch.channel.raid.received");
Check(canvas.Children.OfType<Border>().Count(border => (border.Tag as string)?.StartsWith("__trigger__:", StringComparison.Ordinal) == true) == 2, "Duplicate event addition must not create another card");
window.ApplyLanguage(true); Dispatcher.UIThread.RunJobs();
Check(triggerCards.Any(card => card.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "⚡  Incoming raid")), "Raid card must translate to English");
Invoke(window, "OnNewWorkflowClick", null, new RoutedEventArgs());
window.FindControl<ComboBox>("WorkflowSelector")!.SelectedIndex = 0;
Dispatcher.UIThread.RunJobs();
Check(PrototypeServices.CurrentTriggerTypes.Contains("twitch.channel.raid.received") && PrototypeServices.CurrentTriggerTypes.Contains("twitch.channel.cheer.received"), "Switching workflows must retain all event types");
Check(canvas.Children.OfType<Border>().Count(border => (border.Tag as string)?.StartsWith("__trigger__:", StringComparison.Ordinal) == true) == 2, "Switching workflows must restore event cards");
var selectedActions = (List<string>)typeof(MainWindow).GetField("_selectedActions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
var triggerEdges = new[] { (From: "__trigger__:twitch.channel.raid.received", To: selectedActions[0]) };
var definition = (Automation.Domain.Workflows.WorkflowDefinition)typeof(PrototypeServices).GetMethod("CreatePrototypeWorkflow", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { selectedActions, triggerEdges })!;
var raidBlock = definition.Blocks.Single(block => block.Type == "twitch.channel.raid.received");
Check(definition.Connections.Count == 1 && definition.Connections[0].FromBlockId == raidBlock.Id, "Saved event edge must map to the actual raid trigger without automatic links");
Check(raidBlock.Parameters["triggerKey"]!.ToString() == triggerEdges[0].From, "Draft must retain the trigger key used to restore its arrow");
var raidCard = canvas.Children.OfType<Border>().Single(border => Equals(border.Tag, triggerEdges[0].From));
var targetCard = canvas.Children.OfType<Border>().Single(border => Equals(border.Tag, selectedActions[0]));
var manualLinks = (List<(Border From, Border To)>)typeof(MainWindow).GetField("_manualLinks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
manualLinks.Add((raidCard, targetCard));
Invoke(window, "UpdateDynamicLinks");
var linksBeforeDelete = manualLinks.Count;
Check(raidCard.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Tag, "deleteEventButton")), "Additional events must have a visible delete button");
window.DeleteAdditionalTrigger(raidCard);
Check(!PrototypeServices.CurrentTriggerTypes.Contains("twitch.channel.raid.received") && manualLinks.Count == linksBeforeDelete - 1, "Deleting an event must remove its trigger and its links");
Invoke(window, "OnUndoClick", null, new RoutedEventArgs());
Check(PrototypeServices.CurrentTriggerTypes.Contains("twitch.channel.raid.received") && manualLinks.Count == linksBeforeDelete, "Undo must restore the event and its arrow");
Invoke(window, "OnRedoClick", null, new RoutedEventArgs());
Check(!PrototypeServices.CurrentTriggerTypes.Contains("twitch.channel.raid.received") && manualLinks.Count == linksBeforeDelete - 1, "Redo must remove the event and its arrow again");
window.AddCanvasAction("Enregistrement OBS");
Dispatcher.UIThread.RunJobs();
var recordCard = canvas.Children.OfType<Border>().Last(border => border.Classes.Contains("dynamicAction"));
var recordPicker = recordCard.GetVisualDescendants().OfType<ComboBox>().Single();
recordPicker.SelectedIndex = 2;
var recordKey = (string)recordCard.Tag!;
Check(PrototypeServices.ActionOptions[recordKey]["operation"]!.ToString() == "pause", "Recording operation must be stored per action instance");
window.ApplyLanguage(false); Dispatcher.UIThread.RunJobs();
Check(recordPicker.ItemsSource!.Cast<string>().Contains("Mettre en pause") && recordPicker.SelectedIndex == 2, "OBS operation captions must translate without changing the chosen operation");
var navigationWindow = new MainWindow(false);
navigationWindow.Show(); Dispatcher.UIThread.RunJobs();
var navigationCanvas = navigationWindow.FindControl<Canvas>("WorkflowCanvas")!;
var navigationViewport = (Grid)navigationCanvas.Parent!;
Check(navigationViewport.Background is Avalonia.Media.DrawingBrush, "Grid background must belong to the untransformed viewport");
var panPoint = navigationViewport.TranslatePoint(new Point(650, 350), navigationWindow)!.Value;
navigationWindow.MouseDown(panPoint, MouseButton.Left);
navigationWindow.MouseMove(panPoint + new Vector(80, 40));
navigationWindow.MouseUp(panPoint + new Vector(80, 40), MouseButton.Left);
var panMatrix = navigationCanvas.RenderTransform!.Value;
Check(Math.Abs(panMatrix.M31 - 80) < 1 && Math.Abs(panMatrix.M32 - 40) < 1, "Dragging empty canvas with the left mouse button must pan the view");
Invoke(navigationWindow, "OnZoomOutClick", null, new RoutedEventArgs());
Check(navigationViewport.Background is Avalonia.Media.DrawingBrush && navigationCanvas.RenderTransformOrigin == new RelativePoint(0, 0, RelativeUnit.Relative), "Zooming out must preserve a full viewport grid and use a stable transform origin");
navigationWindow.AddCanvasAction("Attendre"); Dispatcher.UIThread.RunJobs();
Invoke(navigationWindow, "OnCenterCanvasClick", null, new RoutedEventArgs()); Dispatcher.UIThread.RunJobs();
var centered = navigationCanvas.Children.OfType<Border>().Single(border => border.Classes.Contains("dynamicAction"));
var centerPoint = centered.TranslatePoint(new Point(centered.Bounds.Width / 2, centered.Bounds.Height / 2), navigationViewport)!.Value;
Check(Math.Abs(centerPoint.X - navigationViewport.Bounds.Width / 2) < 2 && Math.Abs(centerPoint.Y - navigationViewport.Bounds.Height / 2 - 20) < 2, "Center must position the actual workflow in the center of the viewport");
Invoke(navigationWindow, "OnNewWorkflowClick", null, new RoutedEventArgs());
navigationWindow.AddCanvasEvent("manual.run"); navigationWindow.AddCanvasEvent("twitch.channel.raid.received"); navigationWindow.AddCanvasAction("Attendre");
Dispatcher.UIThread.RunJobs();
var firstEvent = navigationCanvas.Children.OfType<Border>().Single(border => Equals(border.Tag, "__trigger__"));
Check(firstEvent.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Tag, "deleteEventButton")), "The first event must have its own delete button");
var otherEvent = navigationCanvas.Children.OfType<Border>().Single(border => Equals(border.Tag, "__trigger__:twitch.channel.raid.received"));
Invoke(navigationWindow, "OnCenterCanvasClick", null, new RoutedEventArgs()); Dispatcher.UIThread.RunJobs();
foreach (var movableEvent in new[] { firstEvent, otherEvent })
{
    var before = new Point(Canvas.GetLeft(movableEvent), Canvas.GetTop(movableEvent));
    var eventPoint = movableEvent.TranslatePoint(new Point(20, 10), navigationWindow)!.Value;
    var scale = navigationCanvas.RenderTransform!.Value.M11;
    navigationWindow.MouseDown(eventPoint, MouseButton.Left); navigationWindow.MouseMove(eventPoint + new Vector(40, 20)); navigationWindow.MouseUp(eventPoint + new Vector(40, 20), MouseButton.Left);
    Check(Math.Abs(Canvas.GetLeft(movableEvent) - before.X - 40 / scale) < 1 && Math.Abs(Canvas.GetTop(movableEvent) - before.Y - 20 / scale) < 1, "Every event must support mouse dragging at the current zoom");
}
var raidPosition = new Point(Canvas.GetLeft(otherEvent), Canvas.GetTop(otherEvent));
var navigationAction = navigationCanvas.Children.OfType<Border>().Single(border => border.Classes.Contains("dynamicAction"));
var navigationLinks = (List<(Border From, Border To)>)typeof(MainWindow).GetField("_manualLinks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(navigationWindow)!;
navigationLinks.Add((otherEvent, navigationAction));
navigationWindow.DeleteAdditionalTrigger(firstEvent);
Check(PrototypeServices.CurrentTriggerTypes.SequenceEqual(new[] { "twitch.channel.raid.received" }) && navigationLinks.Count == 1 && Equals(navigationLinks[0].From.Tag, "__trigger__"), "Removing the first event must preserve the next event and its links");
Check(new Point(Canvas.GetLeft(navigationLinks[0].From), Canvas.GetTop(navigationLinks[0].From)) == raidPosition, "Promoting the remaining event must retain its dragged position");
Invoke(navigationWindow, "OnUndoClick", null, new RoutedEventArgs());
Check(PrototypeServices.CurrentTriggerTypes.Count == 2 && navigationLinks.Count == 1 && Equals(navigationLinks[0].From.Tag, "__trigger__:twitch.channel.raid.received"), "Undo must restore both events with the original link");
Invoke(navigationWindow, "OnRedoClick", null, new RoutedEventArgs());
navigationWindow.DeleteAdditionalTrigger(navigationCanvas.Children.OfType<Border>().Single(border => Equals(border.Tag, "__trigger__")));
Check(PrototypeServices.CurrentTriggerTypes.Count == 0 && navigationLinks.Count == 0 && !navigationCanvas.Children.OfType<Border>().Any(border => Equals(border.Tag, "__trigger__")), "The last event must also be removable without recreating Manual run");
navigationWindow.Close();
var reconnectAttempts = new List<bool>();
AutoReconnect.ObsEnabled = true; AutoReconnect.TwitchEnabled = true; AutoReconnect.ObsPaused = false; AutoReconnect.TwitchPaused = false;
AutoReconnect.RetryAsync(_ => false, twitch => { reconnectAttempts.Add(twitch); return Task.CompletedTask; }).GetAwaiter().GetResult();
Check(reconnectAttempts.SequenceEqual(new[] { false, true }), "Automatic retry must reconnect disconnected OBS and Twitch");
reconnectAttempts.Clear(); AutoReconnect.ObsPaused = true;
AutoReconnect.RetryAsync(_ => false, twitch => { reconnectAttempts.Add(twitch); return Task.CompletedTask; }).GetAwaiter().GetResult();
Check(reconnectAttempts.SequenceEqual(new[] { true }), "Manual OBS disconnection must prevent automatic reconnection");
reconnectAttempts.Clear(); AutoReconnect.TwitchEnabled = false;
AutoReconnect.RetryAsync(_ => false, twitch => { reconnectAttempts.Add(twitch); return Task.CompletedTask; }).GetAwaiter().GetResult();
Check(reconnectAttempts.Count == 0, "Disabled automatic retry must not initiate a connection");
AutoReconnect.ObsPaused = false; AutoReconnect.TwitchEnabled = true;
AutoReconnect.RetryAsync(_ => true, twitch => { reconnectAttempts.Add(twitch); return Task.CompletedTask; }).GetAwaiter().GetResult();
Check(reconnectAttempts.Count == 0, "Active connections must not be replaced by automatic retry");
AutoReconnect.ObsEnabled = false; AutoReconnect.TwitchEnabled = false;
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
