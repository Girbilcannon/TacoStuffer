using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TacoStuffer.Models;
using TacoStuffer.Services;

namespace TacoStuffer.Views;

public sealed record OverlayAsset(string DocumentName, string Key, string Label, string Kind, string Category, string MapId);

public sealed record OverlayCategory(string DocumentName, string Path, bool IsSeparator = false, bool HasCopy = false)
{
    public string DisplayName { get; init; } = "";
    public string TipDescription { get; init; } = "";
}

public sealed class OverlayWindow : PreviewDialog
{
    public event Action? ToolbarRequested;
    public event Action? ToolbarStateChanged;
    public void SetCheckpointTest(bool active) => ToolbarStateChanged?.Invoke();
    public void SetInteractionTests(bool enabled)
    {
        InteractionTestsEnabled = enabled;
        InteractionTestModeChanged?.Invoke(enabled);
        ToolbarStateChanged?.Invoke();
    }
    public IReadOnlyDictionary<string, bool> ToolbarButtons() => new Dictionary<string, bool>
    {
        ["start"] = startTrail.IsEnabled && !resumable,
        ["pause"] = pauseTrail.IsEnabled && !paused,
        ["continue"] = (pauseTrail.IsEnabled && paused) || (startTrail.IsEnabled && resumable),
        ["segment"] = newSegment.IsEnabled,
        ["stop"] = stopTrail.IsEnabled,
        ["marker"] = placeMarker.IsEnabled,
        ["reposition"] = reposition.IsEnabled
    };
    public void InvokeToolbarAction(string command)
    {
        if (!ToolbarButtons().GetValueOrDefault(command)) return;
        var button = command switch
        {
            "start" => startTrail,
            "pause" => pauseTrail,
            "continue" => paused ? pauseTrail : startTrail,
            "segment" => newSegment,
            "stop" => stopTrail,
            "marker" => placeMarker,
            "reposition" => reposition,
            _ => null
        };
        button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private readonly TextBlock telemetry = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGreen, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ComboBox markerCategories = new();
    private readonly ComboBox markerDocuments = new();
    private readonly Button placeMarker = new() { Content = "+ New Marker", IsEnabled = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button reposition = new() { Content = "Reposition Select Marker", IsEnabled = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock recordingStatus = new() { Text = "Choose an XML and category to record into.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox trailCategories = new();
    private readonly ComboBox trailDocuments = new();
    private readonly Button newTrail = new() { Content = "+ New Trail", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button startTrail = new() { Content = "Start Trail", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button pauseTrail = new() { Content = "Pause", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button newSegment = new() { Content = "Start New Segment", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button stopTrail = new() { Content = "Stop", Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock trailStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Slider trailSmoothness = new() { Minimum = 1, Maximum = 10, Value = 8, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1, Height = 24 };
    private readonly TextBlock smoothnessLabel = new() { Margin = new Thickness(0, 0, 0, 4) };
    private List<EditableTrail> editable = [];
    private readonly HashSet<TrailPointRef> selectedPoints = [];
    private readonly HashSet<TrailPointRef> paintedPoints = [];
    private readonly Dictionary<TrailPointRef, TextBlock> pointLabels = [];
    private static readonly Brush PointSelectionBrush = SelectionBrush();
    private static Brush SelectionBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(52, 86, 77));
        brush.Freeze();
        return brush;
    }

    private TrailPointRef? rangeAnchor;
    private readonly Dictionary<TrailSegmentRef, TreeViewItem> segmentNodes = [];
    private readonly Dictionary<TrailSegmentRef, Action> refreshSegments = [];
    private long? activeTrail;
    private bool recording, paused, resumable, sameMap = true;
    private bool updatingSmoothness, editingSmoothness;
    private readonly TreeView assetsTree = new();
    private readonly Dictionary<string, TreeViewItem> assetNodes = [];
    private readonly Dictionary<string, TreeViewItem> folderNodes = [];
    private Dictionary<string, OverlayAsset> assetIndex = [];
    private readonly Dictionary<string, TreeViewItem> documentNodes = [];
    private TreeViewItem? EnsureAssetNode(string xml, string key)
    {
        if (!assetIndex.TryGetValue(xml + "|" + key, out var asset) || !documentNodes.TryGetValue(xml, out var doc)) return null;
        doc.IsExpanded = true;
        string path = "";
        foreach (string part in asset.Category.Length == 0 ? new[] { "" } : asset.Category.Split('.'))
        {
            path = path.Length == 0 ? part : path + "." + part;
            if (!folderNodes.TryGetValue(xml + "|" + path, out var folder)) return null;
            folder.IsExpanded = true;
        }
        return assetNodes.GetValueOrDefault(xml + "|" + key);
    }

    private void EnsureTrailNode(long number)
    {
        var trail = editable.FirstOrDefault(t => t.Number == number);
        if (trail == null) return;
        var asset = assets.FirstOrDefault(a => a.Key == "Trail:" + trail.Guid);
        if (asset != null && EnsureAssetNode(asset.DocumentName, asset.Key) is { } node) node.IsExpanded = true;
    }

    private readonly TextBlock empty = new() { Text = "No markers in this project yet.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), Foreground = Brushes.Gray };
    private readonly TabControl tabs = new();
    private List<string> categories = [];
    private List<OverlayCategory> targets = [];
    private bool canRecord;
    private bool updatingTargets;
    private List<OverlayAsset> assets = [];
    private GameClientWindow client;
    private bool freePositionInitialized;
    private bool compact;
    private bool minimizeGlowEnabled = true;
    private SolidColorBrush? restoreBrush;
    private bool hostedMode;
    private bool presentationAllowed;
    public bool IsHostedMinimized => hostedMode && compact;
    // Panel visibility and overlay-session activity are separate in Play in App.
    public bool IsRenderingActive => presentationAllowed && (IsVisible || IsHostedMinimized);
    public event Action? PresentationChanged;
    public void RestoreControls() => SetCompact(false);
    private object? expandedContent;
    private double expandedWidth;
    private double expandedHeight;
    private NativeGameWindow.Rect expandedBounds;
    private bool rebuildingAssets;
    public int BoundProcessId { get; }
    public nint Handle => new WindowInteropHelper(this).Handle;
    public event Action<OverlayAsset>? AssetSelected;
    public event Action<string, string>? MarkerRequested;
    public event Action<OverlayAsset>? RepositionRequested;
    public event Action<string, string>? CategoryRequested;
    public event Action<string, string>? NewTrailRequested;
    public event Action? StartTrailRequested, PauseTrailRequested, NewSegmentRequested, StopTrailRequested;
    public event Action<long, long?>? TrailChosen;
    public event Action<TrailSegmentRef>? SegmentSelected;
    public event Action<IReadOnlyList<TrailPointRef>>? PointsSelected;
    public event Action<IReadOnlyList<TrailPointRef>>? DeletePointsRequested;
    public event Action<TrailSegmentRef>? DeleteSegmentRequested;
    public event Action<OverlayAsset>? DeleteAssetRequested;
    public event Action<OverlayAsset>? RenameAssetRequested;
    public event Action<bool>? InteractionTestModeChanged;
    public event Action? MarkerInteractionRequested;
    public event Action<string, string>? CopyCategoryRequested;
    public bool InteractionTestsEnabled { get; private set; }
    public event Action<int>? TrailSmoothnessChanged;
    public void InvokeShortcut(string action)
    {
        void Click(Button button)
        {
            if (button.IsEnabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        switch (action)
        {
            case "interact": MarkerInteractionRequested?.Invoke(); break;
            case "category":
                string? xml = (tabs.SelectedIndex == 1 ? trailDocuments : markerDocuments).SelectedItem as string;
                string? path = (tabs.SelectedIndex == 1 ? trailCategories : markerCategories).SelectedItem as string;
                if (xml != null && path != null) CategoryRequested?.Invoke(xml, path);
                break;
            case "marker":
                Click(placeMarker);
                break;
            case "reposition":
                Click(reposition);
                break;
            case "trail":
                Click(newTrail);
                break;
            case "start":
                if (!recording) Click(startTrail);
                break;
            case "pause":
                if (recording && !paused) Click(pauseTrail);
                break;
            case "continue":
                if (recording && paused) Click(pauseTrail);
                break;
            case "segment":
                Click(newSegment);
                break;
            case "stop":
                Click(stopTrail);
                break;
        }
    }

    public OverlayWindow(int processId) : base("Taco Stuffer · Game Overlay", 550)
    {
        BoundProcessId = processId;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && !e.IsRepeat && Keyboard.FocusedElement is not TextBox && HandleDelete()) e.Handled = true;
        };
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Body.Margin = new Thickness(12);
        SizeToContent = SizeToContent.Manual;
        Width = 750; Height = 720;
        MinWidth = 550; MinHeight = 420;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0)
        });
        Body.Children.Add(telemetry);
        var toolbarButton = new Button { Content = "Open Toolbar", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        toolbarButton.Click += (_, _) => ToolbarRequested?.Invoke();
        Body.Children.Add(toolbarButton);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
        var divider = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(divider, 1); grid.Children.Add(divider);
        ScrollViewer.SetHorizontalScrollBarVisibility(assetsTree, ScrollBarVisibility.Auto);
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var markers = new StackPanel { Margin = new Thickness(12) };
        Caption(markers, "XML file");
        markers.Children.Add(markerDocuments);
        Caption(markers, "Place into category");
        markers.Children.Add(markerCategories);
        markers.Children.Add(placeMarker);
        markers.Children.Add(reposition);
        reposition.Click += (_, _) =>
        {
            if (assetsTree.SelectedItem is TreeViewItem { Tag: OverlayAsset a }) RepositionRequested?.Invoke(a);
        };
        markers.Children.Add(recordingStatus);
        void Template(ComboBox box, ComboBox documents)
        {
            var template = new DataTemplate();
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding
            {
                Converter = new CategoryTextConverter(path =>
            {
                var target = targets.FirstOrDefault(t => t.DocumentName == documents.SelectedItem as string && t.Path == path);
                string label = !string.IsNullOrWhiteSpace(target?.DisplayName) ? target.DisplayName : path[(path.LastIndexOf('.') + 1)..];
                return new string(' ', path.Count(c => c == '.') * 3) + label;
            })
            });
            text.SetBinding(TextBlock.ToolTipProperty, new System.Windows.Data.Binding
            {
                Converter = new CategoryTextConverter(path =>
                path + "\n" + (targets.FirstOrDefault(t => t.DocumentName == documents.SelectedItem as string && t.Path == path)?.TipDescription ?? ""))
            });
            template.VisualTree = text; box.ItemTemplate = template;
        }
        Template(markerCategories, markerDocuments); Template(trailCategories, trailDocuments);
        markerDocuments.SelectionChanged += (_, _) =>
        {
            if (!updatingTargets) UpdateCategories();
        };
        markerCategories.SelectionChanged += (_, _) =>
        {
            if (!updatingTargets) UpdateRecordButton();
        };
        placeMarker.Click += (_, _) =>
        {
            if (placeMarker.IsEnabled && markerDocuments.SelectedItem is string xml && markerCategories.SelectedItem is string path) MarkerRequested?.Invoke(xml, path);
        };
        tabs.Items.Add(new TabItem { Header = "Markers", Content = new ScrollViewer { Content = markers, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var trailPanel = new DockPanel { Margin = new Thickness(12) };
        var smoothnessPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        smoothnessLabel.Foreground = (Brush)FindResource("AccentBrush");
        smoothnessPanel.Children.Add(smoothnessLabel);
        smoothnessPanel.Children.Add(trailSmoothness);
        DockPanel.SetDock(smoothnessPanel, Dock.Bottom);
        trailPanel.Children.Add(smoothnessPanel);
        var trails = new StackPanel();
        trailPanel.Children.Add(new ScrollViewer { Content = trails, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        trailSmoothness.ValueChanged += (_, _) =>
        {
            UpdateSmoothnessLabel();
            if (!updatingSmoothness && !trailSmoothness.IsMouseCaptureWithin) TrailSmoothnessChanged?.Invoke((int)trailSmoothness.Value);
        };
        trailSmoothness.LostMouseCapture += (_, _) =>
        {
            if (!updatingSmoothness && trailSmoothness.IsEnabled) TrailSmoothnessChanged?.Invoke((int)trailSmoothness.Value);
        };
        UpdateSmoothnessLabel();
        Caption(trails, "XML file");
        trails.Children.Add(trailDocuments);
        Caption(trails, "Place into category");
        trails.Children.Add(trailCategories);
        foreach (var button in new[] { newTrail, startTrail, pauseTrail, newSegment, stopTrail }) trails.Children.Add(button);
        trails.Children.Add(trailStatus);
        trailDocuments.SelectionChanged += (_, _) =>
        {
            if (!updatingTargets) UpdateTrailCategories();
        };
        trailCategories.SelectionChanged += (_, _) => UpdateRecordButton();
        newTrail.Click += (_, _) =>
        {
            if (trailDocuments.SelectedItem is string xml && trailCategories.SelectedItem is string path) NewTrailRequested?.Invoke(xml, path);
        };
        startTrail.Click += (_, _) => StartTrailRequested?.Invoke();
        pauseTrail.Click += (_, _) => PauseTrailRequested?.Invoke();
        newSegment.Click += (_, _) => NewSegmentRequested?.Invoke();
        stopTrail.Click += (_, _) => StopTrailRequested?.Invoke();
        tabs.Items.Add(new TabItem { Header = "Trails", Content = trailPanel });
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs) RebuildAssets();
        };
        var targetPanel = new DockPanel();
        var targetHint = new TextBlock
        {
            Text = "Double-clicking a category to the right will update the Target Selection",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(12, 10, 12, 6)
        };
        DockPanel.SetDock(targetHint, Dock.Bottom); targetPanel.Children.Add(targetHint); targetPanel.Children.Add(tabs);
        grid.Children.Add(targetPanel);
        var sidebar = new DockPanel();
        Grid.SetColumn(sidebar, 2);
        var heading = new TextBlock { Text = "RECORDED ASSETS", Foreground = (Brush)FindResource("AccentBrush"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 6, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top);
        sidebar.Children.Add(heading);
        var addCategory = new Button { Content = "+ Category", Margin = new Thickness(6) };
        addCategory.Click += (_, _) =>
        {
            if (assetsTree.SelectedItem is TreeViewItem { Tag: OverlayCategory c } && targets.Any(t => t.DocumentName == c.DocumentName && t.Path == c.Path))
                CategoryRequested?.Invoke(c.DocumentName, c.Path);
            else if ((tabs.SelectedIndex == 1 ? trailDocuments : markerDocuments).SelectedItem is string xml && (tabs.SelectedIndex == 1 ? trailCategories : markerCategories).SelectedItem is string path) CategoryRequested?.Invoke(xml, path);
        };
        DockPanel.SetDock(addCategory, Dock.Bottom);
        sidebar.Children.Add(addCategory);
        DockPanel.SetDock(empty, Dock.Bottom);
        sidebar.Children.Add(empty);
        sidebar.Children.Add(assetsTree);
        grid.Children.Add(sidebar);
        // Let the content fill the actual client area instead of subtracting a
        // guessed caption/telemetry height, which left a fixed blank bottom strip.
        Body.Children.Clear();
        var workspace = new Grid { Margin = new Thickness(12) };
        workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        workspace.Children.Add(telemetry); Grid.SetRow(toolbarButton, 1); workspace.Children.Add(toolbarButton);
        Grid.SetRow(grid, 2); workspace.Children.Add(grid);
        var shell = (DockPanel)((Border)Content).Child; shell.Children.RemoveAt(1); shell.Children.Add(workspace);
        IsVisibleChanged += (_, _) => UpdateMinimizeGlow();
        Closed += (_, _) => StopMinimizeGlow();

        assetsTree.SelectedItemChanged += (_, e) =>
        {
            UpdateRecordButton();
            if (!rebuildingAssets && e.NewValue is TreeViewItem { Tag: OverlayAsset asset }) AssetSelected?.Invoke(asset);
        };
        // TreeViewItem handles some mouse events itself. Catch preview input,
        // including handled events, and resolve the clicked row rather than the
        // previously selected row (which can belong to another XML).
        assetsTree.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(SelectClickedCategory), true);
        var minimize = new Button { Content = "—", ToolTip = "Minimize overlay controls", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(5) };
        minimize.Click += (_, _) => SetCompact(true);
        DockPanel.SetDock(minimize, Dock.Right);
        CaptionBar.Children.Insert(0, minimize);
    }

    private string CategoryLabel(string xml, string path)
    {
        string? label = targets.FirstOrDefault(t => t.DocumentName == xml && t.Path == path)?.DisplayName;
        return string.IsNullOrWhiteSpace(label) ? path[(path.LastIndexOf('.') + 1)..] : label;
    }
    private static void Caption(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });

    private static void Placeholder(Panel panel, string text) => panel.Children.Add(new Button { Content = text, IsEnabled = false, ToolTip = "Recording is not implemented yet", Margin = new Thickness(0, 8, 6, 0) });

    public void SetWorkspace(List<string> documents, List<OverlayCategory> categoryTargets, List<OverlayAsset> recorded)
    {
        assetIndex = recorded.GroupBy(a => a.DocumentName + "|" + a.Key).ToDictionary(g => g.Key, g => g.First());
        targets = categoryTargets;
        categories = targets.Where(t => !t.IsSeparator).Select(t => t.Path).Distinct().ToList();
        assets = recorded;
        updatingTargets = true;
        try
        {
            SetChoices(markerDocuments, documents);
            SetChoices(trailDocuments, documents);
            UpdateCategories();
            UpdateTrailCategories();
        }
        finally
        {
            updatingTargets = false;
            UpdateRecordButton();
        }
        RebuildAssets();
    }

    private void UpdateCategories() => SetChoices(markerCategories, targets.Where(t => !t.IsSeparator && t.DocumentName == (markerDocuments.SelectedItem as string)).Select(t => t.Path).ToList());

    public void SetEditableTrails(List<EditableTrail> trails) => editable = trails;

    private void UpdateTrailCategories() => SetChoices(trailCategories, targets.Where(t => !t.IsSeparator && t.DocumentName == (trailDocuments.SelectedItem as string)).Select(t => t.Path).ToList());

    public void SetTrailState(long? trail, long? segment, bool hasPoints, bool running, bool isPaused, bool onMap)
    {
        if (activeTrail == trail && recording == running && paused == isPaused && resumable == hasPoints && sameMap == onMap) return;
        activeTrail = trail;
        recording = running;
        paused = isPaused;
        resumable = hasPoints;
        sameMap = onMap;
        UpdateRecordButton();
    }

    public void SetTrailSmoothness(int level) => SetTrailSmoothnessContext(level, false);

    public void SetTrailSmoothnessContext(int? level, bool editing)
    {
        // A point/trail selection disables the control without replacing the
        // last displayed segment value with an unrelated recording default.
        if (trailSmoothness.IsMouseCaptureWithin) return;
        updatingSmoothness = true;
        try
        {
            editingSmoothness = editing;
            trailSmoothness.IsEnabled = level.HasValue;
            trailSmoothness.Opacity = level.HasValue ? 1 : .4;
            if (level.HasValue) trailSmoothness.Value = Math.Clamp(level.Value, 1, 10);
            UpdateSmoothnessLabel();
        }
        finally
        {
            updatingSmoothness = false;
        }
    }

    private void UpdateSmoothnessLabel()
    {
        int level = (int)trailSmoothness.Value, interval = 1 << (10 - level);
        smoothnessLabel.Text = trailSmoothness.IsEnabled ? $"{(editingSmoothness ? "Segment" : "Recording")} smoothness · {level}/10" : "Smoothness · select a segment";
        trailSmoothness.ToolTip = editingSmoothness ? "Adjust the selected segment. Lower levels retain fewer source points; higher levels interpolate. Endpoints and disconnected boundaries remain intact." : interval == 1 ? "Full recording density. Every changed position is recorded." : $"Records 1 in {interval} changed positions. Lower levels halve recording density per step. Pause/stop retains the last observed endpoint.";
    }

    public void TrailResult(string message) => trailStatus.Text = message;

    private void UpdateRecordButton()
    {
        placeMarker.IsEnabled = canRecord && markerDocuments.SelectedItem is string xml && markerCategories.SelectedItem is string path && targets.Any(t => !t.IsSeparator && t.DocumentName == xml && t.Path == path);
        reposition.IsEnabled = canRecord && assetsTree.SelectedItem is TreeViewItem { Tag: OverlayAsset { Kind: "POI" } };
        markerCategories.ToolTip = markerCategories.SelectedItem;
        trailCategories.ToolTip = trailCategories.SelectedItem;
        newTrail.IsEnabled = canRecord && trailDocuments.SelectedItem is string tx && trailCategories.SelectedItem is string tp && targets.Any(t => !t.IsSeparator && t.DocumentName == tx && t.Path == tp);
        startTrail.IsEnabled = canRecord && activeTrail != null && !recording && sameMap;
        startTrail.Content = resumable ? "Continue Segment" : "Start Trail";
        pauseTrail.IsEnabled = canRecord && recording && sameMap;
        pauseTrail.Content = paused ? "Continue Trail" : "Pause";
        newSegment.IsEnabled = canRecord && recording && paused && sameMap;
        stopTrail.IsEnabled = recording;
        ToolbarStateChanged?.Invoke();
    }

    public void SetTarget(string xml, string? path)
    {
        if (!markerDocuments.Items.Contains(xml)) return;
        markerDocuments.SelectedItem = xml;
        trailDocuments.SelectedItem = xml;
        UpdateCategories();
        UpdateTrailCategories();
        if (path != null)
        {
            markerCategories.SelectedItem = markerCategories.Items.Contains(path) ? path : null;
            trailCategories.SelectedItem = trailCategories.Items.Contains(path) ? path : null;
        }
        UpdateRecordButton();
    }

    private void SelectClickedCategory(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source != null && source != assetsTree)
        {
            if (source is System.Windows.Controls.Primitives.ToggleButton) return;
            if (source is TreeViewItem node)
            {
                if (node.Tag is OverlayAsset asset)
                {
                    if (e.ClickCount == 2)
                    {
                        e.Handled = true;
                        RenameAssetRequested?.Invoke(asset);
                        return;
                    }
                    // Selection must work while GW2 retains keyboard focus.
                    // Do not depend on TreeViewItem acquiring keyboard focus.
                    bool prior = rebuildingAssets;
                    rebuildingAssets = true;
                    try
                    {
                        node.IsSelected = true;
                    }
                    finally
                    {
                        rebuildingAssets = prior;
                    }
                    selectedPoints.Clear();
                    rangeAnchor = null;
                    if (asset.Kind == "Trail" && editable.FirstOrDefault(t => "Trail:" + t.Guid == asset.Key) is { } chosen) TrailChosen?.Invoke(chosen.Number, null);
                    UpdateRecordButton();
                    AssetSelected?.Invoke(asset);
                    e.Handled = true;
                }
                else if (node.Tag is TrailPointRef point)
                {
                    SelectPointRange(point);
                    e.Handled = true;
                }
                else if (node.Tag is TrailSegmentRef segment)
                {
                    selectedPoints.Clear();
                    rangeAnchor = null;
                    TrailChosen?.Invoke(segment.Trail, segment.Segment);
                    SegmentSelected?.Invoke(segment);
                    UpdatePointHeaders();
                    e.Handled = true;
                }
                else if (InteractionTestsEnabled && node.Tag is OverlayCategory action
                    && targets.Any(t => t.DocumentName == action.DocumentName && t.Path == action.Path && t.IsSeparator && t.HasCopy))
                {
                    CopyCategoryRequested?.Invoke(action.DocumentName, action.Path);
                    e.Handled = true;
                }
                else if (e.ClickCount == 2 && node.Tag is OverlayCategory c)
                {
                    if (targets.Any(t => t.DocumentName == c.DocumentName && t.Path == c.Path && !t.IsSeparator))
                    {
                        SetTarget(c.DocumentName, c.Path);
                        RecordingResult("Recording target: " + c.DocumentName + " / " + c.Path);
                    }
                    else RecordingResult("Separator headings cannot be recording targets. Choose a category beneath this heading.");
                    e.Handled = true;
                }
                return;
            }
            source = source is System.Windows.ContentElement content
                ? System.Windows.ContentOperations.GetParent(content) ?? (content as System.Windows.FrameworkContentElement)?.Parent
                : VisualTreeHelper.GetParent(source);
        }
    }

    private void SelectPointRange(TrailPointRef point)
    {
        var segment = editable.First(t => t.Number == point.Trail).Segments.First(s => s.Id == point.Segment);
        if (NativeGameWindow.ShiftDown && rangeAnchor is { } anchor && anchor.Trail == point.Trail && anchor.Segment == point.Segment)
        {
            int first = segment.Points.FindIndex(p => p.Id == anchor.Point), last = segment.Points.FindIndex(p => p.Id == point.Point);
            if (first >= 0 && last >= 0)
            {
                selectedPoints.Clear();
                foreach (var p in segment.Points.Skip(Math.Min(first, last)).Take(Math.Abs(last - first) + 1)) selectedPoints.Add(new(point.Trail, point.Segment, p.Id));
            }
        }
        else
        {
            if (!NativeGameWindow.ControlDown || selectedPoints.Any(p => p.Trail != point.Trail)) selectedPoints.Clear();
            if (!selectedPoints.Add(point)) selectedPoints.Remove(point);
            rangeAnchor = point;
        }
        TrailChosen?.Invoke(point.Trail, point.Segment);
        PointsSelected?.Invoke(selectedPoints.ToList());
        UpdatePointHeaders();
    }

    public void SetPointSelection(IReadOnlyList<TrailPointRef> points)
    {
        bool changed = !selectedPoints.SetEquals(points);
        selectedPoints.Clear();
        foreach (var p in points) selectedPoints.Add(p);
        if (points.Count > 0)
        {
            tabs.SelectedIndex = 1;
            if (rangeAnchor == null || !selectedPoints.Contains(rangeAnchor)) rangeAnchor = points[0];
            if (changed)
            {
                EnsureTrailNode(points[0].Trail);
                foreach (var p in points) if (segmentNodes.TryGetValue(new(p.Trail, p.Segment), out var branch)) branch.IsExpanded = true;
                TreeViewItem? Reveal(ItemCollection items)
                {
                    foreach (TreeViewItem item in items)
                    {
                        if (item.Tag is TrailPointRef p && p == points[0]) return item;
                        if (Reveal(item.Items) is { } found)
                        {
                            item.IsExpanded = true;
                            return found;
                        }
                    }
                    return null;
                }
                Reveal(assetsTree.Items)?.BringIntoView();
            }
        }
        UpdatePointHeaders();
    }

    private void UpdatePointHeaders()
    {
        var changed = new HashSet<TrailPointRef>(paintedPoints);
        changed.SymmetricExceptWith(selectedPoints);
        foreach (var point in changed)
            if (pointLabels.TryGetValue(point, out var label))
            {
                bool selected = selectedPoints.Contains(point);
                label.Background = selected ? PointSelectionBrush : Brushes.Transparent;
                label.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
            }
        paintedPoints.Clear();
        paintedPoints.UnionWith(selectedPoints);
    }

    public void SelectSegment(TrailSegmentRef reference)
    {
        tabs.SelectedIndex = 1;
        EnsureTrailNode(reference.Trail);
        if (!segmentNodes.TryGetValue(reference, out var node)) return;
        bool prior = rebuildingAssets;
        rebuildingAssets = true;
        try
        {
            node.IsSelected = true;
            node.BringIntoView();
        }
        finally
        {
            rebuildingAssets = prior;
        }
    }

    public void RecordingResult(string message) => recordingStatus.Text = message;

    public void ClearAssetSelection()
    {
        selectedPoints.Clear();
        UpdatePointHeaders();
        bool prior = rebuildingAssets;
        rebuildingAssets = true;
        try
        {
            if (assetsTree.SelectedItem is TreeViewItem { Tag: OverlayAsset } node) node.IsSelected = false;
        }
        finally
        {
            rebuildingAssets = prior;
            UpdateRecordButton();
        }
    }

    public void SelectAsset(string xml, string key)
    {
        if (selectedPoints.Count == 0 && assetsTree.SelectedItem is TreeViewItem { Tag: OverlayAsset current } && current.DocumentName == xml && current.Key == key) return;
        selectedPoints.Clear();
        rangeAnchor = null;
        UpdatePointHeaders();
        bool prior = rebuildingAssets;
        rebuildingAssets = true;
        try
        {
            var target = assetIndex.GetValueOrDefault(xml + "|" + key);
            if (target != null) tabs.SelectedIndex = target.Kind == "Trail" ? 1 : 0;
            if (EnsureAssetNode(xml, key) is { } node)
            {
                node.IsSelected = true;
                node.BringIntoView();
            }

        }
        finally
        {
            rebuildingAssets = prior;
            UpdateRecordButton();
        }
    }

    private static void SetChoices(ComboBox box, List<string> paths)
    {
        string? selected = box.SelectedItem as string;
        box.ItemsSource = paths;
        box.SelectedItem = selected != null && paths.Contains(selected) ? selected : paths.FirstOrDefault();
    }

    // Labels can change during a rename; expansion follows document/object identity.
    private static string? ExpansionKey(object? tag) => tag switch
    {
        OverlayAsset asset => "asset|" + asset.DocumentName + "|" + asset.Key,
        OverlayCategory category => "category|" + category.DocumentName + "|" + category.Path,
        TrailSegmentRef segment => "segment|" + segment.Trail + "|" + segment.Segment,
        string document => document,
        _ => null
    };

    private void RebuildAssets()
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("Overlay.BuildBranches");
        bool prior = rebuildingAssets;
        rebuildingAssets = true;
        try
        {
            string kind = tabs.SelectedIndex == 1 ? "Trail" : "POI";
            string? selected = (assetsTree.SelectedItem as TreeViewItem)?.Tag is OverlayAsset a ? a.DocumentName + "|" + a.Key : null;
            var selectedSegment = (assetsTree.SelectedItem as TreeViewItem)?.Tag as TrailSegmentRef;
            var expanded = new HashSet<string>();
            void Expansion(ItemCollection items)
            {
                foreach (TreeViewItem n in items)
                {
                    if (n.IsExpanded && ExpansionKey(n.Tag) is { } key) expanded.Add(key);
                    Expansion(n.Items);
                }
            }
            Expansion(assetsTree.Items);
            assetsTree.Items.Clear();
            segmentNodes.Clear();
            refreshSegments.Clear();
            pointLabels.Clear();
            paintedPoints.Clear();
            assetNodes.Clear();
            folderNodes.Clear();
            documentNodes.Clear();
            var matching = assets.Where(a => a.Kind == kind).ToList();
            var groupedAssets = matching.ToLookup(a => a.DocumentName + "|" + a.Category);
            var trailIndex = editable.GroupBy(t => "Trail:" + t.Guid).ToDictionary(g => g.Key, g => g.First());
            var paths = new List<(string Xml, string Path)>();
            var seenPaths = new HashSet<(string Xml, string Path)>();
            foreach (var pair in targets.Select(t => (t.DocumentName, t.Path)).Concat(matching.Select(a => (a.DocumentName, a.Category))))
            {
                string path = pair.Item2;
                while (true)
                {
                    if (seenPaths.Add((pair.Item1, path))) paths.Add((pair.Item1, path));
                    int dot = path.LastIndexOf('.');
                    if (dot < 0) break;
                    path = path[..dot];
                }
            }
            var children = paths.ToLookup(p => p.Xml + "|" + (p.Path.Contains('.') ? p.Path[..p.Path.LastIndexOf('.')] : "@root"));
            void Deferred(TreeViewItem node, bool hasChildren, Action populate)
            {
                if (!hasChildren) return;
                bool populated = false;
                node.Items.Add(new TreeViewItem { Header = "" });
                void Load()
                {
                    if (populated) return;
                    populated = true;
                    node.Items.Clear();
                    populate();
                }
                node.Expanded += (_, e) =>
                {
                    if (e.OriginalSource == node) Load();
                };
                if (ExpansionKey(node.Tag) is { } key && expanded.Contains(key))
                {
                    node.IsExpanded = true;
                    Load();
                }
            }
            TreeViewItem Folder(string xml, string path)
            {
                string key = xml + "|" + path;
                var node = new TreeViewItem { Header = path.Length == 0 ? "Unassigned" : CategoryLabel(xml, path), IsExpanded = false, Padding = new Thickness(3, 2, 3, 2), ToolTip = xml + " · " + path, Tag = new OverlayCategory(xml, path) };
                if (targets.Any(t => t.DocumentName == xml && t.Path == path && t.HasCopy))
                {
                    var categoryMenu = new ContextMenu();
                    var copyCategory = new MenuItem { Header = "Test Copy (category action)" };
                    copyCategory.Click += (_, _) => CopyCategoryRequested?.Invoke(xml, path);
                    categoryMenu.Items.Add(copyCategory);
                    node.ContextMenu = categoryMenu;
                }
                folderNodes[key] = node;
                Deferred(node, children[key].Any() || groupedAssets[key].Any(), () =>
                {
                    foreach (var child in children[key]) node.Items.Add(Folder(xml, child.Path));
                    foreach (var asset in groupedAssets[key]) node.Items.Add(AssetNode(asset));
                });
                return node;
            }
            TreeViewItem AssetNode(OverlayAsset asset)
            {
                var node = new TreeViewItem { Header = (asset.Kind == "POI" ? "◆  " : "↝  ") + asset.Label, IsExpanded = false, Foreground = (Brush)FindResource("AccentBrush"), Tag = asset, Padding = new Thickness(3, 2, 3, 2), ToolTip = asset.DocumentName + " · map " + asset.MapId + "\n" + asset.Category };
                assetNodes[asset.DocumentName + "|" + asset.Key] = node;
                var menu = new ContextMenu();
                var rename = new MenuItem { Header = "Rename " + (asset.Kind == "POI" ? "marker" : "trail") + "…" };
                rename.Click += (_, _) => RenameAssetRequested?.Invoke(asset);
                menu.Items.Add(rename);
                var remove = new MenuItem { Header = "Delete " + (asset.Kind == "POI" ? "marker" : "trail") + "…" };
                remove.Click += (_, _) => DeleteAssetRequested?.Invoke(asset);
                menu.Items.Add(remove);
                node.ContextMenu = menu;
                if (asset.Kind == "Trail" && trailIndex.GetValueOrDefault(asset.Key) is { } trail)
                {
                    foreach (var segment in trail.Segments)
                    {
                        var tag = new TrailSegmentRef(trail.Number, segment.Id);
                        var branch = new TreeViewItem { Header = $"Segment {segment.Id:00000} · {segment.Points.Count} points", Tag = tag, IsExpanded = expanded.Contains(ExpansionKey(tag)!), Foreground = (Brush)FindResource("AccentBrush") };
                        node.Items.Add(branch);
                        segmentNodes[tag] = branch;
                        bool populated = false;
                        refreshSegments[tag] = () =>
                        {
                            foreach (TreeViewItem item in branch.Items) if (item.Tag is TrailPointRef point) pointLabels.Remove(point);
                            branch.Items.Clear();
                            populated = false;
                            branch.Header = $"Segment {segment.Id:00000} · {segment.Points.Count} points";
                            if (branch.IsExpanded) Populate();
                            else if (segment.Points.Count > 0) branch.Items.Add(new TreeViewItem { Header = "" });
                        };
                        void Populate()
                        {
                            if (populated) return;
                            populated = true;
                            branch.Items.Clear();
                            foreach (var point in segment.Points) branch.Items.Add(PointNode(new TrailPointRef(trail.Number, segment.Id, point.Id)));
                        }
                        if (branch.IsExpanded) Populate();
                        else if (segment.Points.Count > 0) branch.Items.Add(new TreeViewItem { Header = "" });
                        branch.Expanded += (_, e) =>
                        {
                            if (e.OriginalSource == branch)
                            {
                                Populate();
                                UpdatePointHeaders();
                            }
                        };
                        branch.Collapsed += (_, e) =>
                        {
                            if (e.OriginalSource != branch) return;
                            populated = false;
                            foreach (var point in segment.Points) pointLabels.Remove(new(trail.Number, segment.Id, point.Id));
                            branch.Items.Clear();
                            if (segment.Points.Count > 0) branch.Items.Add(new TreeViewItem { Header = "" });
                        };
                        if (tag == selectedSegment) branch.IsSelected = true;
                    }
                }
                // Rebuilding after a point move must restore the trail as well as its segments.
                node.IsExpanded = expanded.Contains(ExpansionKey(asset)!);
                if (asset.DocumentName + "|" + asset.Key == selected) node.IsSelected = true;
                return node;
            }
            foreach (string xml in paths.Select(p => p.Xml).Distinct())
            {
                var doc = new TreeViewItem { Header = xml, Tag = "document|" + xml, IsExpanded = false };
                documentNodes[xml] = doc;
                assetsTree.Items.Add(doc);
                Deferred(doc, children[xml + "|@root"].Any(), () =>
                {
                    foreach (var root in children[xml + "|@root"]) doc.Items.Add(Folder(xml, root.Path));
                });
            }
            empty.Text = matching.Count == 0 ? "No " + (kind == "Trail" ? "trails" : "markers") + " in this project yet." : matching.Count + " " + (kind == "Trail" ? "trails" : "markers") + " · all project maps";
        }
        finally
        {
            rebuildingAssets = prior;
            UpdateRecordButton();
            UpdatePointHeaders();
        }
    }

    private TreeViewItem PointNode(TrailPointRef key)
    {
        bool selected = selectedPoints.Contains(key);
        var label = new TextBlock { Text = $"◆ Point {key.Point:0000000}", Foreground = (Brush)FindResource("AccentBrush"), Background = selected ? PointSelectionBrush : Brushes.Transparent, FontWeight = selected ? FontWeights.Bold : FontWeights.Normal };
        pointLabels[key] = label;
        var item = new TreeViewItem { Header = label, Tag = key };
        var menu = new ContextMenu();
        var delete = new MenuItem { Header = "Delete selected points…" };
        delete.Click += (_, _) =>
        {
            if (!selectedPoints.Contains(key))
            {
                selectedPoints.Clear();
                selectedPoints.Add(key);
            }
            DeletePointsRequested?.Invoke(selectedPoints.ToList());
        };
        menu.Items.Add(delete);
        item.ContextMenu = menu;
        return item;
    }

    public void RefreshSegment(TrailSegmentRef reference)
    {
        if (refreshSegments.TryGetValue(reference, out var refresh)) refresh();
        UpdatePointHeaders();
    }

    public void AppendTrailPoint(long trail, TrailSegment segment, TrailPoint point)
    {
        if (segmentNodes.TryGetValue(new(trail, segment.Id), out var branch))
        {
            branch.Header = $"Segment {segment.Id:00000} · {segment.Points.Count} points";
            if (branch.IsExpanded)
            {
                if (branch.Items.Count < segment.Points.Count) branch.Items.Add(PointNode(new(trail, segment.Id, point.Id)));
            }
            else if (branch.Items.Count == 0) branch.Items.Add(new TreeViewItem { Header = "" });
        }
    }

    public bool HandleDelete()
    {
        if (!IsEnabled) return false;
        if (selectedPoints.Count > 0)
        {
            DeletePointsRequested?.Invoke(selectedPoints.ToList());
            return true;
        }
        if (assetsTree.SelectedItem is TreeViewItem { Tag: TrailSegmentRef segment })
        {
            DeleteSegmentRequested?.Invoke(segment);
            return true;
        }
        if (assetsTree.SelectedItem is TreeViewItem { Tag: OverlayAsset asset })
        {
            DeleteAssetRequested?.Invoke(asset);
            return true;
        }
        return false;
    }

    private void SetCompact(bool value)
    {
        if (compact == value) return;
        compact = value;
        if (value)
        {
            expandedContent = Content;
            expandedWidth = Width;
            expandedHeight = Height;
            MinWidth = MinHeight = 0;
            ResizeMode = ResizeMode.NoResize;
            NativeGameWindow.GetWindowRect(Handle, out expandedBounds);
            var restore = new Button { Margin = new Thickness(0), Content = "TS", ToolTip = "Restore Taco Stuffer controls", Padding = new Thickness(0) };
            restore.Click += (_, _) => SetCompact(false);
            SizeToContent = SizeToContent.Manual;
            Width = 44;
            Height = 36;
            restoreBrush = new SolidColorBrush((restore.Background as SolidColorBrush)?.Color ?? Color.FromRgb(48, 57, 71));
            restore.Background = restoreBrush;
            Content = restore;
        }
        else
        {
            StopMinimizeGlow();
            restoreBrush = null;
            Content = expandedContent;
            expandedContent = null;
            Width = expandedWidth;
            Height = expandedHeight;
            MinWidth = 550; MinHeight = 420;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            SizeToContent = SizeToContent.Manual;
            UpdateLayout();
            NativeGameWindow.SetWindowPos(Handle, 0, expandedBounds.Left, expandedBounds.Top, 0, 0, 0x0015);
        }
        UpdateLayout();
        // Standalone keeps a TS restore button; hosted mode hides only the controls.
        // Recording, rendering and the selected interaction-test mode stay active.
        UpdateVisibility(client, canRecord, null, Environment.ProcessId, NativeGameWindow.HostedGameOverlay);
        UpdateMinimizeGlow();
        PresentationChanged?.Invoke();
    }

    public void SetMinimizeGlow(bool enabled)
    {
        if (minimizeGlowEnabled == enabled) return;
        minimizeGlowEnabled = enabled; UpdateMinimizeGlow();
    }
    private void StopMinimizeGlow()
    {
        restoreBrush?.BeginAnimation(SolidColorBrush.ColorProperty, null);
    }
    private void UpdateMinimizeGlow()
    {
        StopMinimizeGlow();
        if (!compact || hostedMode || !IsVisible || !minimizeGlowEnabled || restoreBrush == null) return;
        // A gentle four-second cycle, without timers or changes to game rendering.
        var glow = new ColorAnimation
        {
            To = Color.FromRgb(255, 225, 55),
            Duration = TimeSpan.FromSeconds(2),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        restoreBrush.BeginAnimation(SolidColorBrush.ColorProperty, glow);
    }

    public void EnsureHandle() => new WindowInteropHelper(this).EnsureHandle();

    public void UpdateVisibility(GameClientWindow game, bool playable, MumbleSnapshot? frame, int editorProcess, bool hosted = false)
    {
        client = game;
        hostedMode = hosted;
        presentationAllowed = false;
        canRecord = playable && game.TryBounds(out _);
        UpdateRecordButton();
        if (frame != null) telemetry.Text = frame.CharacterName + " · Map " + frame.MapId + "\n" + $"Mumble XYZ (Y-up)  {frame.AvatarPosition.X:F2}  {frame.AvatarPosition.Y:F2}  {frame.AvatarPosition.Z:F2}";
        if (!playable || !game.TryBounds(out var bounds) || !NativeGameWindow.IsForeground(game, Handle, editorProcess))
        {
            Topmost = false;
            if (IsVisible) Hide();
            return;
        }
        presentationAllowed = true;
        // A hosted minimize has no desktop TS button. The button bar restores it;
        // IsRenderingActive keeps the world/session gates independent of Hide().
        if (IsHostedMinimized)
        {
            if (IsVisible) Hide();
            return;
        }
        {
            if (!IsVisible) Show(); // ShowActivated=false keeps the game's keyboard focus.
            if (!NativeGameWindow.IsForeground(game, Handle, editorProcess))
            {
                presentationAllowed = false;
                Topmost = false;
                Hide();
                return;
            }
            var transform = HwndSource.FromHwnd(Handle)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            int width = (int)(ActualWidth * transform.M11), height = (int)(ActualHeight * transform.M22);
            if (!NativeGameWindow.GetWindowRect(Handle, out var own)) return;
            int x = own.Left, y = own.Top;
            if (!freePositionInitialized) { x = bounds.Left + 24; y = bounds.Top + 24; }
            if (compact)
            {
                var work = NativeGameWindow.MonitorWorkArea(game.Handle, bounds);
                x = work.Right - width - 8;
                y = work.Top + 8;
            }
            Topmost = NativeGameWindow.OcclusionFor(false) == 0;
            if (!freePositionInitialized || !NativeGameWindow.HasCorrectOverlayBand(Handle, false))
            {
                if (NativeGameWindow.PlaceOverlay(Handle, NativeGameWindow.EditorOcclusionWindow, x, y, width, height, true))
                    freePositionInitialized = true;
            }
            else if (own.Left != x || own.Top != y || own.Right - own.Left != width || own.Bottom - own.Top != height)
                NativeGameWindow.SetWindowPos(Handle, 0, x, y, width, height, 0x0014);
        }
    }
}

internal sealed class CategoryTextConverter(Func<string, string> label) : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => label(value as string ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
