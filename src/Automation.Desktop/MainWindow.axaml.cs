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
    private readonly Stack<string> _undoActions = new();
    private readonly Stack<string> _redoActions = new();
    private Border? _draggedNode;
    private Border? _selectedNode;
    private readonly List<Border> _dynamicNodes = new();
    private readonly List<Avalonia.Controls.Shapes.Path> _dynamicLinks = new();
    private readonly List<(Border From, Border To)> _manualLinks = new();
    private readonly Dictionary<Avalonia.Controls.Shapes.Path, (Border From, Border To)> _linkMap = new();
    private readonly List<string> _workflowNames = new() { "Mon Live Automatisation" };
    private readonly Dictionary<string, List<string>> _workflowActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Point>> _workflowPositions = new(StringComparer.OrdinalIgnoreCase);
    private string _activeWorkflowName = "Mon Live Automatisation";
    private bool _switchingWorkflow;
    private Point _dragPointerStart;
    private double _dragNodeLeft;
    private double _dragNodeTop;

    public MainWindow()
    {
        InitializeComponent();
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
        Opened += OnMainWindowOpened;
    }

    private async void OnMainWindowOpened(object? sender, EventArgs eventArgs)
    {
        try
        {
            HidePrototypeNodes();
            var language = await ConnectionPersistence.GetLanguageAsync();
            ApplyLanguage(language == "English");
            if (await PrototypeServices.HasPrototypeDraftAsync())
            {
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
        _workflowActions[_activeWorkflowName] = _selectedActions.ToList();
        _workflowPositions[_activeWorkflowName] = _dynamicNodes.Where(node => node.Tag is string).ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node)));
        _workflowNames.Add(name);
        _workflowActions[name] = new List<string>();
        _workflowPositions[name] = new Dictionary<string, Point>();
        WorkflowSelector.ItemsSource = _workflowNames.ToArray();
        WorkflowSelector.SelectedIndex = _workflowNames.Count - 1;
        _activeWorkflowName = name;
        ClearWorkflowCanvas();
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
                ClearWorkflowCanvas();
                foreach (var action in _workflowActions.TryGetValue(name, out var actions) ? actions : [])
                {
                    _selectedActions.Add(action);
                    AddActionNode(action);
                }
                if (_workflowPositions.TryGetValue(name, out var positions))
                    foreach (var node in _dynamicNodes)
                        if (node.Tag is string label && positions.TryGetValue(label, out var position)) { Canvas.SetLeft(node, position.X); Canvas.SetTop(node, position.Y); }
                UpdateDynamicLinks();
                _activeWorkflowName = name;
            }
            TestStatus.Text = _english ? $"Editing: {name}" : $"Édition : {name}";
        }
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
            _workflowNames[WorkflowSelector.SelectedIndex] = result;
            var oldName = current;
            if (_workflowActions.TryGetValue(oldName, out var actions))
            {
                _workflowActions.Remove(oldName);
                _workflowActions[result] = actions;
            }
            WorkflowSelector.ItemsSource = _workflowNames.ToArray();
            WorkflowSelector.SelectedItem = result;
            _activeWorkflowName = result;
            TestStatus.Text = _english ? "Automation renamed" : "Automatisation renommée";
        }
    }

    private void OnDeleteWorkflowClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_workflowNames.Count <= 1 || WorkflowSelector.SelectedIndex < 0)
        {
            TestStatus.Text = _english ? "Keep at least one automation" : "Garde au moins une automatisation";
            return;
        }
        var currentWorkflowName = _workflowNames[WorkflowSelector.SelectedIndex];
        _workflowNames.RemoveAt(WorkflowSelector.SelectedIndex);
        _workflowActions.Remove(currentWorkflowName);
        WorkflowSelector.ItemsSource = _workflowNames.ToArray();
        WorkflowSelector.SelectedIndex = 0;
        ClearWorkflowCanvas();
        TestStatus.Text = _english ? "Automation deleted" : "Automatisation supprimée";
    }

    private void ClearWorkflowCanvas()
    {
        _selectedActions.Clear();
        _manualLinks.Clear();
        _dynamicNodes.Clear();
        foreach (var link in _dynamicLinks) WorkflowCanvas.Children.Remove(link);
        _dynamicLinks.Clear();
        foreach (var node in WorkflowCanvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray())
            WorkflowCanvas.Children.Remove(node);
        _addedActionCount = 0;
        UpdateHistoryButtons();
    }

    private async void OnTestClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (!await PrototypeServices.HasPrototypeDraftAsync())
        {
            TestStatus.Text = _english ? "Save the draft before testing." : "Enregistre le brouillon avant de tester.";
            return;
        }

        TestStatus.Text = _english
            ? "Simulation successful: 3 steps would run."
            : "Simulation réussie : les 3 étapes seraient exécutées.";
    }

    private async void OnSaveWorkflowClick(object? sender, RoutedEventArgs eventArgs)
    {
        try
        {
            await PrototypeServices.SavePrototypeDraftAsync(_selectedActions, GetManualLinkLabels());
            await ConnectionPersistence.SaveWorkflowPositionsAsync(_activeWorkflowName, _dynamicNodes.Where(node => node.Tag is string).ToDictionary(node => (string)node.Tag!, node => new Point(Canvas.GetLeft(node), Canvas.GetTop(node))));
            TestStatus.Text = _english ? "Draft saved" : "Brouillon enregistré";
        }
        catch (Exception exception)
        {
            TestStatus.Text = _english ? $"Save failed: {exception.Message}" : $"Échec de sauvegarde : {exception.Message}";
        }
    }

    private async void OnStartWorkflowClick(object? sender, RoutedEventArgs eventArgs)
    {
        var stopping = StartWorkflowButton.Content?.ToString()?.Contains("Stop", StringComparison.Ordinal) == true
            || StartWorkflowButton.Content?.ToString()?.Contains("Arrêter", StringComparison.Ordinal) == true;
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
        }
        TestStatus.Text = stopping
            ? (_english ? "Automation stopped" : "Automatisation arrêtée")
            : (_english ? "Automation published and enabled" : "Automatisation publiée et activée");
        StartWorkflowButton.Content = stopping
            ? (_english ? "▶  Start" : "▶  Démarrer")
            : (_english ? "■  Stop" : "■  Arrêter");
    }

    private void OnLibraryActionPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.Source is Border border && border.Classes.Contains("libraryItem"))
        {
            var label = border.Child is TextBlock textBlock ? textBlock.Text : null;
            if (!string.IsNullOrWhiteSpace(label))
            {
                TestStatus.Text = _english ? $"Action selected: {label}" : $"Action sélectionnée : {label}";
                if (!_selectedActions.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    _selectedActions.Add(label);
                    _undoActions.Push(label);
                    _redoActions.Clear();
                    AddActionNode(label);
                    UpdateHistoryButtons();
                }
                eventArgs.Handled = true;
            }
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_undoActions.Count == 0) return;
        var label = _undoActions.Pop();
        _selectedActions.Remove(label);
        _redoActions.Push(label);
        RebuildActionNodes();
        TestStatus.Text = _english ? "Action removed" : "Action annulée";
        UpdateHistoryButtons();
    }

    private void OnRedoClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_redoActions.Count == 0) return;
        var label = _redoActions.Pop();
        _selectedActions.Add(label);
        _undoActions.Push(label);
        RebuildActionNodes();
        TestStatus.Text = _english ? "Action restored" : "Action rétablie";
        UpdateHistoryButtons();
    }

    private void RebuildActionNodes()
    {
        foreach (var link in _dynamicLinks) WorkflowCanvas.Children.Remove(link);
        _dynamicLinks.Clear();
        _linkMap.Clear();
        _dynamicNodes.Clear();
        _manualLinks.Clear();
        _selectedNode = null;
        foreach (var child in WorkflowCanvas.Children.OfType<Border>().Where(border => border.Classes.Contains("dynamicAction")).ToArray())
            WorkflowCanvas.Children.Remove(child);
        _addedActionCount = 0;
        foreach (var action in _selectedActions) AddActionNode(action);
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

    private void AddActionNode(string label)
    {
        var palette = GetActionPalette(label);
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
                            Text = label.TrimStart('▣', '✎', '◎', '♟', '◉', '▻', '●', '▧', '◇', '◷', '＋', '♫', ' '),
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        }
                    }
                }
            }
        };
        Canvas.SetLeft(node, 70 + (_addedActionCount % 3) * 230);
        Canvas.SetTop(node, 520 + (_addedActionCount / 3) * 90);
        WorkflowCanvas.Children.Add(node);
        _dynamicNodes.Add(node);
        _addedActionCount++;
    }

    private static (string Header, string Border, string Icon, string Text) GetActionPalette(string label)
    {
        var value = label.ToLowerInvariant();
        if (value.Contains("obs") || value.Contains("scène") || value.Contains("scene") || value.Contains("source") || value.Contains("replay") || value.Contains("capture"))
            return ("#20242C", "#737B8A", "#343A4D", "#DCE2EE");
        if (value.Contains("condition") || value.Contains("attendre") || value.Contains("wait") || value.Contains("variable") || value.Contains("compteur"))
            return ("#173C73", "#3788F5", "#275AA0", "#7EB8FF");
        return ("#4A206F", "#A640F4", "#6F35BE", "#D39AFF");
    }

    private void DeleteActionNode(Border node)
    {
        if (node.Tag is not string label) return;
        _selectedActions.Remove(label);
        _undoActions.Push(label);
        _redoActions.Clear();
        RebuildActionNodes();
        TestStatus.Text = _english ? "Action deleted" : "Action supprimée";
        UpdateHistoryButtons();
    }

    private void OnActionNodePressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Border node) return;
        var point = eventArgs.GetCurrentPoint(node);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            if (_selectedNode is not null) _selectedNode.BorderBrush = new SolidColorBrush(Color.Parse("#A640F4"));
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
        _selectedActions.Remove(label);
        _undoActions.Push(label);
        _redoActions.Clear();
        RebuildActionNodes();
        TestStatus.Text = _english ? "Action deleted" : "Action supprimée";
        UpdateHistoryButtons();
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
        foreach (var (from, to) in _manualLinks)
        {
            var fromLeft = Canvas.GetLeft(from);
            var fromTop = Canvas.GetTop(from);
            var toLeft = Canvas.GetLeft(to);
            var toTop = Canvas.GetTop(to);
            var start = new Point(fromLeft + from.Width, fromTop + from.Height / 2);
            var end = new Point(toLeft - 2, toTop + to.Height / 2);
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            var ux = dx / length;
            var uy = dy / length;
            var px = -uy;
            var py = ux;
            var headBase = new Point(end.X - ux * 17, end.Y - uy * 17);
            var wingA = new Point(headBase.X + px * 8, headBase.Y + py * 8);
            var wingB = new Point(headBase.X - px * 8, headBase.Y - py * 8);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false);
                context.LineTo(end);
                context.BeginFigure(wingA, false);
                context.LineTo(end);
                context.LineTo(wingB);
            }
            var link = new Avalonia.Controls.Shapes.Path
            {
                Data = geometry,
                Stroke = from.BorderBrush,
                StrokeThickness = 3,
                StrokeLineCap = PenLineCap.Round,
                ZIndex = 0
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
                    if (_selectedNode is not null) _selectedNode.BorderBrush = new SolidColorBrush(Color.Parse("#A640F4"));
                    _selectedNode = node;
                    node.BorderBrush = new SolidColorBrush(Color.Parse("#FFD166"));
                    TestStatus.Text = _english ? "Source selected — click a destination" : "Source sélectionnée — clique une destination";
                }
                else
                {
                    if (!_manualLinks.Contains((_selectedNode, node))) _manualLinks.Add((_selectedNode, node));
                    _selectedNode.BorderBrush = new SolidColorBrush(Color.Parse("#A640F4"));
                    _selectedNode = null;
                    TestStatus.Text = _english ? "Link created" : "Lien créé";
                    UpdateDynamicLinks();
                }
            }
            _draggedNode = null;
        }
    }

    internal void SetConnectionStatus(string message) => TestStatus.Text = message;

    internal void UpdateIntegrationCount(bool obsConnected, bool twitchConnected)
    {
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
                ["Ajouter une condition"] = "Add a condition", ["Attendre"] = "Wait", ["Modifier une variable"] = "Edit a variable"
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
                ["Add a condition"] = "Ajouter une condition", ["Wait"] = "Attendre", ["Edit a variable"] = "Modifier une variable"
            };

        translations["Brouillon enregistré"] = _english ? "Draft saved" : "Brouillon enregistré";
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
        translations["Afficher une source"] = _english ? "Show a source" : "Afficher une source";
        translations["Masquer une source"] = _english ? "Hide a source" : "Masquer une source";
        translations["Démarrer un replay"] = _english ? "Start replay" : "Démarrer un replay";
        translations["Prendre une capture"] = _english ? "Take a screenshot" : "Prendre une capture";

        foreach (var control in this.GetVisualDescendants().OfType<TextBlock>())
        {
            if (control.Text is { } text)
            {
                control.Text = TranslateText(text, translations);
            }
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
