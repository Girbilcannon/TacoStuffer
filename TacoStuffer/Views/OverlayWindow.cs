using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using TacoStuffer.Models;
using TacoStuffer.Services;

namespace TacoStuffer.Views;

public sealed record OverlayAsset(string DocumentName, string Key, string Label, string Kind, string Category, string MapId);

public sealed record OverlayCategory(string DocumentName, string Path, bool IsSeparator = false);

public sealed class OverlayWindow : PreviewDialog
{
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
    private bool positioning;
    private bool freePosition, freePositionInitialized;
    private bool rebuildingAssets;
    private int offsetX = 24, offsetY = 24;
    private int lastX = int.MinValue, lastY = int.MinValue;
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
    public event Action<int>? TrailSmoothnessChanged;
    public void InvokeShortcut(string action)
    {
        void Click(Button button)
        {
            if (button.IsEnabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        switch (action)
        {
            case "category":
            string? xml = (tabs.SelectedIndex == 1 ? trailDocuments : markerDocuments).SelectedItem as string;
            string? path = (tabs.SelectedIndex == 1 ? trailCategories : markerCategories).SelectedItem as string;
            if (xml != null && path != null) CategoryRequested?.Invoke(xml, path);
            break;
            case "marker": Click(placeMarker);
            break;
            case "reposition": Click(reposition);
            break;
            case "trail": Click(newTrail);
            break;
            case "start": if (!recording) Click(startTrail);
            break;
            case "pause": if (recording && !paused) Click(pauseTrail);
            break;
            case "continue": if (recording && paused) Click(pauseTrail);
            break;
            case "segment": Click(newSegment);
            break;
            case "stop": Click(stopTrail);
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
        Body.Children.Add(telemetry);
        var grid = new Grid { Height = 460 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(245) });
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
        var categoryTemplate = new DataTemplate();
        var categoryText = new FrameworkElementFactory(typeof(TextBlock));
        categoryText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new LeafCategoryConverter() });
        categoryText.SetBinding(TextBlock.ToolTipProperty, new System.Windows.Data.Binding());
        categoryTemplate.VisualTree = categoryText;
        markerCategories.ItemTemplate = trailCategories.ItemTemplate = categoryTemplate;
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
        tabs.Items.Add(new TabItem { Header = "Markers", Content = markers });
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
        grid.Children.Add(tabs);
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
        Body.Children.Add(grid);

        assetsTree.SelectedItemChanged += (_, e) =>
        {
            UpdateRecordButton();
            if (!rebuildingAssets && e.NewValue is TreeViewItem { Tag: OverlayAsset asset }) AssetSelected?.Invoke(asset);
        };
        // TreeViewItem handles some mouse events itself. Catch preview input,
        // including handled events, and resolve the clicked row rather than the
        // previously selected row (which can belong to another XML).
        assetsTree.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(SelectClickedCategory), true);
        SourceInitialized += (_, _) =>
        {
            NativeGameWindow.DisableActivation(Handle);
            HwndSource.FromHwnd(Handle)?.AddHook(NoActivate);
        };
        LocationChanged += (_, _) =>
        {
            if (positioning || freePosition || !IsVisible || !client.TryBounds(out var bounds) || !NativeGameWindow.GetWindowRect(Handle, out var own)) return;
            offsetX = own.Left - bounds.Left;
            offsetY = own.Top - bounds.Top;
        };
    }

    private static nint NoActivate(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21)
        {
            handled = true;
            return 3;
        } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        return 0;
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
                var node = new TreeViewItem { Header = path.Length == 0 ? "Unassigned" : path[(path.LastIndexOf('.') + 1)..], IsExpanded = false, Padding = new Thickness(3, 2, 3, 2), ToolTip = xml + " · " + path, Tag = new OverlayCategory(xml, path) };
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

    public void EnsureHandle() => new WindowInteropHelper(this).EnsureHandle();

    public void UpdateVisibility(GameClientWindow game, bool playable, MumbleSnapshot? frame, int editorProcess, bool hosted = false)
    {
        client = game;
        if (freePosition != hosted)
        {
            freePosition = hosted;
            if (hosted && IsVisible) freePositionInitialized = true;
            lastX = lastY = int.MinValue;
        }
        canRecord = playable && game.TryBounds(out _);
        UpdateRecordButton();
        if (frame != null) telemetry.Text = frame.CharacterName + " · Map " + frame.MapId + "\n" + $"Mumble XYZ (Y-up)  {frame.AvatarPosition.X:F2}  {frame.AvatarPosition.Y:F2}  {frame.AvatarPosition.Z:F2}";
        if (!playable || !game.TryBounds(out var bounds) || !NativeGameWindow.IsForeground(game, Handle, editorProcess))
        {
            Topmost = false;
            if (IsVisible) Hide();
            return;
        }
        positioning = true;
        try
        {
            bool wasVisible = IsVisible;
            if (!IsVisible) Show(); // ShowActivated=false keeps the game's keyboard focus.
            if (!NativeGameWindow.IsForeground(game, Handle, editorProcess))
            {
                Topmost = false;
                Hide();
                return;
            }
            var transform = HwndSource.FromHwnd(Handle)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            int width = (int)(ActualWidth * transform.M11), height = (int)(ActualHeight * transform.M22);
            if (freePosition)
            {
                Topmost = true;
                if (!NativeGameWindow.IsTopmost(Handle)) NativeGameWindow.SetWindowPos(Handle, new nint(-1), 0, 0, 0, 0, 0x0053);
                // Place once, then retain the user's desktop position across
                // polling, pane resizing, game motion and hide/show cycles.
                if (!freePositionInitialized)
                {
                    if (NativeGameWindow.SetWindowPos(Handle, new nint(-1), bounds.Left + 24, bounds.Top + 24, 0, 0, 0x0011))
                        freePositionInitialized = true;
                }
                return;
            }
            int x = bounds.Left + Math.Clamp(offsetX, 0, Math.Max(0, bounds.Right - bounds.Left - width));
            int y = bounds.Top + Math.Clamp(offsetY, 0, Math.Max(0, bounds.Bottom - bounds.Top - height));
            bool reposition = !wasVisible || !Topmost || !NativeGameWindow.IsTopmost(Handle) || x != lastX || y != lastY;
            Topmost = true;
            if (reposition)
            {
                NativeGameWindow.SetWindowPos(Handle, new nint(-1), x, y, 0, 0, 0x0010 | 0x0001); // no activate, no resize
                lastX = x;
                lastY = y;
            }
        }
        finally
        {
            positioning = false;
        }
    }
}

internal sealed class LeafCategoryConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        string path = value as string ?? "";
        int dot = path.LastIndexOf('.');
        return dot < 0 ? path : "…" + path[(dot + 1)..];
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
