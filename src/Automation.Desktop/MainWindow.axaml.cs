using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System.Net.Http;
using Avalonia.Threading;
using System.IO;
using Automation.Connectors.Twitch;

namespace Automation.Desktop;

public sealed partial class MainWindow : Window
{
    private bool _english;
    private string _selectedQueueName = "Default";
    private int _addedActionCount;
    private readonly List<string> _selectedActions = new();
    private readonly Stack<CanvasSnapshot> _undoActions = new();
    private readonly Stack<CanvasSnapshot> _redoActions = new();
    private CanvasSnapshot? _dragStartSnapshot;
    private sealed record CanvasSnapshot(string[] Actions, Dictionary<string, Point> Positions,
        (string From, string To)[] Links, Dictionary<string, System.Text.Json.Nodes.JsonObject> Options,
        Dictionary<string, System.Text.Json.Nodes.JsonObject> TriggerOptions, Dictionary<string, bool> Visibility, string[] Triggers);
    private Border? _draggedNode;
    private Border? _selectedNode;
    private readonly List<Border> _dynamicNodes = new();
    private Border? _triggerNode;
    private readonly List<Border> _additionalTriggerNodes = new();
    private readonly Dictionary<Border, Func<Task>> _obsCardRefresh = new();
    private readonly List<Avalonia.Controls.Shapes.Path> _dynamicLinks = new();
    private readonly List<Border> _dynamicLinkLabels = new();
    private readonly Dictionary<Border, (Border From, Border To)> _linkLabelMap = new();
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
    private readonly HashSet<Guid> _cycleApprovals = new();
    private readonly Dictionary<Guid, string> _workflowTriggerKinds = new();
    private readonly Dictionary<Guid, string[]> _workflowTriggers = new();
    private Point _dragPointerStart;
    private double _dragNodeLeft;
    private double _dragNodeTop;
    private double _canvasZoom = 1;
    private Point _canvasOffset;
    private Point? _panStart;
    private bool _switchingActivation;
    private Point _panOffset;

    public MainWindow() : this(true) { }

