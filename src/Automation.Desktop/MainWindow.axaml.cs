using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using System.Net.Http;
using Avalonia.Threading;
using System.IO;

namespace Automation.Desktop;

public sealed partial class MainWindow : Window
{
    private bool _english;
    private int _addedActionCount;
    private readonly List<string> _selectedActions = new();
    private readonly Stack<CanvasSnapshot> _undoActions = new();
    private readonly Stack<CanvasSnapshot> _redoActions = new();
    private CanvasSnapshot? _dragStartSnapshot;
    private sealed record CanvasSnapshot(string[] Actions, Dictionary<string, Point> Positions,
        (string From, string To)[] Links, Dictionary<string, System.Text.Json.Nodes.JsonObject> Options, Dictionary<string, bool> Visibility);
    private Border? _draggedNode;
    private Border? _selectedNode;
    private readonly List<Border> _dynamicNodes = new();
    private Border? _triggerNode;
    private readonly List<Border> _additionalTriggerNodes = new();
    private readonly Dictionary<Border, Func<Task>> _obsCardRefresh = new();
    private readonly List<Avalonia.Controls.Shapes.Path> _dynamicLinks = new();
    private readonly List<(Border From, Border To)> _manualLinks = new();
    private readonly Dictionary<Avalonia.Controls.Shapes.Path, (Border From, Border To)> _linkMap = new();
    private readonly List<string> _workflowNames = new() { "Mon Live Automatisation" };
    private readonly Dictionary<string, List<string>> _workflowActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string From, string To)>> _workflowLinks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Point>> _workflowPositions = new(StringComparer.OrdinalIgnoreCase);
    private string _activeWorkflowName = "Mon Live Automatisation";
    private bool _switchingWorkflow;
    private readonly Dictionary<string, Guid> _workflowIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _enabledWorkflowIds = new();
    private readonly Dictionary<Guid, string> _workflowTriggerKinds = new();
    private Point _dragPointerStart;
    private double _dragNodeLeft;
    private double _dragNodeTop;
    private double _canvasZoom = 1;
    private Point _canvasOffset;
    private Point? _panStart;
    private Point _panOffset;

    public MainWindow() : this(true) { }

    internal MainWindow(bool restoreSession)
    {
        InitializeComponent();
        var viewport = (Grid)WorkflowCanvas.Parent!;
        WorkflowCanvas.Children.Remove(WorkflowToolbar);
        WorkflowCanvas.Children.Remove(CanvasControls);
        viewport.Children.Add(WorkflowToolbar);
        viewport.Children.Add(CanvasControls);
        WorkflowToolbar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        WorkflowToolbar.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        WorkflowToolbar.Margin = new Thickness(18);
        WorkflowToolbar.ZIndex = 20;
        CanvasControls.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        CanvasControls.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        CanvasControls.Margin = new Thickness(18);
        CanvasControls.ZIndex = 20;
        viewport.PointerWheelChanged += OnCanvasWheel;
        viewport.PointerPressed += OnCanvasPanPressed;
        viewport.PointerMoved += OnCanvasPanMoved;
        viewport.PointerReleased += OnCanvasPanReleased;
        _workflowIds[_activeWorkflowName] = PrototypeServices.CurrentWorkflowId;
        WorkflowTriggerSelector.ItemsSource = new[] { "Manuel", "Nouveau follower" };
        WorkflowTriggerSelector.SelectedIndex = 0;
        WorkflowSelector.ItemsSource = _workflowNames.ToArray();
        foreach (var node in new[] { EventNode, ConditionNode, ObsAlertNode })
        {
            node.PointerPressed += OnActionNodePressed;
            node.PointerMoved += OnActionNodeMoved;
            node.PointerReleased += OnActionNodeReleased;
            node.Tag = node == EventNode ? "New follow" : node == ConditionNode ? "Is a subscriber?" : "Show an OBS alert";
        }
        foreach (var demoNode in this.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("node")))
            demoNode.IsVisible = false;
        foreach (var demoPath in this.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>())
            demoPath.IsVisible = false;
        if (restoreSession) Opened += OnMainWindowOpened;
        else Opened += (_, _) => HidePrototypeNodes();
    }

    private async void OnMainWindowOpened(object? sender, EventArgs eventArgs)
    {
        try
        {
            HidePrototypeNodes();
            var language = await ConnectionPersistence.GetLanguageAsync();
            ApplyLanguage(language == "English");
            var drafts = await PrototypeServices.GetDraftsAsync();
            _enabledWorkflowIds.UnionWith(await PrototypeServices.GetEnabledWorkflowIdsAsync());
            if (drafts.Count > 0)
            {
                _switchingWorkflow = true;
                _workflowNames.Clear();
                _workflowIds.Clear();
                foreach (var draft in drafts)
                {
                    var name = draft.Name;
                    if (_workflowIds.ContainsKey(name)) name += $" ({draft.Id.ToString()[..8]})";
                    _workflowNames.Add(name);
                    _workflowIds[name] = draft.Id;
                    _workflowTriggerKinds[draft.Id] = draft.Blocks.Any(block => block.Type == "twitch.follow.received") ? "twitch.follow.received" : "manual.run";
                    PrototypeServices.CurrentTriggerTypes.Clear();
                    PrototypeServices.CurrentTriggerTypes.AddRange(draft.Blocks.Where(block => block.Type is "manual.run" or "twitch.follow.received").Select(block => block.Type).Distinct());
                    _workflowActions[name] = draft.Blocks.Select(block => block.Parameters["label"]?.GetValue<string>()).OfType<string>().ToList();
                    var labels = draft.Blocks.Where(block => block.Parameters["label"] is not null).ToDictionary(block => block.Id, block => block.Parameters["label"]!.GetValue<string>());
                    _workflowLinks[name] = draft.Connections.Where(link => labels.ContainsKey(link.FromBlockId) && labels.ContainsKey(link.ToBlockId)).Select(link => (labels[link.FromBlockId], labels[link.ToBlockId])).ToList();
                    _workflowPositions[name] = new Dictionary<string, Point>(await ConnectionPersistence.GetWorkflowPositionsAsync(name));
                }
                _activeWorkflowName = _workflowNames[0];
                PrototypeServices.CurrentWorkflowId = _workflowIds[_activeWorkflowName];
                PrototypeServices.CurrentWorkflowName = _activeWorkflowName;
                PrototypeServices.CurrentTriggerType = _workflowTriggerKinds[PrototypeServices.CurrentWorkflowId];
                WorkflowTriggerSelector.SelectedIndex = PrototypeServices.CurrentTriggerType == "manual.run" ? 0 : 1;
                WorkflowSelector.ItemsSource = _workflowNames.ToArray();
                WorkflowSelector.SelectedIndex = 0;
                _switchingWorkflow = false;
            }
            if (await PrototypeServices.HasPrototypeDraftAsync())
            {
                AddTriggerNode();
                foreach (var trigger in PrototypeServices.CurrentTriggerTypes.Skip(1)) AddAdditionalTriggerNode(trigger);
                foreach (var action in await PrototypeServices.GetDraftActionLabelsAsync())
                {
                    _selectedActions.Add(action);
                    AddActionNode(action);
                }
                foreach (var (from, to) in await PrototypeServices.GetDraftManualLinksAsync())
                {
                    var source = _dynamicNodes.FirstOrDefault(node => string.Equals(node.Tag as string, from, StringComparison.OrdinalIgnoreCase));
                    var target = _dynamicNodes.FirstOrDefault(node => string.Equals(node.Tag as string, to, StringComparison.OrdinalIgnoreCase));
                    if (source is not null && target is not null && !_manualLinks.Contains((source, target)))
                        _manualLinks.Add((source, target));
                }
                UpdateDynamicLinks();
                TestStatus.Text = _english ? "Draft restored" : "Brouillon restauré";
                var savedPositions = await ConnectionPersistence.GetWorkflowPositionsAsync(_activeWorkflowName);
                foreach (var node in _dynamicNodes)
                    if (node.Tag is string label && savedPositions.TryGetValue(label, out var position)) { Canvas.SetLeft(node, position.X); Canvas.SetTop(node, position.Y); }
                UpdateDynamicLinks();
            }
            var publishedRevision = await PrototypeServices.GetPublishedRevisionAsync();
            if (publishedRevision is not null)
            {
                TestStatus.Text = _english
                    ? $"Automation active · revision {publishedRevision}"
                    : $"Automatisation active · révision {publishedRevision}";
                StartWorkflowButton.Content = _english ? "■  Stop" : "■  Arrêter";
            }
        }
        catch
        {
            ApplyLanguage(false);
        }
    }

    private void HidePrototypeNodes()
    {
        foreach (var demoNode in this.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("node")))
            demoNode.IsVisible = false;
        foreach (var demoPath in this.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>())
            demoPath.IsVisible = false;
    }

    private void OnNewWorkflowClick(object? sender, RoutedEventArgs eventArgs)
    {
        _switchingWorkflow = true;
        var name = _english ? $"New automation {_workflowNames.Count + 1}" : $"Nouvelle automatisation {_workflowNames.Count + 1}";
        while (_workflowNames.Contains(name)) name += " +";
        _workflowActions[_activeWorkflowName] = _selectedActions.ToList();
        _workflowLinks[_activeWorkflowName] = GetManualLinkLabels().ToList();
        _workflowPositions[_activeWorkflowName] = _dynamicNodes.Where(node => node.Tag is string).ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node)));
        _workflowNames.Add(name);
        _workflowIds[name] = Guid.NewGuid();
        PrototypeServices.CurrentWorkflowId = _workflowIds[name];
        PrototypeServices.CurrentWorkflowName = name;
        PrototypeServices.CurrentTriggerType = "manual.run";
        PrototypeServices.CurrentTriggerTypes.Clear(); PrototypeServices.CurrentTriggerTypes.Add("manual.run");
        _workflowTriggerKinds[_workflowIds[name]] = "manual.run";
        WorkflowTriggerSelector.SelectedIndex = 0;
        _workflowActions[name] = new List<string>();
        _workflowPositions[name] = new Dictionary<string, Point>();
        WorkflowSelector.ItemsSource = _workflowNames.ToArray();
        WorkflowSelector.SelectedIndex = _workflowNames.Count - 1;
        _activeWorkflowName = name;
        ClearWorkflowCanvas();
        StartWorkflowButton.Content = _english ? "▶  Start" : "▶  Démarrer";
        _switchingWorkflow = false;
        TestStatus.Text = _english ? "New automation created" : "Nouvelle automatisation créée";
    }

    private void OnWorkflowSelected(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_switchingWorkflow) return;
        if (WorkflowSelector is not null && WorkflowSelector.SelectedItem is string name)
        {
            if (_workflowNames.Contains(name))
            {
                _workflowActions[_activeWorkflowName] = _selectedActions.ToList();
                _workflowLinks[_activeWorkflowName] = GetManualLinkLabels().ToList();
                _workflowPositions[_activeWorkflowName] = _dynamicNodes.Where(node => node.Tag is string).ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node)));
                ClearWorkflowCanvas();
                foreach (var action in _workflowActions.TryGetValue(name, out var actions) ? actions : [])
                {
                    _selectedActions.Add(action);
                    AddActionNode(action);
                }
                if (_workflowPositions.TryGetValue(name, out var positions))
                    foreach (var node in _dynamicNodes)
                        if (node.Tag is string label && positions.TryGetValue(label, out var position)) { Canvas.SetLeft(node, position.X); Canvas.SetTop(node, position.Y); }
                if (_workflowLinks.TryGetValue(name, out var savedLinks))
                    foreach (var (from, to) in savedLinks)
                    {
                        var source = _dynamicNodes.FirstOrDefault(node => Equals(node.Tag, from));
                        var target = _dynamicNodes.FirstOrDefault(node => Equals(node.Tag, to));
                        if (source is not null && target is not null) _manualLinks.Add((source, target));
                    }
                UpdateDynamicLinks();
                _activeWorkflowName = name;
                if (!_workflowIds.ContainsKey(name)) _workflowIds[name] = PrototypeServices.CurrentWorkflowId;
                PrototypeServices.CurrentWorkflowId = _workflowIds[name];
                PrototypeServices.CurrentWorkflowName = name;
                PrototypeServices.CurrentTriggerType = _workflowTriggerKinds.GetValueOrDefault(PrototypeServices.CurrentWorkflowId, "manual.run");
                PrototypeServices.CurrentTriggerTypes.Clear(); PrototypeServices.CurrentTriggerTypes.Add(PrototypeServices.CurrentTriggerType);
                var switching = _switchingWorkflow;
                _switchingWorkflow = true;
                WorkflowTriggerSelector.SelectedIndex = PrototypeServices.CurrentTriggerType == "manual.run" ? 0 : 1;
                _switchingWorkflow = switching;
                StartWorkflowButton.Content = _enabledWorkflowIds.Contains(PrototypeServices.CurrentWorkflowId)
                    ? (_english ? "■  Stop" : "■  Arrêter") : (_english ? "▶  Start" : "▶  Démarrer");
            }
            TestStatus.Text = _english ? $"Editing: {name}" : $"Édition : {name}";
        }
    }

    private void OnTriggerKindChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_switchingWorkflow || WorkflowTriggerSelector is null || WorkflowTriggerSelector.SelectedIndex < 0 || TestStatus is null) return;
        PrototypeServices.CurrentTriggerType = WorkflowTriggerSelector.SelectedIndex == 0 ? "manual.run" : "twitch.follow.received";
        PrototypeServices.CurrentTriggerTypes.Clear(); PrototypeServices.CurrentTriggerTypes.Add(PrototypeServices.CurrentTriggerType);
        _workflowTriggerKinds[PrototypeServices.CurrentWorkflowId] = PrototypeServices.CurrentTriggerType;
        UpdateTriggerNode();
        TestStatus.Text = _english ? "Trigger changed — save and publish to apply" : "Déclencheur modifié — enregistre et publie pour appliquer";
    }

    private async void OnAddCanvasEventClick(object? sender, RoutedEventArgs eventArgs)
    {
        var dialog = new Window { Title = _english ? "Add event" : "Ajouter un événement", Width = 360, Height = 190, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var manual = new Button { Content = _english ? "▶  Manual run" : "▶  Exécution manuelle", HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var follower = new Button { Content = _english ? "♥  New Twitch follower" : "♥  Nouveau follower Twitch", HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        manual.Click += (_, _) => dialog.Close("manual.run"); follower.Click += (_, _) => dialog.Close("twitch.follow.received");
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { new TextBlock { Text = _english ? "Choose an event to add to this canvas" : "Choisis un événement à ajouter à ce canevas", TextWrapping = TextWrapping.Wrap }, manual, follower } };
        var trigger = await dialog.ShowDialog<string?>(this);
        if (trigger is not ("manual.run" or "twitch.follow.received")) return;
        if (PrototypeServices.CurrentTriggerTypes.Contains(trigger))
        {
            TestStatus.Text = _english ? "This event is already on the canvas" : "Cet événement est déjà dans le canevas";
            return;
        }
        PrototypeServices.CurrentTriggerTypes.Add(trigger);
        PrototypeServices.CurrentTriggerType = trigger;
        _workflowTriggerKinds[PrototypeServices.CurrentWorkflowId] = trigger;
        AddAdditionalTriggerNode(trigger);
        TestStatus.Text = _english ? "Event added — connect it to an action" : "Événement ajouté — relie-le à une action";
    }

    private async void OnAddCanvasItemClick(object? sender, RoutedEventArgs eventArgs)
    {
        var dialog = new Window { Title = _english ? "Add to canvas" : "Ajouter au canevas", Width = 420, Height = 520, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(18), Spacing = 7 };
        stack.Children.Add(new TextBlock { Text = _english ? "Events" : "Événements", FontWeight = FontWeight.Bold });
        foreach (var item in new[] { ("manual.run", "▶  Exécution manuelle", "▶  Manual run"), ("twitch.follow.received", "♥  Nouveau follower Twitch", "♥  New Twitch follower") })
        { var button = new Button { Content = _english ? item.Item3 : item.Item2, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left }; button.Click += (_, _) => dialog.Close(item.Item1); stack.Children.Add(button); }
        foreach (var group in new[] {
            ("Twitch", new[] { ("Envoyer un message", "Send a message"), ("Créer un clip", "Create a clip"), ("Modifier un titre", "Edit title"), ("Gérer les points de chaîne", "Manage channel points"), ("Récupérer un utilisateur", "Get a user") }),
            ("OBS Studio", new[] { ("Changer de scène", "Change scene"), ("Visibilité d’une source", "Source visibility"), ("Démarrer un replay", "Start replay"), ("Prendre une capture", "Take a screenshot") }),
            ("CORE", new[] { ("Ajouter une condition", "Add a condition"), ("Attendre", "Wait"), ("Modifier une variable", "Edit a variable"), ("Jouer un son", "Play a sound") }) })
        {
            stack.Children.Add(new TextBlock { Text = group.Item1, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 10, 0, 0) });
            foreach (var item in group.Item2)
            { var button = new Button { Content = _english ? item.Item2 : item.Item1, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left }; button.Click += (_, _) => dialog.Close($"action:{item.Item1}"); stack.Children.Add(button); }
        }
        dialog.Content = new ScrollViewer { Content = stack };
        var result = await dialog.ShowDialog<string?>(this);
        if (result is null) return;
        if (result.StartsWith("action:", StringComparison.Ordinal))
        {
            var label = result[7..]; var instance = $"{label}\u001f{Guid.NewGuid():N}"; RememberCanvas(); _selectedActions.Add(instance); AddActionNode(instance); UpdateHistoryButtons();
            TestStatus.Text = _english ? "Action added — connect it in the canvas" : "Action ajoutée — relie-la dans le canevas";
        }
        else if (result is "manual.run" or "twitch.follow.received")
        {
            if (PrototypeServices.CurrentTriggerTypes.Contains(result)) { TestStatus.Text = _english ? "This event is already on the canvas" : "Cet événement est déjà dans le canevas"; return; }
            PrototypeServices.CurrentTriggerTypes.Add(result); AddAdditionalTriggerNode(result);
            TestStatus.Text = _english ? "Event added — connect it in the canvas" : "Événement ajouté — relie-le dans le canevas";
        }
    }

    private void OnActionsTabPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed) return;
        EventLibrarySection.IsVisible = false;
        foreach (var section in this.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("productSection") && b != EventLibrarySection)) section.IsVisible = true;
        ActionsTab.Background = new SolidColorBrush(Color.Parse("#4A2A7D"));
        EventsTab.Background = Brushes.Transparent;
    }

    private void OnEventsTabPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed) return;
        foreach (var section in this.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("productSection") && b != EventLibrarySection)) section.IsVisible = false;
        EventLibrarySection.IsVisible = true;
        EventsTab.Background = new SolidColorBrush(Color.Parse("#4A2A7D"));
        ActionsTab.Background = Brushes.Transparent;
    }

    private async void OnRenameWorkflowClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (WorkflowSelector.SelectedIndex < 0) return;
        var current = _workflowNames[WorkflowSelector.SelectedIndex];
        var input = new TextBox { Text = current, MinWidth = 320, PlaceholderText = _english ? "Automation name" : "Nom de l’automatisation" };
        var dialog = new Window
        {
            Title = _english ? "Rename automation" : "Renommer l’automatisation",
            Width = 420, Height = 170, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 14, Children =
            {
                input,
                new Button { Content = _english ? "Confirm" : "Confirmer", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
            }}
        };
        ((Button)((StackPanel)dialog.Content!).Children[1]).Click += (_, _) => dialog.Close(input.Text?.Trim());
        var result = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(result))
        {
            if (!string.Equals(current, result, StringComparison.OrdinalIgnoreCase) && _workflowNames.Contains(result, StringComparer.OrdinalIgnoreCase))
            {
                TestStatus.Text = _english ? "This name is already used" : "Ce nom est déjà utilisé";
                return;
            }
            _switchingWorkflow = true;
            _workflowActions[current] = _selectedActions.ToList();
            _workflowLinks[current] = GetManualLinkLabels().ToList();
            _workflowPositions[current] = _dynamicNodes.Where(node => node.Tag is string).ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node)));
            _workflowNames[WorkflowSelector.SelectedIndex] = result;
            var oldName = current;
            if (_workflowActions.TryGetValue(oldName, out var actions))
            {
                _workflowActions.Remove(oldName);
                _workflowActions[result] = actions;
            }
            WorkflowSelector.ItemsSource = _workflowNames.ToArray();
            WorkflowSelector.SelectedItem = result;
            if (_workflowLinks.Remove(current, out var links)) _workflowLinks[result] = links;
            if (_workflowPositions.Remove(current, out var positions)) _workflowPositions[result] = positions;
            _activeWorkflowName = result;
            if (_workflowIds.Remove(current, out var id)) _workflowIds[result] = id;
            PrototypeServices.CurrentWorkflowName = result;
            _switchingWorkflow = false;
            await PrototypeServices.SavePrototypeDraftAsync(_selectedActions, GetManualLinkLabels());
            await ConnectionPersistence.SaveWorkflowPositionsAsync(result, _workflowPositions[result]);
            TestStatus.Text = _english ? "Automation renamed" : "Automatisation renommée";
        }
    }

    private async void OnDeleteWorkflowClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_workflowNames.Count <= 1 || WorkflowSelector.SelectedIndex < 0)
        {
            TestStatus.Text = _english ? "Keep at least one automation" : "Garde au moins une automatisation";
            return;
        }
        var currentWorkflowName = _workflowNames[WorkflowSelector.SelectedIndex];
        if (_workflowIds.TryGetValue(currentWorkflowName, out var deletedId))
            await PrototypeServices.DeleteWorkflowAsync(deletedId);
        _workflowNames.Remove(currentWorkflowName);
        _workflowActions.Remove(currentWorkflowName);
        _workflowLinks.Remove(currentWorkflowName);
        _workflowPositions.Remove(currentWorkflowName);
        _workflowIds.Remove(currentWorkflowName);
        ClearWorkflowCanvas();
        _activeWorkflowName = _workflowNames[0];
        WorkflowSelector.ItemsSource = _workflowNames.ToArray();
        WorkflowSelector.SelectedIndex = 0;
        TestStatus.Text = _english ? "Automation deleted" : "Automatisation supprimée";
    }

    private void ClearWorkflowCanvas()
    {
        _obsCardRefresh.Clear();
        _selectedActions.Clear();
        _manualLinks.Clear();
        _dynamicNodes.Clear();
        foreach (var link in _dynamicLinks) WorkflowCanvas.Children.Remove(link);
        _dynamicLinks.Clear();
        foreach (var node in WorkflowCanvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray())
            WorkflowCanvas.Children.Remove(node);
        _addedActionCount = 0;
        _linkMap.Clear();
        _selectedNode = null;
        foreach (var trigger in _additionalTriggerNodes) WorkflowCanvas.Children.Remove(trigger);
        _additionalTriggerNodes.Clear();
        AddTriggerNode();
        _undoActions.Clear();
        _redoActions.Clear();
        UpdateHistoryButtons();
    }

    private void OnActionSearchChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        if (ActionSearch is null || WorkflowCanvas is null) return;
        var query = ActionSearch.Text?.Trim() ?? "";
        foreach (var item in this.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("libraryItem")))
            item.IsVisible = item.Child is TextBlock text && (query.Length == 0 || (text.Text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false));
    }

    private void ApplyCanvasTransform()
    {
        WorkflowCanvas.RenderTransform = new MatrixTransform(new Matrix(_canvasZoom, 0, 0, _canvasZoom, _canvasOffset.X, _canvasOffset.Y));
        CanvasZoomLabel.Text = $"{_canvasZoom * 100:0} %";
    }

    private void SetCanvasZoom(double zoom, Point? anchor = null)
    {
        var target = Math.Clamp(zoom, 0.35, 2);
        var viewport = (Grid)WorkflowCanvas.Parent!;
        var focus = anchor ?? new Point(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2);
        var logical = new Point((focus.X - _canvasOffset.X) / _canvasZoom, (focus.Y - _canvasOffset.Y) / _canvasZoom);
        _canvasOffset = new Point(focus.X - logical.X * target, focus.Y - logical.Y * target);
        _canvasZoom = target;
        ApplyCanvasTransform();
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs args) => SetCanvasZoom(_canvasZoom + 0.1);
    private void OnZoomOutClick(object? sender, RoutedEventArgs args) => SetCanvasZoom(_canvasZoom - 0.1);
    private void OnCanvasWheel(object? sender, PointerWheelEventArgs args)
    {
        if ((args.KeyModifiers & KeyModifiers.Control) == 0) return;
        SetCanvasZoom(_canvasZoom + Math.Sign(args.Delta.Y) * 0.1, args.GetPosition((Visual)WorkflowCanvas.Parent!));
        args.Handled = true;
    }

    private void OnCenterCanvasClick(object? sender, RoutedEventArgs args)
    {
        if (_dynamicNodes.Count == 0) { _canvasZoom = 1; _canvasOffset = default; ApplyCanvasTransform(); return; }
        var left = _dynamicNodes.Min(Canvas.GetLeft);
        var top = _dynamicNodes.Min(Canvas.GetTop);
        var right = _dynamicNodes.Max(node => Canvas.GetLeft(node) + node.Width);
        var bottom = _dynamicNodes.Max(node => Canvas.GetTop(node) + node.Height);
        var viewport = (Grid)WorkflowCanvas.Parent!;
        _canvasZoom = Math.Clamp(Math.Min((viewport.Bounds.Width - 80) / (right - left), (viewport.Bounds.Height - 150) / (bottom - top)), 0.35, 1);
        _canvasOffset = new Point((viewport.Bounds.Width - (right + left) * _canvasZoom) / 2,
            (viewport.Bounds.Height - (bottom + top) * _canvasZoom) / 2 + 20);
        ApplyCanvasTransform();
    }

    private void OnCanvasPanPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(WorkflowCanvas).Properties.IsMiddleButtonPressed) return;
        _panStart = args.GetPosition((Visual)WorkflowCanvas.Parent!);
        _panOffset = _canvasOffset;
        args.Pointer.Capture(WorkflowCanvas);
        args.Handled = true;
    }
    private void OnCanvasPanMoved(object? sender, PointerEventArgs args)
    {
        if (_panStart is not { } start) return;
        var point = args.GetPosition((Visual)WorkflowCanvas.Parent!);
        _canvasOffset = _panOffset + (point - start);
        ApplyCanvasTransform();
    }
    private void OnCanvasPanReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_panStart is null) return;
        _panStart = null;
        args.Pointer.Capture(null);
        args.Handled = true;
    }

    private void OnTestClick(object? sender, RoutedEventArgs eventArgs)
    {
        var errors = PrototypeServices.ValidateEditorDetails(_selectedActions, GetManualLinkLabels());
        foreach (var node in _dynamicNodes)
        {
            var key = (string)node.Tag!;
            var parts = key.Split('\u001f');
            var nodeErrors = parts.Length > 1 && Guid.TryParse(parts[1], out var id) ? errors.Where(error => error.BlockId == id).ToArray() : [];
            node.BorderBrush = new SolidColorBrush(Color.Parse(nodeErrors.Length > 0 ? "#FF5368" : GetActionPalette(key).Border));
            ToolTip.SetTip(node, nodeErrors.Length > 0 ? string.Join(" · ", nodeErrors.Select(error => error.Message)) : null);
        }
        TestStatus.Text = errors.Count > 0 ? string.Join(" · ", errors.Select(error => error.Message))
            : _english ? $"Structure checked · {_selectedActions.Count} cards · no real actions executed"
            : $"Structure vérifiée · {_selectedActions.Count} cartes · aucune action réelle exécutée";
    }

    private async void OnSaveWorkflowClick(object? sender, RoutedEventArgs eventArgs)
    {
        var name = _activeWorkflowName;
        var positions = _dynamicNodes.Where(node => node.Tag is string).ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node)));
        try
        {
            await PrototypeServices.SavePrototypeDraftAsync(_selectedActions, GetManualLinkLabels());
            await ConnectionPersistence.SaveWorkflowPositionsAsync(name, positions);
            TestStatus.Text = _english ? "Draft saved" : "Brouillon enregistré";
        }
        catch (Exception exception)
        {
            TestStatus.Text = _english ? $"Save failed: {exception.Message}" : $"Échec de sauvegarde : {exception.Message}";
        }
    }

    private async void OnRunOnceClick(object? sender, RoutedEventArgs eventArgs)
    {
        RunOnceButton.IsEnabled = false;
        TestStatus.Text = _english ? "Running published workflow…" : "Exécution du workflow publié…";
        try
        {
            var result = await PrototypeServices.RunPublishedOnceAsync();
            TestStatus.Text = result == Automation.Domain.Execution.ExecutionStatus.Succeeded
                ? (_english ? "Run completed — open History for details" : "Exécution terminée — détails dans l’historique")
                : result == Automation.Domain.Execution.ExecutionStatus.UnknownOutcome
                    ? (_english ? "Outcome uncertain — check OBS/Twitch and History before retrying" : "Résultat incertain — vérifie OBS/Twitch et l’historique avant de réessayer")
                    : (_english ? "Run failed or cancelled — open History" : "Exécution échouée ou annulée — ouvre l’historique");
        }
        catch (Exception exception) { TestStatus.Text = exception.Message; }
        finally { RunOnceButton.IsEnabled = true; }
    }

    private async void OnStartWorkflowClick(object? sender, RoutedEventArgs eventArgs)
    {
        var workflowId = PrototypeServices.CurrentWorkflowId;
        var stopping = _enabledWorkflowIds.Contains(workflowId);
        if (!stopping)
        {
            try
            {
                if (!await PrototypeServices.HasPrototypeDraftAsync())
                {
                    TestStatus.Text = _english ? "Save the draft before starting." : "Enregistre le brouillon avant de démarrer.";
                    return;
                }
                await PrototypeServices.PublishPrototypeAsync(_selectedActions, GetManualLinkLabels());
                _enabledWorkflowIds.Add(workflowId);
            }
            catch (Exception exception)
            {
                TestStatus.Text = _english ? $"Publish failed: {exception.Message}" : $"Échec de publication : {exception.Message}";
                return;
            }
        }
        else
        {
            await PrototypeServices.DisablePrototypeAsync();
            _enabledWorkflowIds.Remove(workflowId);
        }
        if (PrototypeServices.CurrentWorkflowId != workflowId) return;
        TestStatus.Text = stopping
            ? (_english ? "Automation stopped" : "Automatisation arrêtée")
            : PrototypeServices.CurrentTriggerType == "manual.run"
                ? (_english ? "Published — click Run once to execute" : "Version publiée — clique Exécuter pour la lancer")
                : (_english ? "Enabled — waiting for a new follower" : "Activée — en attente d’un nouveau follower");
        StartWorkflowButton.Content = stopping
            ? (_english ? "▶  Start" : "▶  Démarrer")
            : (_english ? "■  Stop" : "■  Arrêter");
    }

    private void OnLibraryActionPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        var border = (eventArgs.Source as Visual)?.GetSelfAndVisualAncestors()
            .OfType<Border>().FirstOrDefault(candidate => candidate.Classes.Contains("libraryItem"));
        if (border is not null && eventArgs.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            if (border.Classes.Contains("eventItem"))
            {
                var trigger = border.Tag as string;
                if (trigger is not ("manual.run" or "twitch.follow.received")) return;
                PrototypeServices.CurrentTriggerType = trigger;
                _workflowTriggerKinds[PrototypeServices.CurrentWorkflowId] = trigger;
                if (!PrototypeServices.CurrentTriggerTypes.Contains(trigger))
                {
                    PrototypeServices.CurrentTriggerTypes.Add(trigger);
                    AddAdditionalTriggerNode(trigger);
                }
                UpdateTriggerNode();
                _switchingWorkflow = true;
                WorkflowTriggerSelector.SelectedIndex = trigger == "manual.run" ? 0 : 1;
                _switchingWorkflow = false;
                TestStatus.Text = _english ? "Trigger selected — save and publish to apply" : "Déclencheur sélectionné — enregistre et publie pour appliquer";
                eventArgs.Handled = true;
                return;
            }
            var label = border.Child is TextBlock textBlock ? textBlock.Text : null;
            if (!string.IsNullOrWhiteSpace(label))
            {
                TestStatus.Text = _english ? $"Action selected: {label}" : $"Action sélectionnée : {label}";
                {
                    // Keep a distinct instance key while displaying the original action name.
                    var instance = $"{label}\u001f{Guid.NewGuid():N}";
                    RememberCanvas();
                    _selectedActions.Add(instance);
                    AddActionNode(instance);
                    UpdateHistoryButtons();
                }
                eventArgs.Handled = true;
            }
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_undoActions.Count == 0) return;
        _redoActions.Push(CaptureCanvas());
        RestoreCanvas(_undoActions.Pop());
        TestStatus.Text = _english ? "Change undone" : "Modification annulée";
        UpdateHistoryButtons();
    }

    private void OnRedoClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_redoActions.Count == 0) return;
        _undoActions.Push(CaptureCanvas());
        RestoreCanvas(_redoActions.Pop());
        TestStatus.Text = _english ? "Change restored" : "Modification rétablie";
        UpdateHistoryButtons();
    }

    private void RebuildActionNodes()
    {
        _obsCardRefresh.Clear();
        var savedLinks = GetManualLinkLabels().ToArray();
        var positions = _dynamicNodes.ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node)));
        foreach (var link in _dynamicLinks) WorkflowCanvas.Children.Remove(link);
        _dynamicLinks.Clear();
        _linkMap.Clear();
        _dynamicNodes.Clear();
        _manualLinks.Clear();
        _selectedNode = null;
        foreach (var child in WorkflowCanvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray())
            WorkflowCanvas.Children.Remove(child);
        _addedActionCount = 0;
        foreach (var trigger in _additionalTriggerNodes) WorkflowCanvas.Children.Remove(trigger);
        _additionalTriggerNodes.Clear();
        AddTriggerNode();
        foreach (var trigger in PrototypeServices.CurrentTriggerTypes.Skip(1)) AddAdditionalTriggerNode(trigger);
        foreach (var action in _selectedActions) AddActionNode(action);
        foreach (var node in _dynamicNodes)
            if (positions.TryGetValue((string)node.Tag!, out var position))
            {
                Canvas.SetLeft(node, position.X);
                Canvas.SetTop(node, position.Y);
            }
        foreach (var (from, to) in savedLinks)
        {
            var source = _dynamicNodes.FirstOrDefault(node => Equals(node.Tag, from));
            var target = _dynamicNodes.FirstOrDefault(node => Equals(node.Tag, to));
            if (source is not null && target is not null) _manualLinks.Add((source, target));
        }
        UpdateDynamicLinks();
    }

    private CanvasSnapshot CaptureCanvas() => new(_selectedActions.ToArray(),
        _dynamicNodes.ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node))),
        GetManualLinkLabels().ToArray(),
        _selectedActions.Where(PrototypeServices.ActionOptions.ContainsKey).ToDictionary(key => key, key => (System.Text.Json.Nodes.JsonObject)PrototypeServices.ActionOptions[key].DeepClone()),
        _selectedActions.ToDictionary(key => key, key => PrototypeServices.SourceVisibility.GetValueOrDefault(key, true)));

    private void RememberCanvas()
    {
        _undoActions.Push(CaptureCanvas());
        _redoActions.Clear();
        UpdateHistoryButtons();
    }

    private void RestoreCanvas(CanvasSnapshot snapshot)
    {
        _selectedActions.Clear(); _selectedActions.AddRange(snapshot.Actions);
        _manualLinks.Clear();
        foreach (var (key, value) in snapshot.Options) PrototypeServices.ActionOptions[key] = (System.Text.Json.Nodes.JsonObject)value.DeepClone();
        foreach (var (key, value) in snapshot.Visibility) PrototypeServices.SourceVisibility[key] = value;
        RebuildActionNodes();
        foreach (var node in _dynamicNodes)
            if (snapshot.Positions.TryGetValue((string)node.Tag!, out var point))
            { Canvas.SetLeft(node, point.X); Canvas.SetTop(node, point.Y); }
        foreach (var (from, to) in snapshot.Links)
        {
            var source = _dynamicNodes.FirstOrDefault(node => Equals(node.Tag, from));
            var target = _dynamicNodes.FirstOrDefault(node => Equals(node.Tag, to));
            if (source is not null && target is not null) _manualLinks.Add((source, target));
        }
        UpdateDynamicLinks();
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = _undoActions.Count > 0;
        RedoButton.IsEnabled = _redoActions.Count > 0;
    }

    private IReadOnlyCollection<(string From, string To)> GetManualLinkLabels() =>
        _manualLinks
            .Where(link => link.From.Tag is string && link.To.Tag is string)
            .Select(link => ((string)link.From.Tag!, (string)link.To.Tag!))
            .ToArray();

    private void AddTriggerNode()
    {
        if (_triggerNode is not null) { UpdateTriggerNode(); return; }
        var triggerPicker = new ComboBox
        {
            ItemsSource = _english ? new[] { "Manual run", "New Twitch follower" } : new[] { "Exécution manuelle", "Nouveau follower Twitch" },
            SelectedIndex = PrototypeServices.CurrentTriggerType == "twitch.follow.received" ? 1 : 0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            FontSize = 12,
            Tag = "triggerPicker"
        };
        _triggerNode = new Border
        {
            Width = 230, Height = 112, Background = new SolidColorBrush(Color.Parse("#151A32")),
            BorderBrush = new SolidColorBrush(Color.Parse("#26C281")), BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(12), IsHitTestVisible = false,
            Child = new StackPanel { Spacing = 10, Children =
            {
                new TextBlock { Text = "⚡  Événement", Foreground = new SolidColorBrush(Color.Parse("#62E6AE")), FontSize = 11 },
                triggerPicker
            }}
        };
        _triggerNode.Tag = "__trigger__";
        _triggerNode.PointerPressed += OnActionNodePressed;
        _triggerNode.PointerReleased += OnActionNodeReleased;
        _triggerNode.IsHitTestVisible = true;
        triggerPicker.SelectionChanged += (_, _) =>
        {
            if (triggerPicker.SelectedIndex < 0) return;
            var trigger = triggerPicker.SelectedIndex == 1 ? "twitch.follow.received" : "manual.run";
            PrototypeServices.CurrentTriggerType = trigger;
            _workflowTriggerKinds[PrototypeServices.CurrentWorkflowId] = trigger;
            var switching = _switchingWorkflow; _switchingWorkflow = true;
            WorkflowTriggerSelector.SelectedIndex = triggerPicker.SelectedIndex;
            _switchingWorkflow = switching;
            TestStatus.Text = _english ? "Trigger changed — save and publish to apply" : "Déclencheur modifié — enregistre et publie pour appliquer";
        };
        Canvas.SetLeft(_triggerNode, 60); Canvas.SetTop(_triggerNode, 80);
        WorkflowCanvas.Children.Add(_triggerNode);
        UpdateTriggerNode();
    }

    private void UpdateTriggerNode()
    {
        if (_triggerNode is null) return;
        if (_triggerNode.Child is StackPanel panel && panel.Children.OfType<ComboBox>().FirstOrDefault() is { } picker)
        {
            var selected = picker.SelectedIndex;
            picker.ItemsSource = _english ? new[] { "Manual run", "New Twitch follower" } : new[] { "Exécution manuelle", "Nouveau follower Twitch" };
            picker.SelectedIndex = selected;
        }
    }

    private void AddAdditionalTriggerNode(string trigger)
    {
        if (_additionalTriggerNodes.Any(node => Equals(node.Tag, $"__trigger__:{trigger}"))) return;
        var title = trigger == "twitch.follow.received"
            ? (_english ? "New Twitch follower" : "Nouveau follower Twitch")
            : (_english ? "Manual run" : "Exécution manuelle");
        var node = new Border { Width = 230, Height = 82, Background = new SolidColorBrush(Color.Parse("#151A32")), BorderBrush = new SolidColorBrush(Color.Parse("#26C281")), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Padding = new Thickness(12), Tag = $"__trigger__:{trigger}" };
        node.Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = "⚡  Événement", Foreground = new SolidColorBrush(Color.Parse("#62E6AE")), FontSize = 11 }, new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 } } };
        node.PointerPressed += OnActionNodePressed; node.PointerReleased += OnActionNodeReleased;
        Canvas.SetLeft(node, 60); Canvas.SetTop(node, 210 + _additionalTriggerNodes.Count * 105);
        WorkflowCanvas.Children.Add(node); _additionalTriggerNodes.Add(node);
    }

    private void AddActionNode(string label)
    {
        var palette = GetActionPalette(label);
        var title = LocalizedActionTitle(label);
        var node = new Border
        {
            Width = 210,
            Height = 100,
            Background = new SolidColorBrush(Color.Parse("#151A32")),
            BorderBrush = new SolidColorBrush(Color.Parse(palette.Border)),
            BorderThickness = new Avalonia.Thickness(2),
            CornerRadius = new Avalonia.CornerRadius(11),
            Padding = new Avalonia.Thickness(0)
        };
        node.Tag = label;
        node.Focusable = true;
        node.KeyDown += OnActionNodeKeyDown;
        node.Classes.Add("dynamicAction");
        node.PointerPressed += OnActionNodePressed;
        node.PointerMoved += OnActionNodeMoved;
        node.PointerReleased += OnActionNodeReleased;
        var deleteButton = new Button
        {
            Content = "×",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Padding = new Avalonia.Thickness(4, 0),
            FontSize = 15,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        ToolTip.SetTip(deleteButton, _english ? "Delete action" : "Supprimer l’action");
        deleteButton.Click += (_, _) => DeleteActionNode(node);
        var header = new Border
        {
            Background = new SolidColorBrush(Color.Parse(palette.Header)),
            CornerRadius = new Avalonia.CornerRadius(9, 9, 0, 0),
            Padding = new Avalonia.Thickness(10, 0),
            Child = new Grid
            {
                Children =
                {
                            new TextBlock { Text = "◆  Action", Foreground = new SolidColorBrush(Color.Parse(palette.Text)), FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                    deleteButton
                }
            }
        };
        node.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                header,
                new StackPanel
                {
                    [Grid.RowProperty] = 1,
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 10,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(12, 0),
                    Children =
                    {
                            new Border
                        {
                            Width = 34,
                            Height = 34,
                            CornerRadius = new Avalonia.CornerRadius(8),
                            Background = new SolidColorBrush(Color.Parse(palette.Icon)),
                            Child = new TextBlock { Text = "◆", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                        },
                        new TextBlock
                        {
                            Text = _english ? title.English : title.French,
                            Tag = (title.French, title.English),
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            MaxWidth = 136,
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        }
                    }
                }
            }
        };
        if (label.Contains("source", StringComparison.OrdinalIgnoreCase))
        {
            node.Height = 167;
            var layout = (Grid)node.Child;
            layout.RowDefinitions = new RowDefinitions("32,*,Auto");
            var visibility = new ComboBox
            {
                Tag = "visibilityOptions",
                ItemsSource = _english ? new[] { "Show", "Hide" } : new[] { "Afficher", "Masquer" },
                SelectedIndex = PrototypeServices.SourceVisibility.GetValueOrDefault(label, true) ? 0 : 1,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Margin = new Thickness(0)
            };
            var visibilityField = new StackPanel { Spacing = 4, Margin = new Thickness(12, 0, 12, 10) };
            visibilityField.Children.Add(CreateFieldLabel("Visibilité", "Visibility"));
            visibilityField.Children.Add(visibility);
            Grid.SetRow(visibilityField, 2);
            visibility.SelectionChanged += (_, _) =>
            {
                if (visibility.SelectedIndex < 0) return;
                PrototypeServices.SourceVisibility[label] = visibility.SelectedIndex == 0;
                TestStatus.Text = _english ? "Draft modified" : "Brouillon modifié";
            };
            layout.Children.Add(visibilityField);
        }
        Canvas.SetLeft(node, 70 + (_addedActionCount % 3) * 230);
        AddInlineActionOptions(node, label);
        Canvas.SetTop(node, 520 + (_addedActionCount / 3) * 165);
        WorkflowCanvas.Children.Add(node);
        _dynamicNodes.Add(node);
        _addedActionCount++;
    }

    private void AddInlineActionOptions(Border node, string label)
    {
        var name = label.Split('\u001f')[0].ToLowerInvariant();
        var fields = new List<(string Key, string Fr, string En)>();
        if (name.Contains("source"))
        {
            fields.Add(("scene", "Scène OBS", "OBS scene"));
            fields.Add(("source", "Nom de la source", "Source name"));
        }
        else if (name.Contains("scene") || name.Contains("scène") || name.Contains("capture") || name.Contains("screenshot")) fields.Add(("scene", "Scène OBS", "OBS scene"));
        else if (name.Contains("wait") || name.Contains("attendre")) fields.Add(("seconds", "Durée en secondes", "Duration in seconds"));
        else if (name.Contains("title") || name.Contains("titre")) fields.Add(("title", "Nouveau titre", "New title"));
        else if (name.Contains("message")) fields.Add(("message", "Message à envoyer", "Message to send"));
        else if (name.Contains("variable"))
        {
            fields.Add(("name", "Nom de la variable", "Variable name"));
            fields.Add(("value", "Valeur", "Value"));
        }
        else if (name.Contains("condition"))
        {
            fields.Add(("name", "Variable entière à comparer", "Integer variable to compare"));
            fields.Add(("value", "Valeur attendue", "Expected value"));
        }
        else if (name.Contains("sound") || name.Contains("son")) fields.Add(("file", "Fichier audio", "Audio file"));
        else if (name.Contains("utilisateur") || name.Contains("user")) fields.Add(("login", "Identifiant Twitch", "Twitch login"));
        if (fields.Count == 0) return;
        if (!PrototypeServices.ActionOptions.TryGetValue(label, out var options))
            PrototypeServices.ActionOptions[label] = options = new System.Text.Json.Nodes.JsonObject();
        var form = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 12, 10) };
        if (name.Contains("condition"))
        {
            form.Children.Add(CreateFieldLabel("Comparaison", "Comparison"));
            var comparison = new ComboBox
            {
                Tag = "conditionComparisons",
                ItemsSource = _english ? new[] { "Equals", "Greater than", "Less than" } : new[] { "Égal à", "Supérieur à", "Inférieur à" },
                SelectedIndex = options["comparison"]?.ToString() switch { "greater" => 1, "less" => 2, _ => 0 },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            comparison.SelectionChanged += (_, _) => { if (comparison.SelectedIndex >= 0) options["comparison"] = comparison.SelectedIndex switch { 1 => "greater", 2 => "less", _ => "equals" }; };
            form.Children.Add(comparison);
            form.Children.Add(CreateFieldLabel("Branche à relier", "Branch to connect"));
            var branch = new ComboBox
            {
                Tag = "conditionBranches",
                ItemsSource = _english ? new[] { "Yes", "No" } : new[] { "Oui", "Non" },
                SelectedIndex = options["linkOutput"]?.ToString() == "no" ? 1 : 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            branch.SelectionChanged += (_, _) => { if (branch.SelectedIndex >= 0) options["linkOutput"] = branch.SelectedIndex == 1 ? "no" : "yes"; };
            form.Children.Add(branch);
            node.Height += 128;
        }
        if (name.Contains("variable"))
        {
            form.Children.Add(CreateFieldLabel("Opération", "Operation"));
            var operation = new ComboBox
            {
                Tag = "variableOperations",
                ItemsSource = _english ? new[] { "Set", "Add" } : new[] { "Définir", "Ajouter" },
                SelectedIndex = options["operation"]?.ToString() == "increment" ? 1 : 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            operation.SelectionChanged += (_, _) =>
            {
                if (operation.SelectedIndex < 0) return;
                options["operation"] = operation.SelectedIndex == 1 ? "increment" : "set";
                TestStatus.Text = _english ? "Draft modified" : "Brouillon modifié";
            };
            form.Children.Add(operation);
            node.Height += 64;
        }
        StackPanel? sourcePicker = null;
        ComboBox? scenePicker = null;
        var restoringScene = false;
        async Task LoadSources()
        {
            if (sourcePicker is null) return;
            try
            {
                var scene = options["scene"]?.ToString();
                var selected = options["sources"] is System.Text.Json.Nodes.JsonArray array
                    ? array.Select(item => item!.GetValue<string>()).ToArray()
                    : options["source"] is { } legacy ? new[] { legacy.ToString() } : Array.Empty<string>();
                IReadOnlyList<string> values = selected;
                if (ObsSessionState.Client?.IsConnected == true && !string.IsNullOrWhiteSpace(scene))
                    values = (await ObsSessionState.Client.GetSourceNamesAsync(scene)).Concat(selected).Distinct().ToArray();
                if (options["scene"]?.ToString() != scene) return;
                sourcePicker.Children.Clear();
                foreach (var value in values)
                {
                    var checkbox = new CheckBox
                    {
                        Content = value,
                        IsChecked = selected.Contains(value),
                        FontSize = 12,
                        MinHeight = 28
                    };
                    checkbox.IsCheckedChanged += (_, _) =>
                    {
                        options["sources"] = new System.Text.Json.Nodes.JsonArray(sourcePicker.Children
                            .OfType<CheckBox>().Where(item => item.IsChecked == true)
                            .Select(item => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(item.Content!.ToString())).ToArray());
                        options.Remove("source");
                        TestStatus.Text = _english ? "Draft modified" : "Brouillon modifié";
                    };
                    sourcePicker.Children.Add(checkbox);
                }
            }
            catch (Exception exception)
            {
                ToolTip.SetTip(sourcePicker, (_english ? "OBS list unavailable: " : "Liste OBS indisponible : ") + exception.Message);
            }
        }
        async Task LoadChoices(ComboBox picker, string key)
        {
            try
            {
                var client = ObsSessionState.Client;
                if (client?.IsConnected != true)
                {
                    ToolTip.SetTip(picker, _english ? "Connect OBS in Settings" : "Connecte OBS dans les paramètres");
                    return;
                }
                var scene = options["scene"]?.ToString();
                if (key == "source" && string.IsNullOrWhiteSpace(scene)) return;
                var values = key == "scene" ? await client.GetSceneNamesAsync() : await client.GetSourceNamesAsync(scene!);
                if (key == "source" && options["scene"]?.ToString() != scene) return;
                var saved = options[key]?.ToString();
                restoringScene = true;
                try
                {
                    picker.ItemsSource = saved is null ? values : values.Append(saved).Distinct().ToArray();
                    picker.SelectedItem = saved;
                }
                finally { restoringScene = false; }
                await LoadSources();
            }
            catch (Exception exception)
            {
                ToolTip.SetTip(picker, (_english ? "OBS list unavailable: " : "Liste OBS indisponible : ") + exception.Message);
            }
        }
        foreach (var (key, fr, en) in fields)
        {
            form.Children.Add(CreateFieldLabel(key == "source" ? "Sources à modifier" : fr, key == "source" ? "Sources to change" : en));
            if (key == "source")
            {
                sourcePicker = new StackPanel { Spacing = 3 };
                form.Children.Add(new ScrollViewer { Content = sourcePicker, Height = 110, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
                node.Height += 80;
                _ = LoadSources();
                continue;
            }
            if (key == "scene")
            {
                var picker = new ComboBox { Tag = (fr, en), PlaceholderText = _english ? en : fr, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, FontSize = 12 };
                scenePicker = picker;
                if (options[key]?.ToString() is { } savedScene)
                {
                    picker.ItemsSource = new[] { savedScene };
                    picker.SelectedItem = savedScene;
                }
                picker.SelectionChanged += (_, _) =>
                {
                    if (restoringScene) return;
                    if (picker.SelectedItem is not string selected) return;
                    var changed = options[key]?.ToString() != selected;
                    options[key] = selected;
                    if (key == "scene" && changed && sourcePicker is not null)
                    {
                        options.Remove("source");
                        options.Remove("sources");
                        _ = LoadSources();
                    }
                };
                picker.DropDownOpened += (_, _) => { _ = LoadChoices(picker, key); };
                form.Children.Add(picker);
                _ = LoadChoices(picker, key);
                continue;
            }
            var input = new TextBox
            {
                Tag = (fr, en),
                PlaceholderText = _english ? en : fr,
                Text = options[key]?.ToString() ?? (key == "seconds" ? "1" : ""),
                FontSize = 12
            };
            if (key == "seconds" && options[key] is null) options[key] = "1";
            ToolTip.SetTip(input, _english ? en : fr);
            input.TextChanged += (_, _) =>
            {
                options[key] = input.Text ?? "";
                TestStatus.Text = _english ? "Draft modified" : "Brouillon modifié";
            };
            form.Children.Add(input);
        }
        var grid = (Grid)node.Child!;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(form, grid.RowDefinitions.Count - 1);
        grid.Children.Add(form);
        node.Height += fields.Count * 64 + 10;
        if (scenePicker is not null)
            _obsCardRefresh[node] = async () => { await LoadChoices(scenePicker, "scene"); await LoadSources(); };
    }

    private TextBlock CreateFieldLabel(string french, string english)
    {
        var text = new TextBlock
        {
            Text = _english ? english : french,
            Tag = (french, english),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#CDD0E3"))
        };
        return text;
    }

    private static (string French, string English) LocalizedActionTitle(string key) => PrototypeServices.ActionTypeFor(key) switch
    {
        "obs.set-source-visible" => ("Visibilité d’une source", "Source visibility"),
        "obs.scene.set-current" => ("Changer de scène", "Change scene"),
        "obs.replay.start" => ("Démarrer un replay", "Start replay"),
        "obs.screenshot" => ("Prendre une capture", "Take a screenshot"),
        "twitch.update-title" => ("Modifier le titre", "Edit title"),
        "twitch.send-message" => ("Envoyer un message", "Send a message"),
        "twitch.create-clip" => ("Créer un clip", "Create a clip"),
        "twitch.get-user" => ("Récupérer un utilisateur", "Get a user"),
        "core.delay" => ("Attendre", "Wait"),
        "core.condition" => ("Condition", "Condition"),
        "variables.set-integer" => ("Modifier une variable", "Edit a variable"),
        "audio.play" => ("Jouer un son", "Play a sound"),
        _ => ("Gérer les points de chaîne", "Manage channel points")
    };

    private static (string Header, string Border, string Icon, string Text) GetActionPalette(string label)
    {
        var value = label.ToLowerInvariant();
        if (value.Contains("obs") || value.Contains("scène") || value.Contains("scene") || value.Contains("source") || value.Contains("replay") || value.Contains("capture"))
            return ("#20242C", "#737B8A", "#343A4D", "#DCE2EE");
        if (value.Contains("condition") || value.Contains("attendre") || value.Contains("wait") || value.Contains("variable") || value.Contains("compteur") || value.Contains("sound") || value.Contains("son"))
            return ("#173C73", "#3788F5", "#275AA0", "#7EB8FF");
        return ("#4A206F", "#A640F4", "#6F35BE", "#D39AFF");
    }

    private void DeleteActionNode(Border node)
    {
        if (node.Tag is not string label) return;
        RememberCanvas();
        _selectedActions.Remove(label);
        RebuildActionNodes();
        TestStatus.Text = _english ? "Action deleted" : "Action supprimée";
        UpdateHistoryButtons();
    }

    private void OnActionNodePressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if ((eventArgs.Source as Visual)?.GetSelfAndVisualAncestors().Any(control => control is ComboBox || control is Button || control is TextBox || control is ListBox) == true) return;
        if (sender is not Border node) return;
        if (node == _triggerNode && eventArgs.GetCurrentPoint(node).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            TestStatus.Text = _english ? "The trigger is fixed at the beginning" : "Le déclencheur reste fixé au début";
            eventArgs.Handled = true;
            return;
        }
        var point = eventArgs.GetCurrentPoint(node);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            if (_selectedNode is not null) _selectedNode.BorderBrush = new SolidColorBrush(Color.Parse(GetActionPalette(_selectedNode.Tag as string ?? "").Border));
            _selectedNode = node;
            node.BorderBrush = new SolidColorBrush(Color.Parse("#FFD166"));
            node.Focus();
            TestStatus.Text = _english ? "Action selected — press Delete to remove" : "Action sélectionnée — appuie sur Suppr pour supprimer";
            eventArgs.Handled = true;
            return;
        }

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            _draggedNode = node;
            _dragStartSnapshot = CaptureCanvas();
            node.Focus();
            _dragPointerStart = eventArgs.GetCurrentPoint(WorkflowCanvas).Position;
            _dragNodeLeft = Canvas.GetLeft(node);
            _dragNodeTop = Canvas.GetTop(node);
            eventArgs.Pointer.Capture(node);
            eventArgs.Handled = true;
        }
    }

    private void OnActionNodeKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Delete || sender is not Border node || node.Tag is not string label) return;
        DeleteActionNode(node);
        eventArgs.Handled = true;
    }

    private void OnActionNodeMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_draggedNode != sender || eventArgs.Pointer.Captured != _draggedNode) return;
        var point = eventArgs.GetCurrentPoint(WorkflowCanvas).Position;
        var left = Math.Max(8, _dragNodeLeft + point.X - _dragPointerStart.X);
        var top = Math.Max(8, _dragNodeTop + point.Y - _dragPointerStart.Y);
        Canvas.SetLeft(_draggedNode!, left);
        Canvas.SetTop(_draggedNode!, top);
        UpdateDynamicLinks();
    }

    private void UpdateDynamicLinks()
    {
        foreach (var link in _dynamicLinks) WorkflowCanvas.Children.Remove(link);
        _dynamicLinks.Clear();
        _linkMap.Clear();
        var targetGroups = _manualLinks.GroupBy(link => link.To).ToDictionary(
            group => group.Key,
            group => group.OrderBy(link => Canvas.GetTop(link.From)).ToList());
        foreach (var (from, to) in _manualLinks)
        {
            var fromLeft = Canvas.GetLeft(from);
            var fromTop = Canvas.GetTop(from);
            var toLeft = Canvas.GetLeft(to);
            var toTop = Canvas.GetTop(to);
            var fromCenter = new Point(fromLeft + from.Width / 2, fromTop + from.Height / 2);
            var toCenter = new Point(toLeft + to.Width / 2, toTop + to.Height / 2);
            var horizontal = Math.Abs(toCenter.X - fromCenter.X) >= Math.Abs(toCenter.Y - fromCenter.Y);
            var targetIndex = targetGroups[to].IndexOf((from, to));
            var targetCount = targetGroups[to].Count;
            var spread = (targetIndex - (targetCount - 1) / 2d) * 26;
            var start = horizontal
                ? new Point(toCenter.X >= fromCenter.X ? fromLeft + from.Width : fromLeft, fromCenter.Y)
                : new Point(fromCenter.X, toCenter.Y >= fromCenter.Y ? fromTop + from.Height : fromTop);
            var end = horizontal
                ? new Point(toCenter.X >= fromCenter.X ? toLeft - 2 : toLeft + to.Width + 2, toCenter.Y + spread)
                : new Point(toCenter.X + spread, toCenter.Y >= fromCenter.Y ? toTop - 2 : toTop + to.Height + 2);
            var bend = horizontal
                ? new Point((start.X + end.X) / 2, start.Y)
                : new Point(start.X, (start.Y + end.Y) / 2);
            var finalStart = horizontal
                ? new Point(bend.X, end.Y)
                : new Point(end.X, bend.Y);
            var ux = horizontal ? Math.Sign(toCenter.X - fromCenter.X) : 0;
            var uy = horizontal ? 0 : Math.Sign(toCenter.Y - fromCenter.Y);
            var px = -uy;
            var py = ux;
            var headBase = new Point(end.X - ux * 17, end.Y - uy * 17);
            var wingA = new Point(headBase.X + px * 8, headBase.Y + py * 8);
            var wingB = new Point(headBase.X - px * 8, headBase.Y - py * 8);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false);
                context.LineTo(bend);
                context.LineTo(finalStart);
                context.LineTo(end);
                context.BeginFigure(wingA, false);
                context.LineTo(end);
                context.LineTo(wingB);
            }
            var branchOutput = from.Tag is string sourceKey && to.Tag is string targetKey
                ? PrototypeServices.ActionOptions.GetValueOrDefault(sourceKey)?["branches"]?[targetKey]?.ToString() : null;
            var link = new Avalonia.Controls.Shapes.Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(Color.Parse(branchOutput switch
                {
                    "yes" => "#53D994", "no" => "#F1B85B", _ => GetActionPalette(from.Tag as string ?? "").Border
                })),
                StrokeThickness = 3,
                StrokeLineCap = PenLineCap.Round,
                ZIndex = 10
            };
            link.PointerPressed += OnWorkflowLinkPressed;
            WorkflowCanvas.Children.Insert(0, link);
            _dynamicLinks.Add(link);
            _linkMap[link] = (from, to);
        }
    }

    private void OnWorkflowLinkPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Avalonia.Controls.Shapes.Path line || !_linkMap.TryGetValue(line, out var link)) return;
        if (eventArgs.GetCurrentPoint(line).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed) return;
        TestStatus.Text = _english ? "Removing link…" : "Suppression du lien…";
        RememberCanvas();
        _manualLinks.Remove(link);
        UpdateDynamicLinks();
        TestStatus.Text = _english ? "Link deleted" : "Lien supprimé";
        eventArgs.Handled = true;
    }

    private void OnActionNodeReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_draggedNode == sender)
        {
            eventArgs.Pointer.Capture(null);
            if (sender is Border node && Math.Abs(Canvas.GetLeft(node) - _dragNodeLeft) < 3 && Math.Abs(Canvas.GetTop(node) - _dragNodeTop) < 3)
            {
                if (_selectedNode is null || _selectedNode == node)
                {
                    if (_selectedNode is not null) _selectedNode.BorderBrush = new SolidColorBrush(Color.Parse(GetActionPalette(_selectedNode.Tag as string ?? "").Border));
                    _selectedNode = node;
                    node.BorderBrush = new SolidColorBrush(Color.Parse("#FFD166"));
                    TestStatus.Text = _english ? "Source selected — click a destination" : "Source sélectionnée — clique une destination";
                }
                else
                {
                    if (_selectedNode == _triggerNode && node == _triggerNode || _selectedNode != _triggerNode && node == _triggerNode)
                    {
                        TestStatus.Text = _english ? "Links can only enter actions from an event" : "Les liens doivent partir d’un événement vers une action";
                        _selectedNode = null;
                        return;
                    }
                    if (_manualLinks.Contains((_selectedNode, node)))
                    {
                        TestStatus.Text = _english ? "This link already exists" : "Ce lien existe déjà";
                        _selectedNode = null;
                        return;
                    }
                    var reachable = new HashSet<Border>();
                    var pending = new Stack<Border>(); pending.Push(node);
                    while (pending.TryPop(out var candidate))
                    {
                        if (!reachable.Add(candidate)) continue;
                        foreach (var edge in _manualLinks.Where(edge => edge.From == candidate)) pending.Push(edge.To);
                    }
                    if (reachable.Contains(_selectedNode))
                    {
                        TestStatus.Text = _english ? "This link would create a loop" : "Cette liaison créerait une boucle";
                        _draggedNode = null; _dragStartSnapshot = null;
                        return;
                    }
                    RememberCanvas();
                    _manualLinks.Add((_selectedNode, node));
                    if (_selectedNode.Tag is string sourceKey && node.Tag is string targetKey && sourceKey.Contains("condition", StringComparison.OrdinalIgnoreCase)
                        && PrototypeServices.ActionOptions.TryGetValue(sourceKey, out var sourceOptions))
                    {
                        if (sourceOptions["branches"] is not System.Text.Json.Nodes.JsonObject branches)
                            sourceOptions["branches"] = branches = new System.Text.Json.Nodes.JsonObject();
                        branches[targetKey] = sourceOptions["linkOutput"]?.ToString() ?? "yes";
                    }
                    _selectedNode.BorderBrush = new SolidColorBrush(Color.Parse(GetActionPalette(_selectedNode.Tag as string ?? "").Border));
                    _selectedNode = null;
                    TestStatus.Text = _english ? "Link created" : "Lien créé";
                    UpdateDynamicLinks();
                }
            }
            else if (_dragStartSnapshot is not null)
            {
                _undoActions.Push(_dragStartSnapshot);
                _redoActions.Clear();
                UpdateHistoryButtons();
            }
            _dragStartSnapshot = null;
            _draggedNode = null;
        }
    }

    internal void SetConnectionStatus(string message) => TestStatus.Text = message;

    internal void UpdateIntegrationCount(bool obsConnected, bool twitchConnected)
    {
        foreach (var removed in _obsCardRefresh.Keys.Where(card => !_dynamicNodes.Contains(card)).ToArray())
            _obsCardRefresh.Remove(removed);
        if (obsConnected)
            foreach (var refresh in _obsCardRefresh.Values.ToArray()) _ = refresh();
        ObsActionSection.IsVisible = obsConnected;
        TwitchActionSection.IsVisible = twitchConnected;
        CreatorAvatar.Text = twitchConnected ? "T" : "C";
        CreatorImage.IsVisible = false;
        if (twitchConnected && TwitchSessionState.Identity?.ProfileImageUrl is { } imageUrl)
        {
            _ = LoadCreatorImageAsync(imageUrl);
        }
        CreatorTitle.Text = twitchConnected && TwitchSessionState.Identity is not null
            ? TwitchSessionState.Identity.Login
            : "Créateur";
        CreatorSubtitle.Text = twitchConnected && TwitchSessionState.Identity is not null
            ? "Twitch"
            : "Espace local  ·  Privé";
        StatusCreatorName.Text = twitchConnected && TwitchSessionState.Identity is not null
            ? TwitchSessionState.Identity.Login
            : "Créateur";
        StatusCreatorImage.IsVisible = false;
        StatusCreatorAvatarFallback.IsVisible = true;

        var connected = (obsConnected ? 1 : 0) + (twitchConnected ? 1 : 0);
        IntegrationObsTooltipDot.Fill = new SolidColorBrush(Color.Parse(obsConnected ? "#53D994" : "#F1B85B"));
        IntegrationObsTooltipStatus.Text = _english
            ? (obsConnected ? "Connected" : "Disconnected")
            : (obsConnected ? "Connecté" : "Non connecté");
        IntegrationObsTooltipStatus.Foreground = new SolidColorBrush(Color.Parse(obsConnected ? "#72F0AA" : "#FFD081"));
        IntegrationTwitchTooltipDot.Fill = new SolidColorBrush(Color.Parse(twitchConnected ? "#53D994" : "#F1B85B"));
        IntegrationTwitchTooltipStatus.Text = _english
            ? (twitchConnected ? "Connected" : "Disconnected")
            : (twitchConnected ? "Connecté" : "Non connecté");
        IntegrationTwitchTooltipStatus.Foreground = new SolidColorBrush(Color.Parse(twitchConnected ? "#72F0AA" : "#FFD081"));
        IntegrationCountStatus.Text = _english
            ? $"{connected}/2 integrations connected"
            : $"{connected}/2 intégrations connectées";
        IntegrationStatusDot.Background = new SolidColorBrush(Color.Parse(connected switch
        {
            2 => "#53D994",
            1 => "#F1B85B",
            _ => "#7C849F"
        }));
        IntegrationCountStatus.Foreground = new SolidColorBrush(Color.Parse(connected switch
        {
            2 => "#72F0AA",
            1 => "#FFD081",
            _ => "#9AA1BD"
        }));
    }

    private async void OnIntegrationsClick(object? sender, RoutedEventArgs eventArgs)
    {
        var window = new IntegrationsWindow();
        await window.ShowDialog(this);
        UpdateIntegrationCount(
            ObsSessionState.Client?.IsConnected == true,
            TwitchSessionState.EventSubClient?.IsConnected == true);
    }

    private async void OnHistoryClick(object? sender, RoutedEventArgs eventArgs)
    {
        await new HistoryWindow().ShowDialog(this);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs eventArgs)
    {
        var window = new SettingsWindow(_english ? "English" : "Français", english => ApplyLanguage(english));
        await window.ShowDialog(this);
        UpdateIntegrationCount(
            ObsSessionState.Client?.IsConnected == true,
            TwitchSessionState.EventSubClient?.IsConnected == true);
        if (window.SavedLanguage is { } language)
        {
            ApplyLanguage(language == "English");
        }
    }

    private void OnSettingsPointerPressed(object? sender, PointerPressedEventArgs eventArgs) =>
        OnSettingsClick(sender, new RoutedEventArgs());

    private void OnTwitchSectionClick(object? sender, RoutedEventArgs eventArgs) =>
        ToggleSection(TwitchActions, TwitchSectionChevron);

    private void OnObsSectionClick(object? sender, RoutedEventArgs eventArgs) =>
        ToggleSection(ObsActions, ObsSectionChevron);

    private void OnCoreSectionClick(object? sender, RoutedEventArgs eventArgs) =>
        ToggleSection(CoreActions, CoreSectionChevron);

    private static void ToggleSection(Control content, TextBlock chevron)
    {
        content.IsVisible = !content.IsVisible;
        chevron.Text = content.IsVisible ? "⌃" : "⌄";
    }

    private async Task LoadCreatorImageAsync(string imageUrl)
    {
        try
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(imageUrl);
            await using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CreatorImage.Source = bitmap;
                CreatorImage.IsVisible = true;
                CreatorAvatar.IsVisible = false;
                StatusCreatorImage.Source = bitmap;
                StatusCreatorImage.IsVisible = true;
                StatusCreatorAvatarFallback.IsVisible = false;
            });
        }
        catch
        {
            CreatorImage.IsVisible = false;
            CreatorAvatar.IsVisible = true;
        }
    }

    private void OnLanguageClick(object? sender, RoutedEventArgs eventArgs)
    {
        ApplyLanguage(!_english);
    }

    internal void ApplyLanguage(bool english)
    {
        _english = english;
        var triggerIndex = WorkflowTriggerSelector.SelectedIndex;
        var switching = _switchingWorkflow;
        _switchingWorkflow = true;
        WorkflowTriggerSelector.ItemsSource = english ? new[] { "Manual", "New follower" } : new[] { "Manuel", "Nouveau follower" };
        WorkflowTriggerSelector.SelectedIndex = triggerIndex;
        _switchingWorkflow = switching;
        RunOnceButton.Content = english ? "Run once" : "Exécuter";
        ToolTip.SetTip(RunOnceButton, english ? "Run the published version with real effects" : "Exécuter la version publiée avec des effets réels");
        UiLanguageState.IsEnglish = english;
        SaveWorkflowButton.Content = english ? "Save" : "Enregistrer";
        if (StartWorkflowButton.Content?.ToString()?.Contains("Stop", StringComparison.Ordinal) == true || StartWorkflowButton.Content?.ToString()?.Contains("Arrêter", StringComparison.Ordinal) == true)
        {
            StartWorkflowButton.Content = english ? "■  Stop" : "■  Arrêter";
        }
        else
        {
            StartWorkflowButton.Content = english ? "▶  Start" : "▶  Démarrer";
        }
        var translations = _english
            ? new Dictionary<string, string>
            {
                ["Mon Live Automatisation"] = "My Live Automation", ["Accueil"] = "Home", ["Workflows"] = "Workflows",
                ["Événements"] = "Events", ["Actions"] = "Actions", ["Intégrations"] = "Integrations",
                ["Variables"] = "Variables", ["Pages"] = "Pages", ["Paramètres"] = "Settings",
                ["Tester"] = "Test", ["Enregistrer"] = "Save", ["Démarrer"] = "Start",
                ["Bibliothèque de modèles"] = "Template library", ["Voir tout  ›"] = "View all  ›",
                ["Twitch"] = "Twitch", ["OBS Studio"] = "OBS Studio", ["CORE"] = "CORE",
                ["Rechercher une action…"] = "Search for an action…", ["Données enregistrées localement"] = "Data saved locally",
                ["Moteur actif"] = "Engine active", ["Intégrations"] = "Integrations",
                ["Nouveau follow"] = "New follow", ["Est un abonné ?"] = "Is a subscriber?", ["Oui"] = "Yes", ["Non"] = "No",
                ["Afficher une alerte\nOBS"] = "Show an OBS alert", ["Envoyer un message\nDiscord"] = "Send a Discord message",
                ["Jouer un son"] = "Play a sound", ["Ajouter au compteur"] = "Increment counter",
                ["Follow avec alerte"] = "Follow with alert", ["Nouvel abonné"] = "New subscriber", ["Raid + message"] = "Raid + message", ["Cheer + son"] = "Cheer + sound",
                ["Envoyer un message"] = "Send a message", ["Créer un clip"] = "Create a clip", ["Modifier un titre"] = "Edit title", ["Gérer les points de chaîne"] = "Manage channel points", ["Récupérer un utilisateur"] = "Get a user",
                ["Changer de scène"] = "Change scene", ["Afficher une source"] = "Show a source", ["Masquer une source"] = "Hide a source", ["Démarrer un replay"] = "Start replay", ["Prendre une capture"] = "Take a screenshot",
                ["Ajouter une condition"] = "Add a condition", ["Attendre"] = "Wait", ["Modifier une variable"] = "Edit a variable",
                ["Déclencheurs"] = "Triggers", ["Exécution manuelle"] = "Manual run", ["Nouveau follower Twitch"] = "New Twitch follower"
            }
            : new Dictionary<string, string>
            {
                ["My Live Automation"] = "Mon Live Automatisation", ["Home"] = "Accueil", ["Events"] = "Événements",
                ["Integrations"] = "Intégrations", ["Settings"] = "Paramètres", ["Test"] = "Tester",
                ["Save"] = "Enregistrer", ["Start"] = "Démarrer", ["Template library"] = "Bibliothèque de modèles",
                ["View all  ›"] = "Voir tout  ›", ["Search for an action…"] = "Rechercher une action…",
                ["Data saved locally"] = "Données enregistrées localement", ["Engine active"] = "Moteur actif",
                ["New follow"] = "Nouveau follow", ["Is a subscriber?"] = "Est un abonné?", ["Yes"] = "Oui", ["No"] = "Non",
                ["Show an OBS alert"] = "Afficher une alerte\nOBS", ["Send a Discord message"] = "Envoyer un message\nDiscord", ["Play a sound"] = "Jouer un son", ["Increment counter"] = "Ajouter au compteur",
                ["Follow with alert"] = "Follow avec alerte", ["New subscriber"] = "Nouvel abonné", ["Cheer + sound"] = "Cheer + son",
                ["Send a message"] = "Envoyer un message", ["Create a clip"] = "Créer un clip", ["Edit title"] = "Modifier un titre", ["Manage channel points"] = "Gérer les points de chaîne", ["Get a user"] = "Récupérer un utilisateur",
                ["Change scene"] = "Changer de scène", ["Show a source"] = "Afficher une source", ["Hide a source"] = "Masquer une source", ["Start replay"] = "Démarrer un replay", ["Take a screenshot"] = "Prendre une capture",
                ["Add a condition"] = "Ajouter une condition", ["Wait"] = "Attendre", ["Edit a variable"] = "Modifier une variable",
                ["Triggers"] = "Déclencheurs", ["Manual run"] = "Exécution manuelle", ["New Twitch follower"] = "Nouveau follower Twitch"
            };

        translations["Brouillon enregistré"] = _english ? "Draft saved" : "Brouillon enregistré";
        translations[_english ? "Visibilité d’une source" : "Source visibility"] = _english ? "Source visibility" : "Visibilité d’une source";
        translations["Événement"] = _english ? "Event" : "Événement";
        translations["Condition"] = _english ? "Condition" : "Condition";
        translations["Démarrer"] = _english ? "Start" : "Démarrer";
        translations["Est un abonné?"] = _english ? "Is a subscriber?" : "Est un abonné?";
        translations["Est un abonné ?"] = _english ? "Is a subscriber?" : "Est un abonné ?";
        translations["Afficher une alerte"] = _english ? "Show an OBS alert" : "Afficher une alerte";
        translations["Envoyer un message"] = _english ? "Send a message" : "Envoyer un message";
        translations["Modifier un titre"] = _english ? "Edit title" : "Modifier un titre";
        translations["Gérer les points de chaîne"] = _english ? "Manage channel points" : "Gérer les points de chaîne";
        translations["Récupérer un utilisateur"] = _english ? "Get a user" : "Récupérer un utilisateur";
        translations["Changer de scène"] = _english ? "Change scene" : "Changer de scène";
        foreach (var alias in new[] { "Afficher une source", "Masquer une source", "Show a source", "Hide a source", "Show source", "Hide source" })
            translations[alias] = _english ? "Source visibility" : "Visibilité d’une source";
        translations["Démarrer un replay"] = _english ? "Start replay" : "Démarrer un replay";
        translations["Prendre une capture"] = _english ? "Take a screenshot" : "Prendre une capture";
        translations["＋  Action"] = _english ? "＋  Action" : "＋  Action";
        translations["＋  Événement"] = _english ? "＋  Event" : "＋  Événement";
        translations["＋  Event"] = _english ? "＋  Event" : "＋  Événement";

        foreach (var control in this.GetVisualDescendants().OfType<TextBlock>())
        {
            if (control.Tag is ValueTuple<string, string> fieldLabel)
            {
                control.Text = english ? fieldLabel.Item2 : fieldLabel.Item1;
                continue;
            }
            if (control.GetVisualAncestors().Any(ancestor => ancestor is TextBox or ComboBox or CheckBox)) continue;
            if (control.Text is { } text)
            {
                control.Text = TranslateText(text, translations);
            }
        }

        foreach (var picker in this.GetVisualDescendants().OfType<ComboBox>())
        {
            if (picker.Tag is ValueTuple<string, string> captions)
                picker.PlaceholderText = english ? captions.Item2 : captions.Item1;
            if (Equals(picker.Tag, "visibilityOptions"))
            {
                var selected = picker.SelectedIndex;
                picker.ItemsSource = english ? new[] { "Show", "Hide" } : new[] { "Afficher", "Masquer" };
                picker.SelectedIndex = selected;
            }
            if (Equals(picker.Tag, "variableOperations"))
            {
                var selected = picker.SelectedIndex;
                picker.ItemsSource = english ? new[] { "Set", "Add" } : new[] { "Définir", "Ajouter" };
                picker.SelectedIndex = selected;
            }
            if (Equals(picker.Tag, "conditionComparisons") || Equals(picker.Tag, "conditionBranches"))
            {
                var selected = picker.SelectedIndex;
                picker.ItemsSource = Equals(picker.Tag, "conditionBranches")
                    ? (english ? new[] { "Yes", "No" } : new[] { "Oui", "Non" })
                    : (english ? new[] { "Equals", "Greater than", "Less than" } : new[] { "Égal à", "Supérieur à", "Inférieur à" });
                picker.SelectedIndex = selected;
            }
        }

        foreach (var input in this.GetVisualDescendants().OfType<TextBox>())
            if (input.Tag is ValueTuple<string, string> captions)
            {
                input.PlaceholderText = english ? captions.Item2 : captions.Item1;
                ToolTip.SetTip(input, input.PlaceholderText);
            }

        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            if (button.Content is string text)
            {
                button.Content = TranslateText(text, translations);
            }
        }
    }

    private static string TranslateText(string text, IReadOnlyDictionary<string, string> translations)
    {
        if (translations.TryGetValue(text, out var exact))
        {
            return exact;
        }

        foreach (var pair in translations.OrderByDescending(pair => pair.Key.Length))
        {
            text = text.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }

        return text;
    }
}