    internal MainWindow(bool restoreSession)
    {
        InitializeComponent();
        ManagementButtons.History(UndoButton, _english, false);
        ManagementButtons.History(RedoButton, _english, true);
        var viewport = (Grid)WorkflowCanvas.Parent!;
        viewport.Background = WorkflowCanvas.Background;
        WorkflowCanvas.Background = Brushes.Transparent;
        WorkflowCanvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        WorkflowCanvas.Children.Remove(WorkflowToolbar);
        WorkflowCanvas.Children.Remove(CanvasControls);
        viewport.Children.Add(WorkflowToolbar);
        viewport.Children.Add(CanvasControls);
        WorkflowToolbar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
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
        _switchingWorkflow = true;
        WorkflowTriggerSelector.ItemsSource = new[] { "Manuel", "Nouveau follower" };
        WorkflowTriggerSelector.SelectedIndex = 0;
        _switchingWorkflow = false;
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
                    _workflowTriggers[draft.Id] = draft.Blocks.Where(block => PrototypeServices.IsTriggerType(block.Type)).Select(block => block.Type).Distinct().ToArray();
                    _workflowTriggerKinds[draft.Id] = _workflowTriggers[draft.Id].FirstOrDefault() ?? "manual.run";
                    _workflowActions[name] = draft.Blocks.Select(block => block.Parameters["label"]?.GetValue<string>()).OfType<string>().ToList();
                    var labels = draft.Blocks.Where(block => block.Parameters["label"] is not null || block.Parameters["triggerKey"] is not null).ToDictionary(block => block.Id, block => (block.Parameters["label"] ?? block.Parameters["triggerKey"])!.GetValue<string>());
                    _workflowLinks[name] = draft.Connections.Where(link => labels.ContainsKey(link.FromBlockId) && labels.ContainsKey(link.ToBlockId)).Select(link => (labels[link.FromBlockId], labels[link.ToBlockId])).ToList();
                    _workflowPositions[name] = new Dictionary<string, Point>(await ConnectionPersistence.GetWorkflowPositionsAsync(name));
                }
                _activeWorkflowName = _workflowNames[0];
                PrototypeServices.CurrentWorkflowId = _workflowIds[_activeWorkflowName];
                PrototypeServices.CurrentWorkflowName = _activeWorkflowName;
                PrototypeServices.CurrentTriggerType = _workflowTriggerKinds[PrototypeServices.CurrentWorkflowId];
                PrototypeServices.CurrentTriggerTypes.Clear();
                PrototypeServices.CurrentTriggerTypes.AddRange(_workflowTriggers[PrototypeServices.CurrentWorkflowId]);
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
                    var source = FindCanvasNode(from);
                    var target = FindCanvasNode(to);
                    if (source is not null && target is not null && !_manualLinks.Contains((source, target)))
                        _manualLinks.Add((source, target));
                }
                UpdateDynamicLinks();
                TestStatus.Text = _english ? "Draft restored" : "Brouillon restauré";
                var savedPositions = await ConnectionPersistence.GetWorkflowPositionsAsync(_activeWorkflowName);
                foreach (var node in CanvasNodes())
                    if (node.Tag is string label && savedPositions.TryGetValue(label, out var position)) { Canvas.SetLeft(node, position.X); Canvas.SetTop(node, position.Y); }
                UpdateDynamicLinks();
            }
            var publishedRevision = await PrototypeServices.GetPublishedRevisionAsync();
            if (publishedRevision is not null)
            {
                TestStatus.Text = _english
                    ? $"Automation active · revision {publishedRevision}"
                    : $"Automatisation active · révision {publishedRevision}";
                SetActivationSwitch(_enabledWorkflowIds.Contains(PrototypeServices.CurrentWorkflowId));
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
        _workflowTriggers[PrototypeServices.CurrentWorkflowId] = PrototypeServices.CurrentTriggerTypes.ToArray();
        _workflowLinks[_activeWorkflowName] = GetManualLinkLabels().ToList();
        _workflowPositions[_activeWorkflowName] = CaptureNodePositions();
        _workflowNames.Add(name);
        _workflowIds[name] = Guid.NewGuid();
        PrototypeServices.CurrentWorkflowId = _workflowIds[name];
        PrototypeServices.CurrentWorkflowName = name;
        PrototypeServices.CurrentTriggerType = "";
        PrototypeServices.CurrentTriggerTypes.Clear();
        _workflowTriggerKinds[_workflowIds[name]] = "";
        WorkflowTriggerSelector.SelectedIndex = 0;
        _workflowActions[name] = new List<string>();
        _workflowPositions[name] = new Dictionary<string, Point>();
        WorkflowSelector.ItemsSource = _workflowNames.ToArray();
        WorkflowSelector.SelectedIndex = _workflowNames.Count - 1;
        _activeWorkflowName = name;
        ClearWorkflowCanvas();
        SetActivationSwitch(false);
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
                _workflowTriggers[PrototypeServices.CurrentWorkflowId] = PrototypeServices.CurrentTriggerTypes.ToArray();
                PrototypeServices.CurrentWorkflowId = _workflowIds[name];
                PrototypeServices.CurrentTriggerTypes.Clear();
                PrototypeServices.CurrentTriggerTypes.AddRange(_workflowTriggers.GetValueOrDefault(_workflowIds[name], Array.Empty<string>()));
                PrototypeServices.CurrentTriggerType = PrototypeServices.CurrentTriggerTypes.FirstOrDefault() ?? "";
                _workflowLinks[_activeWorkflowName] = GetManualLinkLabels().ToList();
                _workflowPositions[_activeWorkflowName] = CaptureNodePositions();
                ClearWorkflowCanvas();
                foreach (var action in _workflowActions.TryGetValue(name, out var actions) ? actions : [])
                {
                    _selectedActions.Add(action);
                    AddActionNode(action);
                }
                if (_workflowPositions.TryGetValue(name, out var positions))
                    foreach (var node in CanvasNodes())
                        if (node.Tag is string label && positions.TryGetValue(label, out var position)) { Canvas.SetLeft(node, position.X); Canvas.SetTop(node, position.Y); }
                if (_workflowLinks.TryGetValue(name, out var savedLinks))
                    foreach (var (from, to) in savedLinks)
                    {
                        var source = FindCanvasNode(from);
                        var target = FindCanvasNode(to);
                        if (source is not null && target is not null) _manualLinks.Add((source, target));
                    }
                UpdateDynamicLinks();
                _activeWorkflowName = name;
                if (!_workflowIds.ContainsKey(name)) _workflowIds[name] = PrototypeServices.CurrentWorkflowId;
                PrototypeServices.CurrentWorkflowId = _workflowIds[name];
                PrototypeServices.CurrentWorkflowName = name;
                PrototypeServices.CurrentTriggerType = _workflowTriggerKinds.GetValueOrDefault(PrototypeServices.CurrentWorkflowId, "manual.run");
                UpdateTriggerNode();
                var switching = _switchingWorkflow;
                _switchingWorkflow = true;
                WorkflowTriggerSelector.SelectedIndex = PrototypeServices.CurrentTriggerType == "manual.run" ? 0 : 1;
                _switchingWorkflow = switching;
                SetActivationSwitch(_enabledWorkflowIds.Contains(PrototypeServices.CurrentWorkflowId));
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
        var dialog = new Window { Title = _english ? "Add event" : "Ajouter un événement", Width = 460, Height = 560, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.Parse("#0B1024")) };
        var eventButtonStyle = new SolidColorBrush(Color.Parse("#151A32"));
        var eventBorderStyle = new SolidColorBrush(Color.Parse("#33406A"));
        var manual = new Button { Content = _english ? "▶  Manual run" : "▶  Exécution manuelle", HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Background = eventButtonStyle, Foreground = Brushes.White, BorderBrush = eventBorderStyle, Padding = new Thickness(12, 9) };
        manual.Click += (_, _) => dialog.Close("manual.run");
        var timer = new Button { Content = _english ? "◷  Timer" : "◷  Minuteur", HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Background = eventButtonStyle, Foreground = Brushes.White, BorderBrush = eventBorderStyle, Padding = new Thickness(12, 9) };
        timer.Click += (_, _) => dialog.Close("core.timer");
        var systemItems = new StackPanel { Spacing = 6, Children = { manual, timer } };
        var twitchItems = new StackPanel { Spacing = 6 };
        var choices = new StackPanel { Margin = new Thickness(18), Spacing = 8, Children =
        {
            new TextBlock { Text = _english ? "Choose an event to add to this canvas" : "Choisis un événement à ajouter à ce canevas", TextWrapping = TextWrapping.Wrap },
            new Expander { Header = CategoryHeader("⚡", _english ? "EVENTS / SYSTEM" : "ÉVÉNEMENTS / SYSTÈME", "#159B70", "#62E6AE"), Content = systemItems, IsExpanded = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0) },
            new Expander { Header = CategoryHeader("T", "TWITCH", "#9146FF", "#F0D8FF", true), Content = twitchItems, IsExpanded = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0) }
        } };
        foreach (var item in TwitchTriggerCatalog.All)
        {
            var button = new Button { Content = "⚡  " + (_english ? item.English : item.French), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.Parse("#E8E9F4")), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(10, 8), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            button.Click += (_, _) => dialog.Close(item.BlockType);
            twitchItems.Children.Add(button);
        }
        dialog.Content = new ScrollViewer { Content = choices };
        var trigger = await dialog.ShowDialog<string?>(this);
        if (trigger is null || !PrototypeServices.IsTriggerType(trigger)) return;
        AddCanvasEvent(trigger);
    }

    private async void OnVariablesClick(object? sender, RoutedEventArgs eventArgs)
    {
        try { await new VariablesWindow(_english).ShowDialog(this); }
        catch (Exception exception)
        {
            SetConnectionStatus(_english ? $"Variables could not be opened: {exception.Message}" : $"Impossible d’ouvrir les variables : {exception.Message}");
        }
    }

    private async void OnRewardsClick(object? sender, RoutedEventArgs eventArgs)
    {
        await new RewardsWindow(_english).ShowDialog(this);
    }

    private async void OnCommandsClick(object? sender, RoutedEventArgs eventArgs) => await new CommandsWindow(_english).ShowDialog(this);
    private async void OnQueuesClick(object? sender, RoutedEventArgs eventArgs)
    {
        await new QueuesWindow(_english, _workflowIds).ShowDialog(this);
        await RefreshWorkflowQueueAsync();
    }

    private async void OnWorkflowQueueClick(object? sender, RoutedEventArgs args)
    {
        var workflowId = PrototypeServices.CurrentWorkflowId;
        try
        {
            var config = await AutomationQueues.LoadAsync();
            var names = config["names"]!.AsArray().Select(item => item!.ToString()).ToArray();
            var picker = new ListBox { ItemsSource = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(), SelectedItem = config["workflows"]?[workflowId.ToString()]?.ToString() ?? "Default", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var search = new TextBox { PlaceholderText = _english ? "Search queues…" : "Rechercher une file…" };
            search.TextChanged += (_, _) => { var selected = picker.SelectedItem; picker.ItemsSource = names.Where(name => name.Contains(search.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(); picker.SelectedItem = selected; };
            var save = new Button(); ManagementButtons.Save(save, _english);
            var dialog = new Window { Title = _english ? "Automation queue" : "File de l’automatisation", Width = 500, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
            var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 14 };
            layout.Children.Add(new TextBlock { Text = _english ? "Alert queue" : "File d’alertes" }); Grid.SetRow(search, 1); layout.Children.Add(search); Grid.SetRow(picker, 2); layout.Children.Add(picker); Grid.SetRow(save, 3); layout.Children.Add(save); dialog.Content = layout;
            save.Click += (_, _) => dialog.Close(true);
            if (await dialog.ShowDialog<bool>(this) && picker.SelectedItem is string selected)
            {
                await AutomationQueues.ChangeAsync(settings => settings["workflows"]![workflowId.ToString()] = selected);
                if (PrototypeServices.CurrentWorkflowId == workflowId) { _selectedQueueName = selected; UpdateWorkflowQueueLabel(); }
            }
        }
        catch (Exception ex) { TestStatus.Text = ex.Message; }
    }

    private async Task RefreshWorkflowQueueAsync()
    {
        var id = PrototypeServices.CurrentWorkflowId;
        try { var name = await AutomationQueues.ForWorkflowAsync(id); if (PrototypeServices.CurrentWorkflowId == id) { _selectedQueueName = name; UpdateWorkflowQueueLabel(); } }
        catch (Exception ex) { TestStatus.Text = ex.Message; }
    }

    private void UpdateWorkflowQueueLabel()
    {
        WorkflowQueueButton.Content = "▤  " + _selectedQueueName;
        WorkflowQueueButton.FontSize = 16;
        ToolTip.SetTip(WorkflowQueueButton, _english ? "Choose this automation’s alert queue" : "Choisir la file d’alertes de cette automatisation");
    }

    internal void AddCanvasEvent(string trigger)
    {
        if (!PrototypeServices.IsTriggerType(trigger)) throw new ArgumentException("Unknown trigger", nameof(trigger));
        if (PrototypeServices.CurrentTriggerTypes.Contains(trigger))
        {
            TestStatus.Text = _english ? "This event is already on the canvas" : "Cet événement est déjà dans le canevas";
            return;
        }
        RememberCanvas();
        PrototypeServices.CurrentTriggerTypes.Add(trigger);
        if (PrototypeServices.CurrentTriggerTypes.Count == 1)
        {
            PrototypeServices.CurrentTriggerType = trigger;
            _workflowTriggerKinds[PrototypeServices.CurrentWorkflowId] = trigger;
            AddTriggerNode();
        }
        else AddAdditionalTriggerNode(trigger);
        UpdateHistoryButtons();
        TestStatus.Text = _english ? "Event added — connect it to an action" : "Événement ajouté — relie-le à une action";
    }

    private async void OnAddCanvasItemClick(object? sender, RoutedEventArgs eventArgs)
    {
        var dialog = new Window { Title = _english ? "Add an action" : "Ajouter une action", Width = 560, Height = 700, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.Parse("#0B1024")) };
        var stack = new StackPanel { Margin = new Thickness(18), Spacing = 7 };
        foreach (var group in new[] {
            ("Twitch", new[] { ("Envoyer un message", "Send a message"), ("Créer un clip", "Create a clip"), ("Modifier un titre", "Edit title"), ("Récupérer un utilisateur", "Get a user") }),
            ("OBS Studio · Scènes", new[] { ("Changer de scène", "Change scene"), ("Récupérer la scène actuelle", "Get current scene"), ("Récupérer le statut OBS", "Get OBS status") }),
            ("OBS Studio · Sources", new[] { ("Masquer une source", "Hide source"), ("Afficher une source", "Show source"), ("Pivoter une source", "Rotate source"), ("Définir l’URL d’une source navigateur", "Set browser source URL"), ("Définir la couleur d’une source", "Set source color"), ("Définir un texte GDI", "Set GDI text"), ("Définir le fichier image d’une source", "Set image source file"), ("Définir le fichier média d’une source", "Set media source file"), ("Définir l’état média", "Set media state"), ("Définir la piste audio média", "Set media audio track"), ("Définir l’état d’un filtre", "Set source filter state"), ("Définir le mute d’une source", "Set source mute state"), ("Visibilité d’une source", "Source visibility") }),
            ("OBS Studio · Replay / Sorties", new[] { ("Tampon replay", "Replay buffer"), ("Enregistrement OBS", "OBS recording"), ("Caméra virtuelle", "Virtual camera"), ("Prendre une capture", "Take a screenshot") }) })
        {
            var groupItems = new StackPanel { Spacing = 6 };
            foreach (var item in group.Item2)
            { var button = new Button { Content = ActionIcon(item.Item1) + "  " + (_english ? item.Item2 : item.Item1), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.Parse("#E8E9F4")), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(10, 8) }; button.Click += (_, _) => dialog.Close($"action:{item.Item1}"); groupItems.Children.Add(button); }
            var isTwitch = group.Item1.StartsWith("Twitch", StringComparison.Ordinal);
            var isObs = group.Item1.StartsWith("OBS", StringComparison.Ordinal);
            var header = CategoryHeader(isTwitch ? "T" : isObs ? "OBS" : "▶", group.Item1, isTwitch ? "#9146FF" : isObs ? "#343A4D" : "#2563A5", isTwitch ? "#F0D8FF" : "#DCE8FF", isTwitch, isObs);
            stack.Children.Add(new Expander { Header = header, Content = groupItems, IsExpanded = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.Parse("#0F1530")), BorderBrush = new SolidColorBrush(Color.Parse("#33406A")), BorderThickness = new Thickness(1), Padding = new Thickness(6), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 6, 0, 0) });
        }
        dialog.Content = new ScrollViewer { Content = stack };
        var result = await dialog.ShowDialog<string?>(this);
        if (result is null) return;
        if (result.StartsWith("action:", StringComparison.Ordinal))
        {
            AddCanvasAction(result[7..]);
            TestStatus.Text = _english ? "Action added — connect it in the canvas" : "Action ajoutée — relie-la dans le canevas";
        }
        else if (result is "manual.run" or "twitch.follow.received")
        {
            if (PrototypeServices.CurrentTriggerTypes.Contains(result)) { TestStatus.Text = _english ? "This event is already on the canvas" : "Cet événement est déjà dans le canevas"; return; }
            PrototypeServices.CurrentTriggerTypes.Add(result); AddAdditionalTriggerNode(result);
            TestStatus.Text = _english ? "Event added — connect it in the canvas" : "Événement ajouté — relie-le dans le canevas";
        }
    }

    private static StackPanel CategoryHeader(string badgeText, string label, string badgeColor, string textColor, bool round = false, bool compact = false)
    {
        var badge = new Border
        {
            Width = compact ? 32 : 26,
            Height = 26,
            CornerRadius = new CornerRadius(round || compact ? 13 : 7),
            Background = new SolidColorBrush(Color.Parse(badgeColor)),
            Child = new TextBlock { Text = badgeText, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = compact ? 9 : 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White }
        };
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 9,
            Margin = new Thickness(8, 8, 8, 4),
            Children = { badge, new TextBlock { Text = label, FontWeight = FontWeight.Bold, FontSize = 14, Foreground = new SolidColorBrush(Color.Parse(textColor)), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } }
        };
    }

    private static string ActionIcon(string label)
    {
        var value = label.ToLowerInvariant();
        if (value.Contains("message")) return "💬";
        if (value.Contains("clip")) return "✂";
        if (value.Contains("titre") || value.Contains("title")) return "✏";
        if (value.Contains("scène") || value.Contains("scene")) return "🎬";
        if (value.Contains("source")) return "▣";
        if (value.Contains("replay")) return "↻";
        if (value.Contains("capture") || value.Contains("screenshot")) return "📷";
        if (value.Contains("son") || value.Contains("sound")) return "♫";
        if (value.Contains("variable")) return "ƒ";
        return "▶";
    }

    private async void OnAddCanvasSystemClick(object? sender, RoutedEventArgs eventArgs)
    {
        var dialog = new Window { Title = _english ? "Add a system function" : "Ajouter une fonction système", Width = 560, Height = 700, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.Parse("#0B1024")) };
        var stack = new StackPanel { Margin = new Thickness(18), Spacing = 7 };
        foreach (var group in new[]
        {
            ("⚙", "Logique", "Logic", new[] { ("⤨", "Ajouter une condition", "Add a condition"), ("◷", "Attendre", "Wait") }),
            ("ƒ", "Variables", "Variables", new[] { ("ƒ", "Modifier une variable", "Edit a variable") }),
            ("♫", "Audio", "Audio", new[] { ("♫", "Jouer un son", "Play a sound") })
        })
        {
            var items = new StackPanel { Spacing = 6 };
            foreach (var item in group.Item4)
            {
                var button = new Button
                {
                    Content = item.Item1 + "  " + (_english ? item.Item3 : item.Item2),
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Background = Brushes.Transparent,
                    Foreground = new SolidColorBrush(Color.Parse("#E8E9F4")),
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(10, 8)
                };
                button.Click += (_, _) => dialog.Close(item.Item2);
                items.Children.Add(button);
            }
            stack.Children.Add(new Expander
            {
                Header = CategoryHeader(group.Item1, _english ? group.Item3 : group.Item2, "#2563A5", "#DCE8FF"),
                Content = items,
                IsExpanded = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.Parse("#0F1530")),
                BorderBrush = new SolidColorBrush(Color.Parse("#33406A")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 6, 0, 0)
            });
        }
        dialog.Content = new ScrollViewer { Content = stack };
        var result = await dialog.ShowDialog<string?>(this);
        if (result is null) return;
        AddCanvasAction(result);
        TestStatus.Text = _english ? "System function added — connect it in the canvas" : "Fonction système ajoutée — relie-la dans le canevas";
    }

    internal void AddCanvasAction(string label)
    {
        RememberCanvas();
        var instance = $"{label}\u001f{Guid.NewGuid():N}";
        _selectedActions.Add(instance);
        AddActionNode(instance);
        UpdateHistoryButtons();
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
            _workflowPositions[current] = CaptureNodePositions();
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
        // Snapshot the current canvas before removing it, then switch state explicitly.
        _workflowActions[currentWorkflowName] = _selectedActions.ToList();
        _workflowLinks[currentWorkflowName] = GetManualLinkLabels().ToList();
        _workflowPositions[currentWorkflowName] = CaptureNodePositions();
        if (_workflowIds.TryGetValue(currentWorkflowName, out var deletedId))
            await PrototypeServices.DeleteWorkflowAsync(deletedId);
        _workflowNames.Remove(currentWorkflowName);
        _workflowActions.Remove(currentWorkflowName);
        _workflowLinks.Remove(currentWorkflowName);
        _workflowPositions.Remove(currentWorkflowName);
        if (deletedId != Guid.Empty)
        {
            _workflowTriggers.Remove(deletedId);
            _workflowTriggerKinds.Remove(deletedId);
            _enabledWorkflowIds.Remove(deletedId);
        }
        _workflowIds.Remove(currentWorkflowName);
        var targetName = _workflowNames[0];
        _activeWorkflowName = targetName;
        PrototypeServices.CurrentWorkflowId = _workflowIds[targetName];
        PrototypeServices.CurrentWorkflowName = targetName;
        PrototypeServices.CurrentTriggerTypes.Clear();
        PrototypeServices.CurrentTriggerTypes.AddRange(_workflowTriggers.GetValueOrDefault(PrototypeServices.CurrentWorkflowId, Array.Empty<string>()));
        PrototypeServices.CurrentTriggerType = PrototypeServices.CurrentTriggerTypes.FirstOrDefault() ?? "";
        ClearWorkflowCanvas();
        foreach (var action in _workflowActions.GetValueOrDefault(targetName, []))
        {
            _selectedActions.Add(action);
            AddActionNode(action);
        }
        if (_workflowPositions.TryGetValue(targetName, out var targetPositions))
            foreach (var node in CanvasNodes())
                if (node.Tag is string label && targetPositions.TryGetValue(label, out var position)) { Canvas.SetLeft(node, position.X); Canvas.SetTop(node, position.Y); }
        foreach (var (from, to) in _workflowLinks.GetValueOrDefault(targetName, []))
        {
            var source = FindCanvasNode(from); var target = FindCanvasNode(to);
            if (source is not null && target is not null) _manualLinks.Add((source, target));
        }
        UpdateDynamicLinks();
        WorkflowSelector.ItemsSource = _workflowNames.ToArray();
        _switchingWorkflow = true;
        WorkflowSelector.SelectedIndex = 0;
        _switchingWorkflow = false;
        SetActivationSwitch(_enabledWorkflowIds.Contains(PrototypeServices.CurrentWorkflowId));
        TestStatus.Text = _english ? "Automation deleted" : "Automatisation supprimée";
    }

    private void ClearWorkflowCanvas()
    {
        _obsCardRefresh.Clear();
        _selectedActions.Clear();
        _manualLinks.Clear();
        _dynamicNodes.Clear();
        foreach (var link in _dynamicLinks) WorkflowCanvas.Children.Remove(link);
        foreach (var label in _dynamicLinkLabels) WorkflowCanvas.Children.Remove(label);
        _dynamicLinks.Clear();
        _dynamicLinkLabels.Clear();
        _linkLabelMap.Clear();
        foreach (var node in WorkflowCanvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray())
            WorkflowCanvas.Children.Remove(node);
        _addedActionCount = 0;
        _linkMap.Clear();
        _selectedNode = null;
        foreach (var trigger in _additionalTriggerNodes) WorkflowCanvas.Children.Remove(trigger);
        _additionalTriggerNodes.Clear();
        AddTriggerNode();
        foreach (var trigger in PrototypeServices.CurrentTriggerTypes.Skip(1)) AddAdditionalTriggerNode(trigger);
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
        SetCanvasZoom(_canvasZoom + Math.Sign(args.Delta.Y) * 0.1, args.GetPosition((Visual)WorkflowCanvas.Parent!));
        args.Handled = true;
    }

    private void OnCenterCanvasClick(object? sender, RoutedEventArgs args)
    {
        var nodes = _dynamicNodes.Concat(_additionalTriggerNodes).Concat(_triggerNode is null ? Array.Empty<Border>() : new[] { _triggerNode }).Where(node => node.IsVisible).ToArray();
        if (nodes.Length == 0) { _canvasOffset = default; ApplyCanvasTransform(); return; }
        var left = nodes.Min(Canvas.GetLeft);
        var top = nodes.Min(Canvas.GetTop);
        var right = nodes.Max(node => Canvas.GetLeft(node) + NodeWidth(node));
        var bottom = nodes.Max(node => Canvas.GetTop(node) + NodeHeight(node));
        var viewport = (Grid)WorkflowCanvas.Parent!;
        _canvasOffset = new Point((viewport.Bounds.Width - (right + left) * _canvasZoom) / 2,
            (viewport.Bounds.Height - (bottom + top) * _canvasZoom) / 2 + 20);
        ApplyCanvasTransform();
    }

    private void OnCanvasPanPressed(object? sender, PointerPressedEventArgs args)
    {
        var point = args.GetCurrentPoint((Visual)WorkflowCanvas.Parent!);
        var background = ReferenceEquals(args.Source, WorkflowCanvas) || ReferenceEquals(args.Source, WorkflowCanvas.Parent);
        if (!point.Properties.IsMiddleButtonPressed && !(point.Properties.IsLeftButtonPressed && background)) return;
        if (point.Properties.IsLeftButtonPressed && background) ClearSelectionVisuals();
        _panStart = args.GetPosition((Visual)WorkflowCanvas.Parent!);
        _panOffset = _canvasOffset;
        args.Pointer.Capture((Grid)WorkflowCanvas.Parent!);
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
        var positions = CaptureNodePositions();
        var draftSaved = false;
        try
        {
            await PrototypeServices.SavePrototypeDraftAsync(_selectedActions, GetManualLinkLabels());
            await ConnectionPersistence.SaveWorkflowPositionsAsync(name, positions);
            draftSaved = true;
            if (_enabledWorkflowIds.Contains(PrototypeServices.CurrentWorkflowId))
            {
                var allowCycles = await EnsureCyclesApprovedAsync();
                if (allowCycles is null)
                {
                    TestStatus.Text = _english ? "Draft saved — active version unchanged because the loop was declined" : "Brouillon enregistré — version active inchangée, car la boucle a été refusée";
                    return;
                }
                try
                {
                    await PrototypeServices.PublishPrototypeAsync(_selectedActions, GetManualLinkLabels(), allowCycles.Value);
                }
                catch (InvalidOperationException exception) when (exception.Message.Contains("Commande de chat", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("Chat command", StringComparison.OrdinalIgnoreCase))
                {
                    await ConnectionPersistence.ReconnectAsync(true);
                    await PrototypeServices.PublishPrototypeAsync(_selectedActions, GetManualLinkLabels(), allowCycles.Value);
                }
                await PrototypeServices.ProcessTimersAsync();
                TestStatus.Text = _english ? "Saved — active version updated" : "Enregistrée — version active mise à jour";
            }
            else
            {
                TestStatus.Text = _english ? "Draft saved" : "Brouillon enregistré";
            }
        }
        catch (Exception exception)
        {
            TestStatus.Text = draftSaved
                ? (_english
                    ? $"Draft saved, but the active version could not be updated: {exception.Message}"
                    : $"Brouillon enregistré, mais la version active n’a pas pu être mise à jour : {exception.Message}")
                : (_english ? $"Save failed: {exception.Message}" : $"Échec de sauvegarde : {exception.Message}");
        }
    }

    private async void OnRunOnceClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (!RunOnceButton.IsEnabled)
        {
            TestStatus.Text = _english ? "Activate this automation before running it." : "Active cette automatisation avant de l’exécuter.";
            return;
        }
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
        finally { RunOnceButton.IsEnabled = _enabledWorkflowIds.Contains(PrototypeServices.CurrentWorkflowId); }
    }

    private async void OnWorkflowActivationChanged(object? sender, RoutedEventArgs eventArgs)
    {
        if (_switchingActivation) return;
        var workflowId = PrototypeServices.CurrentWorkflowId;
        var activate = StartWorkflowButton.IsChecked == true;
        if (activate)
        {
            try
            {
                if (!await PrototypeServices.HasPrototypeDraftAsync())
                {
                    TestStatus.Text = _english ? "Save the draft before activating." : "Enregistre le brouillon avant de l’activer.";
                    SetActivationSwitch(false);
                    return;
                }
                var allowCycles = await EnsureCyclesApprovedAsync();
                if (allowCycles is null)
                {
                    TestStatus.Text = _english ? "Activation cancelled — loop declined" : "Activation annulée — boucle refusée";
                    SetActivationSwitch(false);
                    return;
                }
                try
                {
                    await PrototypeServices.PublishPrototypeAsync(_selectedActions, GetManualLinkLabels(), allowCycles.Value);
                }
                catch (InvalidOperationException exception) when (exception.Message.Contains("Commande de chat", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("Chat command", StringComparison.OrdinalIgnoreCase))
                {
                    await ConnectionPersistence.ReconnectAsync(true);
                    await PrototypeServices.PublishPrototypeAsync(_selectedActions, GetManualLinkLabels(), allowCycles.Value);
                }
                await PrototypeServices.ProcessTimersAsync();
                _enabledWorkflowIds.Add(workflowId);
            }
            catch (Exception exception)
            {
                TestStatus.Text = _english ? $"Publish failed: {exception.Message}" : $"Échec de publication : {exception.Message}";
                SetActivationSwitch(false);
                return;
            }
        }
        else
        {
            await PrototypeServices.DisablePrototypeAsync();
            _enabledWorkflowIds.Remove(workflowId);
        }
        if (PrototypeServices.CurrentWorkflowId != workflowId) return;
        SetActivationSwitch(activate);
        TestStatus.Text = !activate
            ? (_english ? "Automation disabled" : "Automatisation désactivée")
            : PrototypeServices.CurrentTriggerType == "manual.run"
                ? (_english ? "Published — click Run once to execute" : "Version publiée — clique Exécuter pour la lancer")
                : (_english ? "Enabled — waiting for a new follower" : "Activée — en attente d’un nouveau follower");
    }

    private void SetActivationSwitch(bool active)
    {
        _ = RefreshWorkflowQueueAsync();
        _switchingActivation = true;
        StartWorkflowButton.IsChecked = active;
        RunOnceButton.IsEnabled = active;
        ToolTip.SetTip(RunOnceButton, _english
            ? (active ? "Run the active published manual branch with real effects" : "Activate this automation to run real actions")
            : (active ? "Exécuter la branche manuelle publiée avec des effets réels" : "Active cette automatisation pour exécuter des actions réelles"));
        _switchingActivation = false;
    }

    private async Task<bool?> EnsureCyclesApprovedAsync()
    {
        if (!PrototypeServices.HasCycle(_selectedActions, GetManualLinkLabels())) return false;
        if (_cycleApprovals.Contains(PrototypeServices.CurrentWorkflowId)) return true;
        if (!await ConfirmCycleAsync()) return null;
        _cycleApprovals.Add(PrototypeServices.CurrentWorkflowId);
        return true;
    }

    private async Task<bool> ConfirmCycleAsync()
    {
        var dialog = new Window
        {
            Title = _english ? "Allow loop?" : "Autoriser la boucle ?",
            Width = 460, Height = 220, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var reject = new Button { Content = _english ? "No, cancel" : "Non, annuler" };
        var accept = new Button { Content = _english ? "Yes, allow loop" : "Oui, autoriser la boucle", Classes = { "primary" } };
        reject.Click += (_, _) => dialog.Close(false);
        accept.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22), Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = _english
                        ? "This connection creates a loop. Allow it? Execution will stop automatically after 1,000 steps if the loop has no exit."
                        : "Cette liaison crée une boucle. Veux-tu l’autoriser? L’exécution sera arrêtée automatiquement après 1 000 étapes si la boucle n’a aucune sortie.",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Children = { reject, accept } }
            }
        };
        return await dialog.ShowDialog<bool>(this);
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
        var positions = CaptureNodePositions();
        foreach (var link in _dynamicLinks) WorkflowCanvas.Children.Remove(link);
        foreach (var label in _dynamicLinkLabels) WorkflowCanvas.Children.Remove(label);
        _dynamicLinks.Clear();
        _dynamicLinkLabels.Clear();
        _linkLabelMap.Clear();
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
        foreach (var node in CanvasNodes())
            if (positions.TryGetValue((string)node.Tag!, out var position))
            {
                Canvas.SetLeft(node, position.X);
                Canvas.SetTop(node, position.Y);
            }
        foreach (var (from, to) in savedLinks)
        {
            var source = FindCanvasNode(from);
            var target = FindCanvasNode(to);
            if (source is not null && target is not null) _manualLinks.Add((source, target));
        }
        UpdateDynamicLinks();
    }

    private CanvasSnapshot CaptureCanvas() => new(_selectedActions.ToArray(),
        CaptureNodePositions(),
        GetManualLinkLabels().ToArray(),
        _selectedActions.Where(PrototypeServices.ActionOptions.ContainsKey).ToDictionary(key => key, key => (System.Text.Json.Nodes.JsonObject)PrototypeServices.ActionOptions[key].DeepClone()),
        PrototypeServices.TriggerOptions.ToDictionary(pair => pair.Key, pair => (System.Text.Json.Nodes.JsonObject)pair.Value.DeepClone()),
        _selectedActions.ToDictionary(key => key, key => PrototypeServices.SourceVisibility.GetValueOrDefault(key, true)), PrototypeServices.CurrentTriggerTypes.ToArray());

    private void RememberCanvas()
    {
        _undoActions.Push(CaptureCanvas());
        _redoActions.Clear();
        UpdateHistoryButtons();
    }

    private void RestoreCanvas(CanvasSnapshot snapshot)
    {
        PrototypeServices.CurrentTriggerTypes.Clear(); PrototypeServices.CurrentTriggerTypes.AddRange(snapshot.Triggers);
        PrototypeServices.CurrentTriggerType = snapshot.Triggers.FirstOrDefault() ?? "";
        _selectedActions.Clear(); _selectedActions.AddRange(snapshot.Actions);
        _manualLinks.Clear();
        foreach (var (key, value) in snapshot.Options) PrototypeServices.ActionOptions[key] = (System.Text.Json.Nodes.JsonObject)value.DeepClone();
        PrototypeServices.TriggerOptions.Clear();
        foreach (var (key, value) in snapshot.TriggerOptions) PrototypeServices.TriggerOptions[key] = (System.Text.Json.Nodes.JsonObject)value.DeepClone();
        foreach (var (key, value) in snapshot.Visibility) PrototypeServices.SourceVisibility[key] = value;
        RebuildActionNodes();
        foreach (var node in CanvasNodes())
            if (snapshot.Positions.TryGetValue((string)node.Tag!, out var point))
            { Canvas.SetLeft(node, point.X); Canvas.SetTop(node, point.Y); }
        foreach (var (from, to) in snapshot.Links)
        {
            var source = FindCanvasNode(from);
            var target = FindCanvasNode(to);
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

    private Border? FindCanvasNode(string label)
    {
        if (string.Equals(label, "__trigger__", StringComparison.Ordinal)) return _triggerNode;
        return _additionalTriggerNodes.FirstOrDefault(node => Equals(node.Tag, label))
            ?? _dynamicNodes.FirstOrDefault(node => Equals(node.Tag, label));
    }

    private IEnumerable<Border> CanvasNodes() => _dynamicNodes.Concat(_additionalTriggerNodes)
        .Concat(_triggerNode is null ? Array.Empty<Border>() : new[] { _triggerNode });

    private Dictionary<string, Point> CaptureNodePositions() => CanvasNodes()
        .Where(node => node.Tag is string).ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node)));

    private void AddTriggerNode()
    {
        if (PrototypeServices.CurrentTriggerTypes.Count == 0)
        {
            if (_triggerNode is not null) WorkflowCanvas.Children.Remove(_triggerNode);
            _triggerNode = null;
            return;
        }
        if (_triggerNode is not null) { UpdateTriggerNode(); return; }
        _triggerNode = new Border
        {
            Width = 230, Height = 112, Background = new SolidColorBrush(Color.Parse("#151A32")),
            BorderBrush = new SolidColorBrush(Color.Parse("#26C281")), BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(0), IsHitTestVisible = false,
            Child = new Grid { RowDefinitions = new RowDefinitions("32,*"), Children =
            {
                new Border { Background = new SolidColorBrush(Color.Parse("#145A43")), CornerRadius = new CornerRadius(9,9,0,0), Padding = new Thickness(10,0), Child = new TextBlock { Text = $"⚡  {TwitchTriggerCatalog.Title(PrototypeServices.CurrentTriggerTypes.FirstOrDefault() ?? "manual.run", _english)}", Foreground = new SolidColorBrush(Color.Parse("#62E6AE")), FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
                new StackPanel { [Grid.RowProperty] = 1, Spacing = 7, Margin = new Thickness(12,8) }
            }}
        };
        _triggerNode.Tag = "__trigger__";
        _triggerNode.Focusable = true;
        _triggerNode.PointerPressed += OnActionNodePressed;
        _triggerNode.PointerMoved += OnActionNodeMoved;
        _triggerNode.PointerReleased += OnActionNodeReleased;
        _triggerNode.IsHitTestVisible = true;
        Canvas.SetLeft(_triggerNode, 60); Canvas.SetTop(_triggerNode, 80);
        WorkflowCanvas.Children.Add(_triggerNode);
        AddEventDeleteButton(_triggerNode);
        _triggerNode.KeyDown += OnAdditionalTriggerKeyDown;
        UpdateTriggerNode();
    }

    private void UpdateTriggerNode()
    {
        if (_triggerNode is null) return;
        var title = TwitchTriggerCatalog.Title(PrototypeServices.CurrentTriggerTypes.FirstOrDefault() ?? "manual.run", _english);
        if (_triggerNode.Child is Grid grid && grid.Children[0] is Border headerBorder)
        {
            var header = headerBorder.Child as TextBlock ?? (headerBorder.Child as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();
            if (header is not null) header.Text = $"⚡  {title}";
        }
        ConfigureTriggerBody(_triggerNode, PrototypeServices.CurrentTriggerTypes.FirstOrDefault() ?? "manual.run");
    }

    private void ConfigureTriggerBody(Border node, string trigger)
    {
        if (node.Child is not Grid grid || grid.Children[1] is not StackPanel body) return;
        body.Children.Clear();
        if (trigger == "manual.run")
        {
            var run = new Button { Content = _english ? "▶  Run once" : "▶  Exécuter", Classes = { "eventAdd" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Tag = "manualRunButton" };
            ToolTip.SetTip(run, _english ? "Execute the published manual branch with real effects" : "Exécuter la branche manuelle publiée avec des effets réels");
            run.Click += OnRunOnceClick;
            body.Children.Add(run);
            node.Height = 112;
            return;
        }
        if (trigger == "core.timer")
        {
            node.Height = 142;
            var key = (string)node.Tag!;
            var options = PrototypeServices.GetOrCreateTriggerOptions(key,
                () => new System.Text.Json.Nodes.JsonObject { ["seconds"] = "60" });
            body.Children.Add(new TextBlock { Text = _english ? "Interval (seconds)" : "Intervalle (secondes)", FontSize = 11, FontWeight = FontWeight.SemiBold });
            var seconds = new TextBox { Text = options["seconds"]?.ToString() ?? "60", PlaceholderText = "60", Tag = "timerSeconds" };
            seconds.TextChanged += (_, _) =>
            {
                options["seconds"] = seconds.Text?.Trim() ?? "";
                TestStatus.Text = _english ? "Timer changed — save to apply" : "Minuteur modifié — enregistre pour appliquer";
            };
            body.Children.Add(seconds);
            return;
        }
        if (trigger == "twitch.channel.chat.message.received")
        {
            node.Width = 280; node.Height = 142;
            var options = PrototypeServices.GetOrCreateTriggerOptions((string)node.Tag!);
            body.Children.Add(CreateFieldLabel("Commande", "Command"));
            var picker = new ComboBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            EventHandler<SelectionChangedEventArgs>? selectionHandler = null;
            var manage = new Button { Content = "⚙", Width = 38 };
            ToolTip.SetTip(manage, _english ? "Manage commands" : "Gérer les commandes");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            row.Children.Add(picker); Grid.SetColumn(manage, 1); row.Children.Add(manage); body.Children.Add(row);
            async Task Load()
            {
                try
                {
                    var commands = await ChatCommands.ListAsync();
                    if (selectionHandler is not null) picker.SelectionChanged -= selectionHandler;
                    if (options["commandId"] is null && options["command"] is { } legacy)
                    {
                        var existing = commands.FirstOrDefault(item => string.Equals(item["command"]?.ToString(), legacy.ToString(), StringComparison.OrdinalIgnoreCase));
                        if (existing is null) { existing = (System.Text.Json.Nodes.JsonObject)options.DeepClone(); existing["id"] = Guid.NewGuid().ToString(); await ChatCommands.SaveAsync(existing); commands.Add(existing); }
                        options["commandId"] = existing["id"]?.ToString();
                    }
                    picker.ItemsSource = commands.Select(item => item["command"]?.ToString()).ToArray();
                    picker.SelectedIndex = commands.FindIndex(item => item["id"]?.ToString() == options["commandId"]?.ToString());
                    selectionHandler = (_, _) =>
                    {
                        if (picker.SelectedIndex < 0 || picker.SelectedIndex >= commands.Count) return;
                        foreach (var pair in commands[picker.SelectedIndex]) options[pair.Key] = pair.Value?.DeepClone();
                        options["commandId"] = commands[picker.SelectedIndex]["id"]?.ToString();
                    };
                    picker.SelectionChanged += selectionHandler;
                }
                catch (Exception ex) { TestStatus.Text = ex.Message; }
            }
            manage.Click += async (_, _) => { await new CommandsWindow(_english).ShowDialog(this); await Load(); };
            _ = Load();
            return;
        }
        if (trigger == "twitch.channel.channel_points_custom_reward_redemption.add.received")
        {
            node.Height = 142;
            var key = (string)node.Tag!;
            var options = PrototypeServices.GetOrCreateTriggerOptions(key, () => new System.Text.Json.Nodes.JsonObject { ["reward"] = "" });
            body.Children.Add(new TextBlock { Text = _english ? "Reward (optional)" : "Récompense (facultatif)", FontSize = 11, FontWeight = FontWeight.SemiBold });
            var savedReward = options["reward"]?.ToString() ?? "";
            var rewardItems = string.IsNullOrWhiteSpace(savedReward)
                ? new[] { _english ? "All rewards" : "Toutes les récompenses" }
                : new[] { _english ? "All rewards" : "Toutes les récompenses", savedReward };
            var reward = new ComboBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, ItemsSource = rewardItems, SelectedIndex = string.IsNullOrWhiteSpace(savedReward) ? 0 : 1 };
            var refreshRewards = new Button { Content = "↻", Width = 34 };
            ToolTip.SetTip(refreshRewards, _english ? "Load Twitch rewards" : "Charger les récompenses Twitch");
            refreshRewards.Click += async (_, _) =>
            {
                try
                {
                    var rewards = await PrototypeServices.GetTwitchRewardsAsync();
                    reward.ItemsSource = new[] { _english ? "All rewards" : "Toutes les récompenses" }.Concat(rewards.Select(item => item.Title)).ToArray();
                    var selected = options["reward"]?.ToString();
                    reward.SelectedIndex = string.IsNullOrWhiteSpace(selected) ? 0 : Math.Max(0, Array.IndexOf(((IEnumerable<string>)reward.ItemsSource).ToArray(), selected));
                }
                catch (Exception ex) { TestStatus.Text = ex.Message; }
            };
            reward.SelectionChanged += (_, _) => options["reward"] = reward.SelectedIndex <= 0 ? "" : reward.SelectedItem?.ToString() ?? "";
            var rewardRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            rewardRow.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
            rewardRow.Children.Add(reward); Grid.SetColumn(refreshRewards, 1); rewardRow.Children.Add(refreshRewards);
            var createReward = new Button { Content = "+", Width = 34 };
            ToolTip.SetTip(createReward, _english ? "Create a Twitch reward" : "Créer une récompense Twitch");
            createReward.Click += async (_, _) =>
            {
                var titleBox = new TextBox { PlaceholderText = _english ? "Reward name" : "Nom de la récompense" };
                var costBox = new TextBox { Text = "100", PlaceholderText = _english ? "Cost" : "Coût" };
                var ok = new Button { Content = _english ? "Create" : "Créer", Classes = { "primary" } };
                var createDialog = new Window { Title = _english ? "Create reward" : "Créer une récompense", Width = 320, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { new TextBlock { Text = _english ? "Reward name" : "Nom" }, titleBox, new TextBlock { Text = _english ? "Cost" : "Coût" }, costBox, ok } } };
                ok.Click += (_, _) => createDialog.Close(true);
                if (await createDialog.ShowDialog<bool>(this) && int.TryParse(costBox.Text, out var cost) && !string.IsNullOrWhiteSpace(titleBox.Text))
                {
                    try
                    {
                        var createdTitle = titleBox.Text.Trim();
                        await PrototypeServices.CreateTwitchRewardAsync(createdTitle, cost);
                        reward.ItemsSource = new[] { _english ? "All rewards" : "Toutes les récompenses", createdTitle };
                        reward.SelectedIndex = 1;
                        options["reward"] = createdTitle;
                        TestStatus.Text = _english ? "Reward created and selected" : "Récompense créée et sélectionnée";
                    }
                    catch (Exception ex) { TestStatus.Text = ex.Message; }
                }
            };
            Grid.SetColumn(createReward, 2); rewardRow.Children.Add(createReward);
            body.Children.Add(rewardRow);
            return;
        }
        node.Height = 100;
    }

    private void AddAdditionalTriggerNode(string trigger)
    {
        if (_additionalTriggerNodes.Any(node => Equals(node.Tag, $"__trigger__:{trigger}"))) return;
        var title = TwitchTriggerCatalog.Title(trigger, _english);
        var node = new Border { Width = 230, Height = 100, Background = new SolidColorBrush(Color.Parse("#151A32")), BorderBrush = new SolidColorBrush(Color.Parse("#26C281")), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Padding = new Thickness(0), Tag = $"__trigger__:{trigger}" };
        node.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("32,*"),
            Children =
            {
                new Border { Background = new SolidColorBrush(Color.Parse("#145A43")), CornerRadius = new CornerRadius(9,9,0,0), Padding = new Thickness(10,0), Child = new TextBlock { Text = _english ? $"⚡  {title}" : $"⚡  {title}", Foreground = new SolidColorBrush(Color.Parse("#62E6AE")), FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
                new StackPanel { [Grid.RowProperty] = 1, Margin = new Thickness(12,0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
            }
        };
        node.PointerPressed += OnActionNodePressed; node.PointerReleased += OnActionNodeReleased;
        node.PointerMoved += OnActionNodeMoved;
        ConfigureTriggerBody(node, trigger);
        AddEventDeleteButton(node);
        node.Focusable = true;
        node.KeyDown += OnAdditionalTriggerKeyDown;
        Canvas.SetLeft(node, 60); Canvas.SetTop(node, 210 + _additionalTriggerNodes.Count * 105);
        WorkflowCanvas.Children.Add(node); _additionalTriggerNodes.Add(node);
    }

    private void AddEventDeleteButton(Border node)
    {
        if (node.Child is Grid layout && layout.Children[0] is Border header)
        {
            var titleControl = header.Child!;
            header.Child = null;
            var remove = new Button { Content = "×", Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Padding = new Thickness(4, 0), FontSize = 15, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            ToolTip.SetTip(remove, _english ? "Delete event" : "Supprimer l’événement");
            remove.Tag = "deleteEventButton";
            remove.Click += (_, _) => DeleteAdditionalTrigger(node);
            header.Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { titleControl, remove } };
            Grid.SetColumn(remove, 1);
        }
    }

    private void OnAdditionalTriggerKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Delete || sender is not Border node) return;
        DeleteAdditionalTrigger(node);
        eventArgs.Handled = true;
    }

    internal void DeleteAdditionalTrigger(Border node)
    {
        if (node == _triggerNode)
        {
            var snapshot = CaptureCanvas();
            RememberCanvas();
            var remaining = snapshot.Triggers.Skip(1).ToArray();
            var promotedKey = remaining.Length > 0 ? $"__trigger__:{remaining[0]}" : null;
            var links = snapshot.Links.Where(edge => edge.From != "__trigger__" && edge.To != "__trigger__")
                .Select(edge => (edge.From == promotedKey ? "__trigger__" : edge.From, edge.To == promotedKey ? "__trigger__" : edge.To)).ToArray();
            var positions = new Dictionary<string, Point>(snapshot.Positions);
            positions.Remove("__trigger__");
            if (promotedKey is not null && positions.Remove(promotedKey, out var promotedPosition)) positions["__trigger__"] = promotedPosition;
            WorkflowCanvas.Children.Remove(node); _triggerNode = null;
            RestoreCanvas(snapshot with { Triggers = remaining, Links = links, Positions = positions });
            ClearSelectionVisuals();
            TestStatus.Text = _english ? "Event removed" : "Événement supprimé";
            UpdateHistoryButtons();
            return;
        }
        if (!_additionalTriggerNodes.Contains(node) || node.Tag is not string tag || !tag.StartsWith("__trigger__:", StringComparison.Ordinal)) return;
        var trigger = tag[12..];
        RememberCanvas();
        _additionalTriggerNodes.Remove(node);
        WorkflowCanvas.Children.Remove(node);
        PrototypeServices.CurrentTriggerTypes.Remove(trigger);
        _manualLinks.RemoveAll(edge => edge.From == node || edge.To == node);
        UpdateDynamicLinks();
        ClearSelectionVisuals();
        TestStatus.Text = _english ? "Additional event removed" : "Événement supplémentaire supprimé";
        UpdateHistoryButtons();
    }

    private void AddActionNode(string label)
    {
        var palette = GetActionPalette(label);
        var title = LocalizedActionTitle(label);
        var node = new Border
        {
            Width = 240,
            MinHeight = PrototypeServices.ActionTypeFor(label) == "core.condition" ? 230 : 72,
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
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock { Text = $"◆  {ActionCategoryLabel(label)} · {(_english ? title.English : title.French)}", Foreground = new SolidColorBrush(Color.Parse(palette.Text)), FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0,0,6,0) },
                    deleteButton
                }
            }
        };
        Grid.SetColumn(deleteButton, 1);
        node.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("32"),
            Children = { header }
        };
        if (label.Contains("source", StringComparison.OrdinalIgnoreCase))
        {
            var layout = (Grid)node.Child;
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
            visibility.SelectionChanged += (_, _) =>
            {
                if (visibility.SelectedIndex < 0) return;
                PrototypeServices.SourceVisibility[label] = visibility.SelectedIndex == 0;
                TestStatus.Text = _english ? "Draft modified" : "Brouillon modifié";
            };
            AppendNodeSection(layout, visibilityField);
        }
        Canvas.SetLeft(node, 70 + (_addedActionCount % 3) * 230);
        AddInlineActionOptions(node, label);
        Canvas.SetTop(node, 520 + (_addedActionCount / 3) * 165);
        WorkflowCanvas.Children.Add(node);
        _dynamicNodes.Add(node);
        _addedActionCount++;
    }

    private string ActionCategoryLabel(string label)
    {
        var kind = PrototypeServices.ActionTypeFor(label);
        if (kind.StartsWith("core.", StringComparison.Ordinal) || kind.StartsWith("variables.", StringComparison.Ordinal) || kind.StartsWith("audio.", StringComparison.Ordinal))
            return _english ? "SYSTEM" : "SYSTÈME";
        return kind.StartsWith("obs.", StringComparison.Ordinal) ? "OBS" : "Twitch";
    }

    private static void AppendNodeSection(Grid layout, Control section)
    {
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(section, layout.RowDefinitions.Count - 1);
        layout.Children.Add(section);
    }

    private static string[] ObsOperationCaptions(IEnumerable<string> operations, bool english) => operations.Select(value => value switch
    {
        "start" => english ? "Start" : "Démarrer", "stop" => english ? "Stop" : "Arrêter", "pause" => english ? "Pause" : "Mettre en pause", "resume" => english ? "Resume" : "Reprendre", "save" => english ? "Save replay" : "Enregistrer le replay", _ => value
    }).ToArray();

    private void AddInlineActionOptions(Border node, string label)
    {
        var type = PrototypeServices.ActionTypeFor(label);
        if (type.StartsWith("obs.output.", StringComparison.Ordinal))
        {
            if (!PrototypeServices.ActionOptions.TryGetValue(label, out var outputOptions)) PrototypeServices.ActionOptions[label] = outputOptions = new System.Text.Json.Nodes.JsonObject();
            var values = type == "obs.output.record" ? new[] { "start", "stop", "pause", "resume" } : type == "obs.output.replay" ? new[] { "start", "stop", "save" } : new[] { "start", "stop" };
            var picker = new ComboBox { Tag = "obsOperations:" + type, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            picker.ItemsSource = ObsOperationCaptions(values, _english);
            picker.SelectedIndex = Math.Max(0, Array.IndexOf(values, outputOptions["operation"]?.ToString() ?? "start"));
            outputOptions["operation"] = values[picker.SelectedIndex];
            picker.SelectionChanged += (_, _) => { if (picker.SelectedIndex >= 0) outputOptions["operation"] = values[picker.SelectedIndex]; };
            var outputForm = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 12, 10), Children = { CreateFieldLabel("Opération", "Operation"), picker } };
            var layout = (Grid)node.Child!;
            AppendNodeSection(layout, outputForm);
            return;
        }
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
        else if (name.Contains("récompense") || name.Contains("reward")) { fields.Add(("title", "Nom de la récompense", "Reward title")); fields.Add(("cost", "Coût en points", "Cost in points")); }
        else if (name.Contains("message")) fields.Add(("message", "Message à envoyer (ex. Bonjour %visites%)", "Message to send (e.g. Hello %visits%)"));
        else if (name.Contains("variable"))
        {
            fields.Add(("name", "Nom de la variable", "Variable name"));
            fields.Add(("value", "Valeur ou calcul (+ − × /)", "Value or calculation (+ − × /)"));
        }
        else if (name.Contains("condition"))
        {
            fields.Add(("name", "Variable à comparer", "Variable to compare"));
            fields.Add(("value", "Valeur attendue", "Expected value"));
        }
        else if (name.Contains("sound") || name.Contains("son")) fields.Add(("file", "Fichier audio", "Audio file"));
        else if (name.Contains("utilisateur") || name.Contains("user")) fields.Add(("outputVariable", "Variable de sortie", "Output variable"));
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
                ItemsSource = _english ? new[] { "Equal to", "Greater than", "Greater than or equal", "Less than", "Less than or equal" } : new[] { "Égal à", "Supérieur à", "Supérieur ou égal", "Inférieur à", "Inférieur ou égal" },
                SelectedIndex = options["comparison"]?.ToString() switch { "greater" => 1, "greaterOrEqual" => 2, "less" => 3, "lessOrEqual" => 4, _ => 0 },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            comparison.SelectionChanged += (_, _) => { if (comparison.SelectedIndex >= 0) options["comparison"] = comparison.SelectedIndex switch { 1 => "greater", 2 => "greaterOrEqual", 3 => "less", 4 => "lessOrEqual", _ => "equals" }; };
            form.Children.Add(comparison);
            form.Children.Add(CreateFieldLabel("Portée de la variable", "Variable scope"));
            var conditionScope = new ComboBox { ItemsSource = _english ? new[] { "System", "Viewer" } : new[] { "Système", "Viewer" }, SelectedIndex = options["scope"]?.ToString() == "viewer" ? 1 : 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            conditionScope.SelectionChanged += (_, _) => options["scope"] = conditionScope.SelectedIndex == 1 ? "viewer" : "system";
            form.Children.Add(conditionScope); options["scope"] ??= "system";
        }
        if (name.Contains("variable"))
        {
            if (options["operation"]?.ToString() is { } legacyOperation && options["value"]?.ToString() is { Length: > 0 } legacyValue)
            {
                options["value"] = legacyOperation == "increment"
                    ? (legacyValue.StartsWith('+') || legacyValue.StartsWith('-') ? legacyValue : "+" + legacyValue)
                    : "=" + legacyValue;
                options.Remove("operation");
            }
            form.Children.Add(CreateFieldLabel("Type de variable", "Variable type"));
            var valueType = new ComboBox
            {
                Tag = "variableTypes",
                ItemsSource = _english ? new[] { "Number", "Text" } : new[] { "Nombre", "Texte" },
                SelectedIndex = options["valueType"]?.ToString() == "text" ? 1 : 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            form.Children.Add(valueType);
            void ApplyVariableType()
            {
                var isText = valueType.SelectedIndex == 1;
                options["valueType"] = isText ? "text" : "integer";
            }
            valueType.SelectionChanged += (_, _) =>
            {
                ApplyVariableType();
                TestStatus.Text = _english ? "Draft modified" : "Brouillon modifié";
            };
            ApplyVariableType();
            form.Children.Add(CreateFieldLabel("Portée de la variable", "Variable scope"));
            var scope = new ComboBox { ItemsSource = _english ? new[] { "System", "Viewer" } : new[] { "Système", "Viewer" }, SelectedIndex = options["scope"]?.ToString() == "viewer" ? 1 : 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var viewerInfo = new TextBlock { Text = _english ? "Uses the viewer who triggered this workflow." : "Utilise le viewer qui a déclenché cette automatisation.", Foreground = Brush.Parse("#9DA8CB"), FontSize = 11, TextWrapping = TextWrapping.Wrap, IsVisible = scope.SelectedIndex == 1 };
            scope.SelectionChanged += (_, _) => { options["scope"] = scope.SelectedIndex == 1 ? "viewer" : "system"; viewerInfo.IsVisible = scope.SelectedIndex == 1; };
            form.Children.Add(scope);
            form.Children.Add(viewerInfo);
            form.Children.Add(CreateFieldLabel("Durée", "Lifetime"));
            var lifetime = new ComboBox { ItemsSource = _english ? new[] { "Temporary", "Permanent" } : new[] { "Temporaire", "Permanente" }, SelectedIndex = options["lifetime"]?.ToString() == "temporary" ? 0 : 1, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            lifetime.SelectionChanged += (_, _) => options["lifetime"] = lifetime.SelectedIndex == 0 ? "temporary" : "permanent";
            form.Children.Add(lifetime);
            options["scope"] ??= "system"; options["lifetime"] ??= "permanent";
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
                FontSize = 12,
                IsReadOnly = key == "file",
                IsHitTestVisible = key != "file",
                Focusable = key != "file",
                AcceptsReturn = key == "message",
                TextWrapping = key == "message" ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MaxLength = key == "message" ? 500 : int.MaxValue,
                MinHeight = key == "message" ? 54 : 0,
                MaxHeight = key == "message" ? 150 : double.PositiveInfinity
            };
            if (key == "message")
            {
                input.Height = 54;
                ToolTip.SetTip(input, _english ? "Maximum 500 characters (Twitch limit)" : "Maximum 500 caractères (limite Twitch)");
            }
            if (key == "seconds" && options[key] is null) options[key] = "1";
            ToolTip.SetTip(input, _english ? en : fr);
            input.TextChanged += (_, _) =>
            {
                options[key] = input.Text ?? "";
                if (key == "message")
                {
                    var value = input.Text ?? "";
                    var visualLines = value.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(Math.Max(1, line.Length) / 34d)));
                    input.Height = Math.Clamp(34 + visualLines * 20, 54, 150);
                    TestStatus.Text = value.Length >= 500
                        ? (_english ? "Twitch message limit reached (500 characters)" : "Limite Twitch atteinte (500 caractères)")
                        : (_english ? "Draft modified" : "Brouillon modifié");
                    return;
                }
                TestStatus.Text = _english ? "Draft modified" : "Brouillon modifié";
            };
            if (key == "file")
            {
                var browse = new Button { Content = "📁", Width = 42, Padding = new Thickness(6), Classes = { "primary" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
                ToolTip.SetTip(browse, _english ? "Browse for an MP3 or WAV file" : "Parcourir un fichier MP3 ou WAV");
                browse.Click += async (_, _) =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = _english ? "Choose an audio file" : "Choisir un fichier audio",
                        AllowMultiple = false,
                        FileTypeFilter = [new FilePickerFileType("MP3 / WAV") { Patterns = ["*.mp3", "*.wav"] }]
                    });
                    if (files.Count == 0) return;
                    var path = files[0].TryGetLocalPath();
                    if (string.IsNullOrWhiteSpace(path)) return;
                    input.Text = path;
                };
                input.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                var fileRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
                fileRow.Children.Add(input);
                Grid.SetColumn(browse, 1);
                fileRow.Children.Add(browse);
                form.Children.Add(fileRow);
            }
            else form.Children.Add(input);
        }
        var grid = (Grid)node.Child!;
        AppendNodeSection(grid, form);
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
            ,TextWrapping = TextWrapping.Wrap
        };
        return text;
    }

    private static (string French, string English) LocalizedActionTitle(string key) => PrototypeServices.ActionTypeFor(key) switch
    {
        "obs.output.record" => ("Enregistrement OBS", "OBS recording"),
        "obs.output.replay" => ("Tampon replay", "Replay buffer"),
        "obs.output.virtual-camera" => ("Caméra virtuelle", "Virtual camera"),
        "obs.set-source-visible" => ("Visibilité d’une source", "Source visibility"),
        "obs.scene.set-current" => ("Changer de scène", "Change scene"),
        "obs.replay.start" => ("Démarrer un replay", "Start replay"),
        "obs.screenshot" => ("Prendre une capture", "Take a screenshot"),
        "twitch.update-title" => ("Modifier le titre", "Edit title"),
        "twitch.send-message" => ("Envoyer un message", "Send a message"),
        "twitch.create-clip" => ("Créer un clip", "Create a clip"),
        "twitch.get-user" => ("Récupérer un utilisateur", "Get a user"),
        "twitch.create-reward" => ("Créer une récompense de points", "Create channel points reward"),
        "core.delay" => ("Attendre", "Wait"),
        "core.condition" => ("Condition", "Condition"),
        "variables.set-integer" => ("Modifier une variable", "Edit a variable"),
        "variables.set-text" => ("Modifier une variable", "Edit a variable"),
        "variables.number-expression" => ("Modifier une variable", "Edit a variable"),
        "audio.play" => ("Jouer un son", "Play a sound"),
        _ => ("Action", "Action")
    };

    private static (string Header, string Border, string Icon, string Text) GetActionPalette(string label)
    {
        if (label.StartsWith("__trigger__", StringComparison.Ordinal))
            return ("#145A43", "#26C281", "#20865F", "#62E6AE");
        var value = label.ToLowerInvariant();
        if (PrototypeServices.ActionTypeFor(label).StartsWith("obs.", StringComparison.Ordinal)) return ("#20242C", "#737B8A", "#343A4D", "#DCE2EE");
        if (value.Contains("obs") || value.Contains("scène") || value.Contains("scene") || value.Contains("source") || value.Contains("replay") || value.Contains("capture"))
            return ("#20242C", "#737B8A", "#343A4D", "#DCE2EE");
        if (value.Contains("condition") || value.Contains("attendre") || value.Contains("wait") || value.Contains("variable") || value.Contains("compteur") || value.Contains("sound") || value.Contains("son"))
            return ("#173C73", "#3788F5", "#275AA0", "#7EB8FF");
        return ("#4A206F", "#A640F4", "#6F35BE", "#D39AFF");
    }

    private void RestoreNodeBorder(Border node)
    {
        var isTrigger = node == _triggerNode || (node.Tag as string)?.StartsWith("__trigger__:", StringComparison.Ordinal) == true;
        node.BorderBrush = new SolidColorBrush(Color.Parse(isTrigger ? "#26C281" : GetActionPalette(node.Tag as string ?? "").Border));
    }

    private void ClearSelectionVisuals()
    {
        foreach (var node in _dynamicNodes) RestoreNodeBorder(node);
        if (_triggerNode is not null) RestoreNodeBorder(_triggerNode);
        foreach (var node in _additionalTriggerNodes) RestoreNodeBorder(node);
        _selectedNode = null;
    }

    private void DeleteActionNode(Border node)
    {
        if (node == _triggerNode || _additionalTriggerNodes.Contains(node)) return;
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
        var point = eventArgs.GetCurrentPoint(node);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            ClearSelectionVisuals();
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
        foreach (var label in _dynamicLinkLabels) WorkflowCanvas.Children.Remove(label);
        foreach (var staleLabel in WorkflowCanvas.Children.OfType<Border>().Where(border => Equals(border.Tag, "branchLabel")).ToArray())
            WorkflowCanvas.Children.Remove(staleLabel);
        _dynamicLinks.Clear();
        _dynamicLinkLabels.Clear();
        _linkLabelMap.Clear();
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
            var fromWidth = NodeWidth(from);
            var fromHeight = NodeHeight(from);
            var toWidth = NodeWidth(to);
            var toHeight = NodeHeight(to);
            var fromCenter = new Point(fromLeft + fromWidth / 2, fromTop + fromHeight / 2);
            var toCenter = new Point(toLeft + toWidth / 2, toTop + toHeight / 2);
            var branchOutput = from.Tag is string branchSourceKey && to.Tag is string branchTargetKey
                ? PrototypeServices.ActionOptions.GetValueOrDefault(branchSourceKey)?["branches"]?[branchTargetKey]?.ToString() : null;
            var horizontal = Math.Abs(toCenter.X - fromCenter.X) >= Math.Abs(toCenter.Y - fromCenter.Y);
            var targetIndex = targetGroups[to].IndexOf((from, to));
            var targetCount = targetGroups[to].Count;
            var spread = (targetIndex - (targetCount - 1) / 2d) * 26;
            var branchOffset = branchOutput == "yes" ? -24 : branchOutput == "no" ? 24 : 0;
            var start = horizontal
                ? new Point(toCenter.X >= fromCenter.X ? fromLeft + fromWidth : fromLeft, fromCenter.Y + branchOffset)
                : new Point(fromCenter.X + branchOffset, toCenter.Y >= fromCenter.Y ? fromTop + fromHeight : fromTop);
            var end = horizontal
                ? new Point(toCenter.X >= fromCenter.X ? toLeft - 2 : toLeft + toWidth + 2, toCenter.Y + spread)
                : new Point(toCenter.X + spread, toCenter.Y >= fromCenter.Y ? toTop - 2 : toTop + toHeight + 2);
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
            var link = new Avalonia.Controls.Shapes.Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(Color.Parse(branchOutput switch
                {
                    "yes" => "#53D994", "no" => "#F05B68", _ => GetActionPalette(from.Tag as string ?? "").Border
                })),
                StrokeThickness = 3,
                StrokeLineCap = PenLineCap.Round,
                ZIndex = 10
            };
            link.PointerPressed += OnWorkflowLinkPressed;
            WorkflowCanvas.Children.Insert(0, link);
            _dynamicLinks.Add(link);
            _linkMap[link] = (from, to);
            if (branchOutput is "yes" or "no")
            {
                var branchLabel = new Border
                {
                    Tag = "branchLabel",
                    Background = new SolidColorBrush(Color.Parse(branchOutput == "yes" ? "#145A43" : "#702735")),
                    BorderBrush = new SolidColorBrush(Color.Parse(branchOutput == "yes" ? "#53D994" : "#F05B68")),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 2),
                    Child = new TextBlock { Text = branchOutput == "yes" ? (_english ? "True" : "Vrai") : (_english ? "False" : "Faux"), Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeight.SemiBold }
                };
                Canvas.SetLeft(branchLabel, (start.X + end.X) / 2 - 20);
                Canvas.SetTop(branchLabel, (start.Y + end.Y) / 2 - 12);
                branchLabel.ZIndex = 11;
                branchLabel.PointerPressed += OnBranchLabelPressed;
                WorkflowCanvas.Children.Add(branchLabel);
                _dynamicLinkLabels.Add(branchLabel);
                _linkLabelMap[branchLabel] = (from, to);
            }
        }
    }

    private static double NodeWidth(Control node) =>
        double.IsFinite(node.Width) ? node.Width : Math.Max(node.MinWidth, node.Bounds.Width);

    private static double NodeHeight(Control node) =>
        double.IsFinite(node.Height) ? node.Height : Math.Max(node.MinHeight, node.Bounds.Height);

    private async void OnWorkflowLinkPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Avalonia.Controls.Shapes.Path line || !_linkMap.TryGetValue(line, out var link)) return;
        if (eventArgs.GetCurrentPoint(line).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed) return;
        var dialog = new Window { Title = _english ? "Delete link" : "Supprimer le lien", Width = 380, Height = 170, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = new Button { Content = _english ? "Cancel" : "Annuler" };
        var remove = new Button { Content = _english ? "Delete" : "Supprimer", Classes = { "danger" } };
        cancel.Click += (_, _) => dialog.Close(false); remove.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children = {
            new TextBlock { Text = _english ? "Do you really want to remove this link?" : "Veux-tu vraiment supprimer ce lien?", TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Children = { cancel, remove } }
        }};
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed) { TestStatus.Text = _english ? "Link kept" : "Lien conservé"; return; }
        if (!_manualLinks.Contains(link))
        {
            TestStatus.Text = _english ? "This link no longer exists" : "Ce lien n’existe plus";
            return;
        }
        TestStatus.Text = _english ? "Removing link…" : "Suppression du lien…";
        RememberCanvas();
        _manualLinks.Remove(link);
        UpdateDynamicLinks();
        TestStatus.Text = _english ? "Link deleted" : "Lien supprimé";
        eventArgs.Handled = true;
    }

    private void OnBranchLabelPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Border label || !_linkLabelMap.TryGetValue(label, out var link)) return;
        if (eventArgs.GetCurrentPoint(label).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (link.From.Tag is not string sourceKey || link.To.Tag is not string targetKey ||
            !PrototypeServices.ActionOptions.TryGetValue(sourceKey, out var options)) return;
        if (options["branches"] is not System.Text.Json.Nodes.JsonObject branches)
            options["branches"] = branches = new System.Text.Json.Nodes.JsonObject();
        var next = branches[targetKey]?.ToString() == "yes" ? "no" : "yes";
        branches[targetKey] = next;
        TestStatus.Text = next == "yes"
            ? (_english ? "Link changed to True" : "Liaison changée vers Vrai")
            : (_english ? "Link changed to False" : "Liaison changée vers Faux");
        UpdateDynamicLinks();
        eventArgs.Handled = true;
    }

    private async void OnActionNodeReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_draggedNode == sender)
        {
            eventArgs.Pointer.Capture(null);
            if (sender is Border node && Math.Abs(Canvas.GetLeft(node) - _dragNodeLeft) < 3 && Math.Abs(Canvas.GetTop(node) - _dragNodeTop) < 3)
            {
                if (_selectedNode is null || _selectedNode == node)
                {
                    ClearSelectionVisuals();
                    _selectedNode = node;
                    node.BorderBrush = new SolidColorBrush(Color.Parse("#FFD166"));
                    TestStatus.Text = _english ? "Source selected — click a destination" : "Source sélectionnée — clique une destination";
                }
                else
                {
                    if (node == _triggerNode || _additionalTriggerNodes.Contains(node))
                    {
                        TestStatus.Text = _english ? "Links can only enter actions from an event" : "Les liens doivent partir d’un événement vers une action";
                        ClearSelectionVisuals();
                        _selectedNode = node;
                        node.BorderBrush = new SolidColorBrush(Color.Parse("#FFD166"));
                        _draggedNode = null;
                        return;
                    }
                    if (_manualLinks.Contains((_selectedNode, node)))
                    {
                        TestStatus.Text = _english ? "This link already exists" : "Ce lien existe déjà";
                        ClearSelectionVisuals();
                        _draggedNode = null;
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
                        if (!await ConfirmCycleAsync())
                        {
                            TestStatus.Text = _english ? "Loop declined — link not created" : "Boucle refusée — liaison non créée";
                            _draggedNode = null; _dragStartSnapshot = null;
                            return;
                        }
                        _cycleApprovals.Add(PrototypeServices.CurrentWorkflowId);
                    }
                    RememberCanvas();
                    _manualLinks.Add((_selectedNode, node));
                    if (_selectedNode.Tag is string sourceKey && node.Tag is string targetKey && PrototypeServices.ActionTypeFor(sourceKey) == "core.condition"
                        && PrototypeServices.ActionOptions.TryGetValue(sourceKey, out var sourceOptions))
                    {
                        if (sourceOptions["branches"] is not System.Text.Json.Nodes.JsonObject branches)
                            sourceOptions["branches"] = branches = new System.Text.Json.Nodes.JsonObject();
                        branches[targetKey] = sourceOptions["linkOutput"]?.ToString() ?? "yes";
                    }
                    RestoreNodeBorder(_selectedNode);
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
        UpdateTriggerNode();
        foreach (var node in _additionalTriggerNodes)
            if (node.GetVisualDescendants().OfType<TextBlock>().ToArray() is var labels && labels.Length > 0)
            {
                var eventTitle = TwitchTriggerCatalog.Title(((string)node.Tag!)[12..], english);
                if (labels[0] is { } header) header.Text = $"⚡  {eventTitle}";
            }
        var triggerIndex = WorkflowTriggerSelector.SelectedIndex;
        var switching = _switchingWorkflow;
        _switchingWorkflow = true;
        WorkflowTriggerSelector.ItemsSource = english ? new[] { "Manual", "New follower" } : new[] { "Manuel", "Nouveau follower" };
        WorkflowTriggerSelector.SelectedIndex = triggerIndex;
        _switchingWorkflow = switching;
        RunOnceButton.Content = english ? "Run once" : "Exécuter";
        ToolTip.SetTip(RunOnceButton, english
            ? (RunOnceButton.IsEnabled ? "Run the active published manual branch with real effects" : "Activate this automation to run real actions")
            : (RunOnceButton.IsEnabled ? "Exécuter la branche manuelle publiée avec des effets réels" : "Active cette automatisation pour exécuter des actions réelles"));
        UiLanguageState.IsEnglish = english;
        ManagementButtons.Save(SaveWorkflowButton, english);
        ManagementButtons.History(UndoButton, english, false);
        ManagementButtons.History(RedoButton, english, true);
        UpdateWorkflowQueueLabel();
        CommandsButton.Content = english ? "⌨   Commands" : "⌨   Commandes";
        QueuesButton.Content = english ? "☷   Alert queues" : "☷   Files d’alertes";
        RewardsButton.Content = english ? "◎   Rewards" : "◎   Récompenses";
        StartWorkflowButton.OnContent = english ? "Active" : "Activée";
        StartWorkflowButton.OffContent = english ? "Inactive" : "Désactivée";
        ToolTip.SetTip(StartWorkflowButton, english ? "Enable or disable automatic triggers" : "Active ou désactive les déclencheurs automatiques");
        var translations = _english
            ? new Dictionary<string, string>
            {
                ["Mon Live Automatisation"] = "My Live Automation", ["Accueil"] = "Home", ["Workflows"] = "Workflows",
                ["Événements"] = "Events", ["Actions"] = "Actions", ["Intégrations"] = "Integrations",
                ["Historique"] = "History", ["Variables"] = "Variables", ["Pages"] = "Pages", ["Paramètres"] = "Settings",
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
                ["My Live Automation"] = "Mon Live Automatisation", ["Home"] = "Accueil", ["Events"] = "Événements", ["History"] = "Historique",
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
        translations["＋  Système"] = _english ? "＋  System" : "＋  Système";
        translations["＋  System"] = _english ? "＋  System" : "＋  Système";

        ToolTip.SetTip(AddCanvasSystemButton, _english ? "Add an internal function to the canvas" : "Ajouter une fonction interne au canevas");

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
            if (picker.Tag is string outputTag && outputTag.StartsWith("obsOperations:", StringComparison.Ordinal))
            {
                var index = picker.SelectedIndex;
                var values = outputTag.EndsWith("record", StringComparison.Ordinal) ? new[] { "start", "stop", "pause", "resume" } : outputTag.EndsWith("replay", StringComparison.Ordinal) ? new[] { "start", "stop", "save" } : new[] { "start", "stop" };
                picker.ItemsSource = ObsOperationCaptions(values, english); picker.SelectedIndex = index;
            }
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
            if (Equals(picker.Tag, "variableTypes"))
            {
                var selected = picker.SelectedIndex;
                picker.ItemsSource = english ? new[] { "Number", "Text" } : new[] { "Nombre", "Texte" };
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
            if (Equals(button.Tag, "deleteEventButton")) ToolTip.SetTip(button, english ? "Delete event" : "Supprimer l’événement");
            if (Equals(button.Tag, "manualRunButton"))
            {
                button.Content = english ? "▶  Run once" : "▶  Exécuter";
                ToolTip.SetTip(button, english ? "Execute the published manual branch with real effects" : "Exécuter la branche manuelle publiée avec des effets réels");
                continue;
            }
            if (button.Content is string text)
            {
                button.Content = TranslateText(text, translations);
            }
        }
        foreach (var node in _dynamicNodes)
            if (node.Tag is string key && node.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault() is { } header)
            {
                var actionTitle = LocalizedActionTitle(key);
                header.Text = $"◆  {ActionCategoryLabel(key)} · {(_english ? actionTitle.English : actionTitle.French)}";
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
