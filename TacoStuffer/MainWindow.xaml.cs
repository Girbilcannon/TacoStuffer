using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using TacoStuffer.Models;
using TacoStuffer.Services;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow : Window
{
    private sealed class XmlPage
    {
        private XDocument xml;
        private long revision, formattedRevision = -1;
        private string formatted = "";
        public XmlPage(string name, RichTextBox editor, TabItem tab, XDocument document)
        {
            Name = name;
            Editor = editor;
            Tab = tab;
            xml = document;
            xml.Changed += DocumentMutated;
        }

        private void DocumentMutated(object? sender, XObjectChangeEventArgs e)
        {
            revision++;
            Changed?.Invoke();
        }

        public Action? Changed { get; set; }
        public string Name { get; set; }
        public RichTextBox Editor { get; }
        public TabItem Tab { get; }
        public XDocument Xml
        {
            get => xml;
            set
            {
                if (ReferenceEquals(xml, value)) return;
                xml.Changed -= DocumentMutated;
                xml = value;
                xml.Changed += DocumentMutated;
                revision++;
                Changed?.Invoke();
            }
        }
        public string FormattedXml
        {
            get
            {
                if (formattedRevision != revision)
                {
                    formatted = FormatXml(xml);
                    formattedRevision = revision;
                }
                return formatted;
            }
        }
        public string Source { get; set; } = "";
        public bool Loaded { get; set; }
        public bool Dirty { get; set; }
        private string? indexedSource;
        private Func<int, XElement?>? sourceMap;
        public XElement? SourceElement(string source, int offset)
        {
            if (sourceMap == null || indexedSource != source)
            {
                sourceMap = XmlSourceIndex.Build(source);
                indexedSource = source;
            }
            return sourceMap(offset);
        }

        public void ReleaseSourceIndex()
        {
            indexedSource = null;
            sourceMap = null;
        }
    }

    private readonly List<XmlPage> pages = [];
    private readonly AttributeCatalog catalog = new();
    private readonly DispatcherTimer xmlTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private OverlayWindow? overlay;
    private ExplorerRow? selected;
    private string? projectFolder;
    private int xmlNumber = 3;
    private bool updating;
    private bool rebuildingAttributes;
    private bool applyingEdit;
    private int dragAnchor = -1;
    public MainWindow()
    {
        InitializeComponent();
        UpdateBuildStatus();
        PreviewKeyDown += ProjectShortcut;
        updating = true;
        Documents.Items.Clear();
        GameTab.Visibility = Visibility.Collapsed;
        AddTab.Visibility = Visibility.Collapsed;
        Documents.Items.Add(GameTab);
        Documents.Items.Add(AddTab);
        updating = false;
        catalog.Reload(null);
        InitializeWorkspace();
        InitializeHierarchy();
        InitializePlayInApp();
        InitializeGameConnection();
        RefreshExplorer();
        xmlTimer.Tick += (_, _) =>
        {
            xmlTimer.Stop();
            TrySyncXml();
            QueueHistoryCheckpoint("Edit XML");
        };
        InitializeProjectPersistence();
        ResetHistory();
        PreviewMouseDown += (_, _) => FlushHistoryCheckpoint();
        Closed += (_, _) =>
        {
            xmlTimer.Stop();
            searchTimer?.Stop();
            searchCancellation?.Cancel();
            searchCancellation?.Dispose();
            searchCancellation = null;
            overlay?.Close();
        };
    }

    private void ProjectShortcut(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            e.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
            return;
        }
        var modifiers = Keyboard.Modifiers;
        bool redo = modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z;
        if (!redo && !(modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.N or Key.O or Key.S))
        {
            CustomShortcut(e);
            return;
        }
        // Reserve undo/redo before text controls can invoke their separate native history.
        e.Handled = true;
        if (e.IsRepeat) return;
        if (redo || e.Key == Key.Z)
        {
            var code = codePages.FirstOrDefault(p => p.Editor.IsKeyboardFocusWithin);
            if (code != null)
            {
                if (redo) code.Editor.Redo();
                else code.Editor.Undo();
            }
            else if (redo) RedoProject();
            else UndoProject();
            return;
        }
        switch (e.Key)
        {
            case Key.N: CreateProject();
            break;
            case Key.O: OpenProject(this, new RoutedEventArgs());
            break;
            case Key.S: SaveCurrentProject();
            break;
        }
    }

    private void UpdateBuildStatus()
    {
        var assembly = typeof(MainWindow).Assembly;
        string version = (Attribute.GetCustomAttribute(assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute))
            as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3) ?? "unknown";
        BuildStatusText.Text = "Build: " + version;
    }

    private EditorPreferences? visualPreviewPreferences;
    private static string PreferenceFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacoStuffer", "preferences.json");
    private void ShowPreferences(object sender, RoutedEventArgs e) => EditPreferences(false);

    private void ShowGamePreferences(object sender, RoutedEventArgs e) => EditPreferences(true);

    private void EditPreferences(bool gameSettings)
    {
        var dialog = new PreferencesWindow(preferences, gameSettings) { Owner = this };
        dialog.VisualPreviewChanged += preview =>
        {
            visualPreviewPreferences = preview;
            worldMarkers?.ApplyVisualPreferences(preview);
        };
        try
        {
            if (dialog.ShowDialog() != true) return;
            preferences = dialog.Preferences;
            RefreshWorldMarkers();
            try
            {
                preferences.Save(PreferenceFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText.Text = "Preferences apply for this session; could not save them: " + ex.Message;
            }
        }
        finally
        {
            visualPreviewPreferences = null;
            worldMarkers?.ApplyVisualPreferences(preferences);
        }
    }

    private void Register(string name, RichTextBox editor, TabItem tab, string xml, bool loadEditor = true)
    {
        var page = new XmlPage(name, editor, tab, ParseXml(xml));
        pages.Add(page);
        page.Changed = InvalidateModelIndexes;
        InvalidateModelIndexes();
        editor.IsUndoEnabled = false;
        page.Source = xml;
        if (loadEditor) EnsureEditor(page);
        editor.TextChanged += (_, _) =>
        {
            if (!updating)
            {
                page.Dirty = true;
                editor.Tag = null;
                xmlTimer.Stop();
                xmlTimer.Start();
            }
        };
        WireEditorSelection(page);
    }

    private static XDocument ParseXml(string text)
    {
        using var source = new StringReader(text);
        using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var xml = XDocument.Load(reader);
        if (xml.Root?.Name != "OverlayData") throw new XmlException("Root element must be OverlayData.");
        return xml;
    }

    private bool TrySyncXml()
    {
        if (!pages.Any(p => p.Dirty)) return true;
        try
        {
            var parsed = pages.Select(p => p.Dirty ? ParseXml(PageSource(p)) : p.Xml).ToList();
            string? typeError = AttributeTypeError(parsed);
            if (typeError != null)
            {
                EditorNotice.Text = typeError + " Last valid state retained until XML is corrected.";
                return false;
            }
            categoryOrder = CategoryOrder.Collect(parsed, categoryOrder);
            for (int i = 0; i < pages.Count; i++) categoryOrder = CategoryOrder.AdoptDocument(categoryOrder, pages[i].Xml, parsed[i]);
            var originalPaths = parsed.Select(d => d.Descendants("MarkerCategory").Select(CategoryFullPath).ToList()).ToList();
            var normalized = CategoryOrder.Normalize(parsed, categoryOrder, clone: false);
            updating = true;
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Xml = normalized[i];
                pages[i].Source = PageSource(pages[i]);
                pages[i].Dirty = false;
                if (!originalPaths[i].SequenceEqual(normalized[i].Descendants("MarkerCategory").Select(CategoryFullPath))) SetPageSource(pages[i], FormatXml(normalized[i]));
            }
            updating = false;
            RefreshExplorer();
            EditorNotice.Text = "XML and Attributes synchronized. Save to keep your changes.";
            return true;
        }
        catch (XmlException ex)
        {
            EditorNotice.Text = "XML incomplete: " + ex.Message + " Last valid structure retained; pane edits are paused.";
            return false;
        }
    }

    private void WritePages()
    {
        categoryOrder = CategoryOrder.Collect(pages.Select(p => p.Xml), categoryOrder);
        var normalized = CategoryOrder.Normalize(pages.Select(p => p.Xml).ToList(), categoryOrder, clone: false);
        for (int i = 0; i < pages.Count; i++) pages[i].Xml = normalized[i];
        updating = true;
        foreach (var page in pages) SetPageSource(page, page.FormattedXml);
        updating = false;
        RefreshExplorer();
        EditorNotice.Text = "XML updated. Use Project → Save, or build to save and export.";
    }

    private static string FormatXml(XDocument document)
    {
        // Reader/editor whitespace can survive a move as text nodes, suppressing
        // LINQ to XML indentation. Normalize only element-only containers.
        var formatted = new XDocument(document);
        foreach (var element in formatted.Descendants())
        {
            // Removing the last child can leave whitespace or an explicit empty
            // content string. RemoveNodes resets both so the category self-closes.
            if (element.Name == "MarkerCategory" && element.Nodes().All(n => n is XText text && string.IsNullOrWhiteSpace(text.Value)))
            {
                element.RemoveNodes();
                continue;
            }
            if (!element.Elements().Any() || element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) continue;
            foreach (var whitespace in element.Nodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList()) whitespace.Remove();
        }
        return formatted.ToString();
    }

    private static string CategoryName(XElement e) => (string?)e.Attribute("name") ?? "unnamed";

    private static string CategoryFullPath(XElement e) => string.Join('.', e.AncestorsAndSelf("MarkerCategory").Reverse().Select(CategoryName));

    private static string ElementKey(XElement e) => ElementIdentity.Key(e);

    private static string Value(XElement e, string name, string fallback = "") => e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;

    private static void SetValue(XElement e, string name, string? value)
    {
        var attributes = e.Attributes().Where(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (value == null)
        {
            foreach (var a in attributes) a.Remove();
            return;
        }
        if (attributes.Count == 0) e.SetAttributeValue(name, value);
        else
        {
            attributes[0].Value = value;
            foreach (var duplicate in attributes.Skip(1)) duplicate.Remove();
        }
    }

    private void RefreshExplorer(bool refreshScene = true)
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("Explorer.Rebuild");
        InvalidateModelIndexes();
        string? focusedKey = selected == null ? null : selected.DocumentName + "|" + selected.Key;
        var keys = ElementsList.SelectedItems.Cast<ExplorerRow>().Select(r => r.DocumentName + "|" + r.Key).ToHashSet();
        if (selected != null) keys.Add(selected.DocumentName + "|" + selected.Key);
        var rows = new List<ExplorerRow>();
        if (projectFile != null) rows.Add(new(ProjectTitle.Text, "", null, 0, "project", "project"));
        foreach (var code in codePages) rows.Add(new("▤ " + code.Name, code.Name, null, 1, "code", "code:" + code.Name));
        var orderedPages = pages.Where(p => ViewOpen(p.Tab)).Concat(pages.Where(p => !ViewOpen(p.Tab))).ToList();
        bool closedHeading = false;
        foreach (var page in orderedPages)
        {
            if (!ViewOpen(page.Tab) && !closedHeading)
            {
                rows.Add(new("Closed Scripts", "", null, 0, "closedScripts", "closedScripts"));
                closedHeading = true;
            }
            var categoryPaths = page.Xml.Descendants("MarkerCategory").Select(CategoryFullPath).ToHashSet();
            var objects = (page.Xml.Root!.Element("POIs")?.Elements() ?? Enumerable.Empty<XElement>()).ToLookup(e => Value(e, "type"));
            rows.Add(new("▤ " + page.Name, page.Name, null, 1, "document", page.Name));
            foreach (var category in page.Xml.Root!.Elements("MarkerCategory")) AddCategoryRows(page, category, 2, rows, objects);
            foreach (var e in page.Xml.Root!.Element("POIs")?.Elements() ?? Enumerable.Empty<XElement>())
            if (!categoryPaths.Contains(Value(e, "type")))
            {
                rows.Add(new("◇ " + e.Name.LocalName + " · unassigned", page.Name, e, 2, e.Name.LocalName, ElementKey(e)));
                if (e.Name.LocalName == "Trail") AddTrailRows(page, e, 3, rows);
            }
        }
        updating = true;
        allExplorerRows = ExplorerHierarchy.Prepare(rows, collapsedElements);
        ReindexSearch();
        ElementsList.ItemsSource = FilterExplorer(allExplorerRows);
        rows = ElementsList.Items.Cast<ExplorerRow>().ToList();
        foreach (var row in rows.Where(r => keys.Contains(r.DocumentName + "|" + r.Key))) ElementsList.SelectedItems.Add(row);
        selected = rows.FirstOrDefault(r => r.DocumentName + "|" + r.Key == focusedKey) ?? ElementsList.SelectedItems.Cast<ExplorerRow>().FirstOrDefault(r => r.Element != null);
        if (refreshScene) RebuildQuickMenu();
        updating = false;
        if (refreshScene && selected?.Kind == "texture") ShowTextureAttributes(selected);
        else if (refreshScene && selected?.Element != null) ShowAttributes();
        else if (SearchQuery.Length > 0)
        {
            AttributesHost.Children.Clear();
            SelectionTitle.Text = "Select a search result";
            CategoryPath.Text = "";
        }
        if (refreshScene)
        {
            RefreshOverlayWorkspace();
            QueueHistoryCheckpoint();
        }
    }

    private void AddCategoryRows(XmlPage page, XElement category, int depth, List<ExplorerRow> rows, ILookup<string, XElement> objects)
    {
        string path = CategoryFullPath(category);
        rows.Add(new("▾ ▱ " + Value(category, "DisplayName", CategoryName(category)), page.Name, category, depth, "MarkerCategory", ElementKey(category)));
        foreach (var e in objects[path])
        {
            string label = (e.Name.LocalName == "Trail" ? "↝ " : "◆ ") + Value(e, "DisplayName", Value(e, "name", AssetStem(e)));
            rows.Add(new(label, page.Name, e, depth + 1, e.Name.LocalName, ElementKey(e)));
            if (e.Name.LocalName == "Trail") AddTrailRows(page, e, depth + 2, rows);
        }
        foreach (var child in category.Elements("MarkerCategory")) AddCategoryRows(page, child, depth + 1, rows, objects);
    }

    private void AddTrailRows(XmlPage page, XElement element, int depth, List<ExplorerRow> rows)
    {
        if (TrailFor(element) is not { } trail) return;
        foreach (var segment in trail.Segments)
        {
            var row = new ExplorerRow($"Segment {segment.Id:00000} · {segment.Points.Count} points", page.Name, element, depth, "segment", $"segment|{trail.Number}|{segment.Id}") { HasChildren = segment.Points.Count > 0 };
            if (initializedTrailSegments.Add(row.RowId)) collapsedElements.Add(row.RowId);
            rows.Add(row);
            if (collapsedElements.Contains(row.RowId) && SearchQuery.Length == 0) continue;
            foreach (var point in segment.Points)
            {
                string label = $"◆ Point {point.Id:0000000}";
                string pointKey = new TrailPointRef(trail.Number, segment.Id, point.Id).Key;
                if (SearchQuery.Length > 0 && !ExplorerSearch.Matches(label + " " + pointKey, SearchQuery)) continue;
                rows.Add(new(label, page.Name, element, depth + 1, "point", pointKey));
            }
        }
    }

    private IReadOnlyDictionary<string, XElement>? categoryValueIndex;
    private IReadOnlyDictionary<string, XElement> CategoryValues() => categoryValueIndex ??= Categories().ToDictionary(p => p.Key, p => p.Value.Element!);

    private IReadOnlyDictionary<string, ExplorerRow>? categoryIndex;
    private void InvalidateModelIndexes()
    {
        categoryIndex = null;
        categoryValueIndex = null;
        editableTrailIndex = null;
    }

    private IReadOnlyDictionary<string, ExplorerRow> Categories() => categoryIndex ??= pages.SelectMany(p => p.Xml.Descendants("MarkerCategory").Select(c => new ExplorerRow(Value(c, "DisplayName", CategoryName(c)), p.Name, c, 0, "MarkerCategory", ElementKey(c)))).GroupBy(r => CategoryFullPath(r.Element!)).ToDictionary(g => g.Key, g => g.First());

    private readonly Dictionary<string, TreeViewItem> quickMenuNodes = [];
    private readonly Dictionary<string, bool> quickMenuExpanded = [];
    private Dictionary<string, ExplorerRow> quickMenuRows = [];
    private TreeViewItem? EnsureQuickMenuNode(string path)
    {
        if (quickMenuNodes.TryGetValue(path, out var existing)) return existing;
        int dot = path.LastIndexOf('.');
        if (dot < 0) return null;
        if (EnsureQuickMenuNode(path[..dot]) is { } parent) parent.IsExpanded = true;
        return quickMenuNodes.GetValueOrDefault(path);
    }

    private void RebuildQuickMenu()
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("QuickMenu.Build");
        foreach (var pair in quickMenuNodes) quickMenuExpanded[pair.Key] = pair.Value.IsExpanded;
        CategoryTree.Items.Clear();
        quickMenuNodes.Clear();
        quickMenuRows = Categories().ToDictionary(p => p.Key, p => p.Value);
        var orderIndex = categoryOrder.Select((path, index) => (path, index)).ToDictionary(p => p.path, p => p.index);
        var ordered = quickMenuRows.Keys.OrderBy(p => orderIndex.GetValueOrDefault(p, int.MaxValue)).ToList();
        string? ParentPath(string path)
        {
            int dot = path.LastIndexOf('.');
            if (dot < 0) return null;
            string parent = path[..dot];
            return quickMenuRows.ContainsKey(parent) ? parent : null;
        }
        var children = ordered.ToLookup(ParentPath);
        TreeViewItem Create(string path)
        {
            var row = quickMenuRows[path];
            FrameworkElement header;
            string separator = Value(row.Element!, "isSeparator");
            if (separator == "1" || separator.Equals("true", StringComparison.OrdinalIgnoreCase))
                header = new Border
                {
                    BorderBrush = (Brush)FindResource("AccentBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 1, 0, 2), Margin = new Thickness(0, 1, 0, 1),
                    Child = new TextBlock { Text = row.Label, Foreground = (Brush)FindResource("AccentBrush"), FontWeight = FontWeights.SemiBold, MinHeight = 15 }
                };
            else
            {
                var check = new CheckBox { Content = row.Label, IsChecked = !hiddenPreviewCategories.Contains(path), Margin = new Thickness(0, 1, 0, 1), ToolTip = "Editor preview visibility; does not alter exported attributes." };
                check.Click += (_, _) =>
                {
                    if (check.IsChecked == true) hiddenPreviewCategories.Remove(path);
                    else hiddenPreviewCategories.Add(path);
                    ApplyPreviewVisibility();
                };
                header = check;
            }
            var node = new TreeViewItem { Header = header, Tag = row, IsExpanded = false, Padding = new Thickness(4, 1, 4, 1), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            quickMenuNodes[path] = node;
            AddCategoryContext(node, row);
            if (children[path].Any())
            {
                node.Items.Add(new TreeViewItem { Header = "", IsEnabled = false });
                bool populated = false;
                void Populate()
                {
                    if (populated) return;
                    populated = true;
                    node.Items.Clear();
                    foreach (string child in children[path]) node.Items.Add(Create(child));
                }
                node.Expanded += (_, e) =>
                {
                    if (e.OriginalSource != node) return;
                    quickMenuExpanded[path] = true;
                    Populate();
                };
                node.Collapsed += (_, e) =>
                {
                    if (e.OriginalSource == node) quickMenuExpanded[path] = false;
                };
                node.IsExpanded = quickMenuExpanded.GetValueOrDefault(path, !path.Contains('.'));
                if (node.IsExpanded) Populate();
            }
            return node;
        }
        foreach (string root in children[null]) CategoryTree.Items.Add(Create(root));
        FilterQuickMenu();
    }

    private void SelectElements(object sender, SelectionChangedEventArgs e)
    {
        if (updating || selectingFromCode) return;
        var focus = e.AddedItems.Cast<ExplorerRow>().LastOrDefault() ?? ElementsList.SelectedItems.Cast<ExplorerRow>().LastOrDefault();
        if (focus == null)
        {
            ClearWorldSelection();
            UpdateTrailControls();
            return;
        }
        if (focus.Kind == "project")
        {
            selected = focus;
            selectedTrailPoints.Clear();
            SyncOverlaySelection();
            UpdateTrailControls();
            AttributesHost.Children.Clear();
            SelectionTitle.Text = ProjectTitle.Text;
            CategoryPath.Text = "";
            return;
        }
        if (focus.Kind == "document")
        {
            selected = focus;
            selectedTrailPoints.Clear();
            SyncOverlaySelection();
            UpdateTrailControls();
            var page = pages.FirstOrDefault(p => p.Name == focus.DocumentName);
            if (page != null) OpenTab(page.Tab);
            AttributesHost.Children.Clear();
            SelectionTitle.Text = focus.DocumentName;
            CategoryPath.Text = "";
            return;
        }
        if (focus.Kind == "code")
        {
            selected = focus;
            selectedTrailPoints.Clear();
            SyncOverlaySelection();
            UpdateTrailControls();
            var code = codePages.FirstOrDefault(p => p.Name == focus.DocumentName);
            if (code != null) OpenTab(code.Tab);
            AttributesHost.Children.Clear();
            SelectionTitle.Text = focus.DocumentName;
            CategoryPath.Text = "Lua code · saved and packaged; execution/visual preview is not supported yet.";
            return;
        }
        if (focus.Kind == "texture")
        {
            selected = focus;
            ShowTextureAttributes(focus);
            return;
        }
        if (focus.Element == null) return;
        selected = focus;
        var active = pages.First(p => p.Name == selected.DocumentName);
        OpenTab(active.Tab);
        ShowAttributes();
        selectingFromExplorer = true;
        try
        {
            SelectElementText(active.Editor, selected.Element);
        }
        finally
        {
            selectingFromExplorer = false;
        }
        StatusText.Text = $"{ElementsList.SelectedItems.Count} selected · Shift / Ctrl / drag supported · changes are in memory";
    }

    private sealed record XmlRunIndex(string Source, Dictionary<string, List<Run>> Tokens, Dictionary<string, Run> Elements);

    private static void SelectElementText(RichTextBox editor, XElement element)
    {
        if (editor.Tag is XmlRunIndex indexed && indexed.Elements.TryGetValue(ElementKey(element), out var exact))
        {
            editor.Selection.Select(exact.ContentStart, exact.ContentEnd);
            return;
        }
        string value = element.Name.LocalName == "MarkerCategory" ? CategoryName(element) : Value(element, "GUID");
        if (value.Length == 0) return;
        string token = "\"" + value + "\"";
        if (editor.Tag is XmlRunIndex index && index.Tokens.TryGetValue(token, out var matches) && matches.Count > 0)
        {
            var run = matches[0];
            editor.Selection.Select(run.ContentStart, run.ContentEnd);
            return;
        }
        // The token index is discarded after typing; use the normal text walk
        // until the edited document is formatted again.
        for (TextPointer? p = editor.Document.ContentStart; p != null && p.CompareTo(editor.Document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            string part = p.GetTextInRun(LogicalDirection.Forward);
            int offset = part.IndexOf(token, StringComparison.Ordinal);
            if (offset < 0) continue;
            var first = p.GetPositionAtOffset(offset);
            var last = first?.GetPositionAtOffset(token.Length);
            if (first != null && last != null) editor.Selection.Select(first, last);
            break;
        }
    }

    private void SelectCategory(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (updating || e.NewValue is not TreeViewItem { Tag: ExplorerRow row }) return;
        if (Mouse.LeftButton == MouseButtonState.Pressed && Ancestor<CheckBox>(Mouse.DirectlyOver) != null) return;
        FocusRow(row);
    }

    private void ShowAttributes()
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("Attributes.Build");
        if (selected?.Kind == "segment")
        {
            ShowTrailSegmentAttributes();
            return;
        }
        if (selected?.Kind == "point")
        {
            ShowTrailPointAttributes();
            return;
        }
        SyncOverlaySelection();
        if (selected?.Element is not XElement element) return;
        rebuildingAttributes = true;
        AttributesHost.Children.Clear();
        bool category = selected.Kind == "MarkerCategory";
        SelectionTitle.Text = category ? Value(element, "DisplayName", CategoryName(element)) : selected.Label.TrimStart('◆', '↝', ' ');
        CategoryPath.Text = category ? CategoryFullPath(element) : Value(element, "type");
        if (ElementsList.SelectedItems.Count > 1)
        {
            Section($"{ElementsList.SelectedItems.Count} ITEMS SELECTED");
            AttributesHost.Children.Add(new TextBlock { Text = "Add Attributes and non-identity value edits apply to every selected object. Values below show the focused object; name, GUID and type edits affect only it.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush") });
        }
        if (element.Name.LocalName is "POI" or "Trail")
            AttributesHost.Children.Add(new TextBlock { Text = "Position: Mumble/TacO meters · Y is vertical; X/Z are horizontal.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 8) });
        Section("IDENTITY");
        if (category)
        {
            string full = CategoryFullPath(element);
            string prefix = full.Contains('.') ? full[..(full.LastIndexOf('.') + 1)] : "";
            AttributesHost.Children.Add(new TextBlock { Text = "name · parent path is protected", Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 5, 0, 5) });
            var line = new DockPanel();
            var prefixText = new TextBlock { Text = prefix, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("AccentBrush") };
            DockPanel.SetDock(prefixText, Dock.Left);
            line.Children.Add(prefixText);
            var name = new TextBox { Text = CategoryName(element), ToolTip = "Edit this category's final name only; all matching type paths will update." };
            line.Children.Add(name);
            AttributesHost.Children.Add(line);
            Commit(name, () => RenameCategory(name.Text));
            Field("DisplayName", Value(element, "DisplayName", CategoryName(element)), v => ChangeAttribute("DisplayName", v));
        }
        else
        {
            Field("name", Value(element, "name", AssetStem(element)), v => ChangeAttribute("name", v));
            Field("DisplayName", Value(element, "DisplayName", Value(element, "name", AssetStem(element))), v => ChangeAttribute("DisplayName", v));
            foreach (string a in new[] { "MapID", "GUID", "type" }) if (element.Attribute(a) != null) Field(a, Value(element, a), v => ChangeAttribute(a, v));
        }
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "DisplayName", "MapID", "GUID", "type" };
        foreach (var definition in catalog.Groups)
        {
            if (!definition.Applies(element) || !definition.Members.Any(m => EffectiveValue(element, m.Name, "\0") != "\0")) continue;
            Section(definition.DisplayName.ToUpperInvariant() + (definition.IsCustom ? " · CUSTOM" : ""));
            foreach (var member in definition.Members)
            {
                consumed.Add(member.Name);
                bool present = element.Attributes().Any(a => a.Name.LocalName.Equals(member.Name, StringComparison.OrdinalIgnoreCase));
                if (member.Optional)
                {
                    var enabled = new CheckBox { Content = "Enable " + member.Name, IsChecked = present };
                    enabled.Click += (_, _) => ChangeAttribute(member.Name, enabled.IsChecked == true ? member.DefaultValue : null);
                    AttributesHost.Children.Add(enabled);
                    if (!present) continue;
                }
                if (member.Type == "bool")
                {
                    string effective = EffectiveValue(element, member.Name, member.DefaultValue);
                    var check = new CheckBox { Content = member.Name, IsChecked = effective is "1" or "true" };
                    check.Click += (_, _) => ChangeAttribute(member.Name, check.IsChecked == true ? "1" : "0");
                    AttributesHost.Children.Add(check);
                }
                else if (present && (member.Name is "iconFile" or "texture")) AssetField(member.Name, Value(element, member.Name));
                else if (present) Field(member.Name, Value(element, member.Name), v => ChangeAttribute(member.Name, v));
                else if (EffectiveValue(element, member.Name, "\0") != "\0") AttributesHost.Children.Add(new TextBlock { Text = member.Name + " · inherited: " + EffectiveValue(element, member.Name, ""), Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 5, 0, 5) });
                else AttributesHost.Children.Add(new TextBlock { Text = "⚠ Missing required: " + member.Name, Foreground = (Brush)FindResource("TortillaBrush"), Margin = new Thickness(0, 5, 0, 5) });
            }
            if (definition.Members.Any(m => !m.Optional && EffectiveValue(element, m.Name, "\0") == "\0"))
            {
                var required = new Button { Content = "Add missing required controls", Margin = new Thickness(0, 8, 0, 4) };
                required.Click += (_, _) => AddMissingControls(definition);
                AttributesHost.Children.Add(required);
            }
            bool localGroup = definition.Members.Any(m => element.Attributes().Any(a => a.Name.LocalName.Equals(m.Name, StringComparison.OrdinalIgnoreCase)));
            var remove = new Button { Content = localGroup ? "Remove local " + definition.DisplayName : "Override inherited " + definition.DisplayName, Margin = new Thickness(0, 8, 0, 4) };
            remove.Click += (_, _) =>
            {
                if (!TrySyncXml() || selected?.Element == null) return;
                var values = new Dictionary<string, string?>();
                foreach (var member in definition.Members)
                {
                    if (localGroup) values[member.Name] = null;
                    else
                    {
                        string inherited = EffectiveValue(selected.Element, member.Name, "\0");
                        if (inherited != "\0") values[member.Name] = inherited;
                    }
                }
                ApplyAttributeValues(AttributeTargets(), values);
            };
            AttributesHost.Children.Add(remove);
        }
        if (!catalog.Groups.Any(g => g.Key == "visibility" && g.Members.Any(m => element.Attributes().Any(a => a.Name.LocalName.Equals(m.Name, StringComparison.OrdinalIgnoreCase)))))
        {
            Section("VISIBILITY");
            foreach (var (name, label) in new[] { ("inGameVisibility", "Show in world"), ("miniMapVisibility", "Show on minimap"), ("mapVisibility", "Show on full map") })
            {
                var check = new CheckBox { Content = label, IsChecked = EffectiveValue(element, name, "1") is "1" or "true", ToolTip = "Click writes an explicit XML attribute: " + name };
                check.Click += (_, _) => ChangeAttribute(name, check.IsChecked == true ? "1" : "0");
                AttributesHost.Children.Add(check);
                consumed.Add(name);
            }
        }
        var unknown = element.Attributes().Where(a => !consumed.Contains(a.Name.LocalName) && !IsStandardName(a.Name.LocalName)).ToList();
        var standard = element.Attributes().Where(a => !consumed.Contains(a.Name.LocalName) && IsStandardName(a.Name.LocalName)).ToList();
        if (standard.Count != 0)
        {
            Section("OTHER STANDARD ATTRIBUTES");
            foreach (var a in standard) Field(a.Name.LocalName, a.Value, v => ChangeAttribute(a.Name.LocalName, v));
        }
        if (unknown.Count != 0)
        {
            Section("CUSTOM ATTRIBUTES");
            foreach (var a in unknown) Field(a.Name.LocalName, a.Value, v => ChangeAttribute(a.Name.LocalName, v));
            var export = new Button { Content = "Export Custom Attributes…", Margin = new Thickness(0, 12, 0, 0) };
            export.Click += (_, _) => ExportCustom(unknown);
            AttributesHost.Children.Add(export);
        }
        rebuildingAttributes = false;
    }

    private static bool IsStandardName(string name) => ("Achievement AutoTrigger Behavior Bounce CanFade Copy Cull DefaultToggle Festival Info InvertBehavior IsHidden IsWall MapType Mount Occlude Profession Race Raid ResetGUID ResetLength Rotate ScaleOnMapWithZoom Schedule Script ShowHide Size Specialization Tip Toggle TrailData TriggerRange Visibility achievementId achievementBit rotate").Split(' ').Contains(name, StringComparer.OrdinalIgnoreCase);

    private string EffectiveValue(XElement element, string name, string fallback)
    {
        return AttributeRules.Effective(element, name, fallback, CategoryValues());
    }

    private void ChangeAttribute(string name, string? value)
    {
        if (!TrySyncXml() || selected?.Element == null) return;
        var targets = name.ToLowerInvariant() is "name" or "guid" or "type" ? new List<XElement> { selected.Element } : AttributeTargets();
        ApplyAttributeValues(targets, new Dictionary<string, string?> { [name] = value });
    }

    private void RenameCategory(string name)
    {
        if (!TrySyncXml() || selected?.Element?.Name != "MarkerCategory") return;
        name = name.Trim();
        try
        {
            XmlConvert.VerifyNCName(name);
            if (name.Contains('.')) throw new XmlException("Category name cannot contain a dot.");
        }
        catch (XmlException ex)
        {
            Notice("Invalid category name", ex.Message);
            ShowAttributes();
            return;
        }
        string oldPath = CategoryFullPath(selected.Element);
        string newPath = oldPath.Contains('.') ? oldPath[..(oldPath.LastIndexOf('.') + 1)] + name : name;
        if (oldPath == newPath) return;
        if (Categories().ContainsKey(newPath))
        {
            Notice("Category already exists", "Choose a distinct category name.");
            ShowAttributes();
            return;
        }
        foreach (var category in pages.SelectMany(p => p.Xml.Descendants("MarkerCategory")).Where(c => CategoryFullPath(c) == oldPath).ToList()) category.SetAttributeValue("name", name);
        foreach (var element in pages.SelectMany(p => p.Xml.Descendants()).Where(e => e.Attribute("type") != null))
        {
            string path = Value(element, "type");
            if (path == oldPath || path.StartsWith(oldPath + '.', StringComparison.Ordinal)) SetValue(element, "type", newPath + path[oldPath.Length..]);
        }
        var collapsed = collapsedElements.Select(k =>
        {
            int split = k.IndexOf("|category:", StringComparison.Ordinal);
            if (split < 0) return k;
            string path = k[(split + 10)..];
            return path == oldPath || path.StartsWith(oldPath + '.', StringComparison.Ordinal) ? k[..(split + 10)] + newPath + path[oldPath.Length..] : k;
        }).ToList();
        collapsedElements.Clear();
        foreach (var key in collapsed) collapsedElements.Add(key);
        categoryOrder = categoryOrder.Select(p => p == oldPath || p.StartsWith(oldPath + '.', StringComparison.Ordinal) ? newPath + p[oldPath.Length..] : p).ToList();
        selected = selected with { Key = "category:" + newPath };
        WritePages();
    }

    private void RenameDisplayName(string value)
    {
        if (!TrySyncXml() || selected?.Element == null) return;
        string path = CategoryFullPath(selected.Element);
        foreach (var c in pages.SelectMany(p => p.Xml.Descendants("MarkerCategory")).Where(c => CategoryFullPath(c) == path)) SetValue(c, "DisplayName", value);
        WritePages();
    }

    private void Section(string text) => AttributesHost.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("SectionTitle") });

    private void Field(string name, string value, Action<string> change)
    {
        AttributesHost.Children.Add(new TextBlock { Text = name, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 6, 0, 4) });
        var input = new TextBox { Text = value, Tag = name };
        Commit(input, () =>
        {
            if (input.Text != value) change(input.Text);
        });
        AttributesHost.Children.Add(input);
    }

    private void Commit(TextBox input, Action action)
    {
        void Apply()
        {
            if (updating || rebuildingAttributes || applyingEdit) return;
            applyingEdit = true;
            try
            {
                action();
            }
            finally
            {
                applyingEdit = false;
            }
        }
        input.LostKeyboardFocus += (_, _) => Apply();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Apply();
                e.Handled = true;
            }
        };
    }

    private static void ColorXml(RichTextBox editor, string xml)
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("XML.Color");
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 22 };
        var brushes = new Dictionary<string, Brush>();
        foreach (string color in new[] { "#788796", "#E3BE7B", "#88C7BB", "#BBC9DF" })
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            brushes[color] = brush;
        }
        var tokens = new Dictionary<string, List<Run>>();
        var starts = new Dictionary<int, Run>();
        int sourceOffset = 0;
        foreach (string part in Regex.Split(xml, "(<!--.*?-->|\"[^\"]*\"|</?[A-Za-z][A-Za-z0-9]*)", RegexOptions.Singleline))
        {
            string color = part.StartsWith("<!--") ? "#788796" : part.StartsWith('"') ? "#E3BE7B" : part.StartsWith('<') ? "#88C7BB" : "#BBC9DF";
            var run = new Run(part) { Foreground = brushes[color] };
            paragraph.Inlines.Add(run);
            if (part.StartsWith('<')) starts[sourceOffset] = run;
            sourceOffset += part.Length;
            if (part.StartsWith('"'))
            {
                if (!tokens.TryGetValue(part, out var runs)) tokens[part] = runs = [];
                runs.Add(run);
            }
        }
        var elements = new Dictionary<string, Run>();
        try
        {
            foreach (var pair in XmlSourceIndex.BuildMap(xml).Starts) if (starts.TryGetValue(pair.Value, out var run)) elements[pair.Key] = run;
        }
        catch (XmlException)
        { /* Invalid source stays editable with token highlighting. */
        }
        editor.Tag = new XmlRunIndex(xml, tokens, elements);
        editor.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(18), FontFamily = new FontFamily("Consolas"), FontSize = 13 };
    }

    private void DocumentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (inAppLayout && !updating && e.Source is TabControl gameOwner && gameOwner.SelectedItem is TabItem gameCode && gameCode != GameTab && gameCode != AddTab)
        {
            OpenHostedDocument(gameCode);
            return;
        }
        if (e.Source is TabControl owner && owner.SelectedItem is TabItem tab) PrepareDocumentView(tab);
        if (pages == null || Documents == null || AddTab == null || Documents.SelectedItem != AddTab || updating) return;
        CreateXmlPage();
    }

    private void CreateXmlPage()
    {
        if (projectFile == null) return;
        if (!TrySyncXml())
        {
            Documents.SelectedItem = pages.FirstOrDefault()?.Tab;
            return;
        }
        var rootCategory = pages.SelectMany(p => p.Xml.Root!.Elements("MarkerCategory")).FirstOrDefault();
        string packName = rootCategory == null ? Regex.Replace(ProjectTitle.Text.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_') : CategoryName(rootCategory);
        if (packName.Length == 0) packName = "pack";
        string display = rootCategory == null ? ProjectTitle.Text : Value(rootCategory, "DisplayName", ProjectTitle.Text);
        string name;
        do
        {
            name = $"markers{xmlNumber++}.xml";
        }
        while (pages.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        var xml = new XDocument(new XElement("OverlayData", new XElement("MarkerCategory", new XAttribute("name", packName), new XAttribute("DisplayName", display), new XElement("MarkerCategory", new XAttribute("name", Path.GetFileNameWithoutExtension(name)), new XAttribute("DisplayName", "New Category"))), new XElement("POIs")));
        var editor = new RichTextBox { FontFamily = new FontFamily("Consolas"), FontSize = 13, AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var tab = new TabItem { Header = name, Content = editor };
        Register(name, editor, tab, xml.ToString());
        OpenTab(tab);
        RefreshExplorer();
    }

    private void AddAttributes(object sender, RoutedEventArgs e)
    {
        if (selected?.Kind is "point" or "segment") return;
        if (!TrySyncXml()) return;
        var targets = AttributeTargets();
        if (targets.Count == 0) return;
        if (!catalog.Groups.Any(g => !g.IsCustom))
        {
            Notice("Built-in attributes unavailable", "The application could not load its compiled attribute definitions.\n\n" + string.Join("\n", catalog.Errors));
            return;
        }
        // Marker coordinates are added automatically and remain editable in Attributes.
        // Offer Position here only for categories, including when several items are selected.
        var definitions = catalog.Groups.Where(g => targets.All(g.Applies)
            && (!g.Key.Equals("position", StringComparison.OrdinalIgnoreCase)
                || targets.All(t => t.Name.LocalName.Equals("MarkerCategory", StringComparison.OrdinalIgnoreCase)))).ToList();
        var dialog = new AttributePickerWindow(definitions, ReloadDefinitions) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var values = dialog.SelectedGroups.SelectMany(g => g.Members).ToDictionary(m => m.Name, m => (string?)m.DefaultValue, StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 1) values = values.Where(p => !targets[0].Attributes().Any(a => a.Name.LocalName.Equals(p.Key, StringComparison.OrdinalIgnoreCase))).ToDictionary(p => p.Key, p => p.Value);
        ApplyAttributeValues(targets, values);
    }

    private void ReloadDefinitions()
    {
        if (projectFolder == null)
        {
            var folder = new OpenFolderDialog { Title = "Choose project folder containing CUSTOM_ATTRIBUTES" };
            if (folder.ShowDialog(this) != true) return;
            projectFolder = folder.FolderName;
        }
        catalog.Reload(projectFolder);
        ShowAttributes();
        if (catalog.Errors.Count != 0) Notice("Definition warnings", string.Join("\n", catalog.Errors));
    }

    private void ExportCustom(List<XAttribute> attributes)
    {
        var definition = new CustomExportWindow(attributes) { Owner = this };
        if (definition.ShowDialog() != true || definition.Definition == null) return;
        if (projectFolder == null)
        {
            var folder = new OpenFolderDialog { Title = "Choose the project folder; definitions go into CUSTOM_ATTRIBUTES" };
            if (folder.ShowDialog(this) != true) return;
            projectFolder = folder.FolderName;
        }
        try
        {
            string target = Path.Combine(projectFolder, "CUSTOM_ATTRIBUTES");
            Directory.CreateDirectory(target);
            var save = new SaveFileDialog { InitialDirectory = target, FileName = definition.Definition.Key + ".xml", Filter = "Attribute definition (*.xml)|*.xml", AddExtension = true, DefaultExt = ".xml", OverwritePrompt = true };
            if (save.ShowDialog(this) != true) return;
            if (!string.Equals(Path.GetFullPath(Path.GetDirectoryName(save.FileName)!), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                Notice("Choose CUSTOM_ATTRIBUTES", "Save this definition inside this project's CUSTOM_ATTRIBUTES folder so it can be registered.");
                return;
            }
            definition.Definition.ToXml().Save(save.FileName);
            catalog.Reload(projectFolder);
            ShowAttributes();
            Notice("Custom attributes exported", "XML values were retained. The saved definition is available under Custom now. After editing definition files externally, reload definitions or reopen the project.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice("Export failed", ex.Message);
        }
    }

    private ListBoxItem? RowContainer(object? source)
    {
        var current = source as DependencyObject;
        while (current != null && current is not ListBoxItem) current = current is Visual ? VisualTreeHelper.GetParent(current) : null;
        return current as ListBoxItem;
    }

    private void BeginSelectionDrag(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<ExplorerRowView>(e.OriginalSource) is { } view && view.IsToggleHit(e.GetPosition(view))) return;
        var container = RowContainer(e.OriginalSource);
        dragAnchor = container == null ? -1 : ElementsList.ItemContainerGenerator.IndexFromContainer(container);
        if (container != null && ElementsList.ItemContainerGenerator.ItemFromContainer(container) is ExplorerRow row && row.DocumentName.Length > 0)
        {
            var page = pages.FirstOrDefault(p => p.Name == row.DocumentName);
            if (page != null) OpenTab(page.Tab);
        }
    }

    private void SelectionDrag(object sender, MouseEventArgs e)
    {
        ElementsMiddleMove(e);
        if (dragAnchor < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        var item = RowContainer(ElementsList.InputHitTest(e.GetPosition(ElementsList)));
        if (item == null) return;
        int end = ElementsList.ItemContainerGenerator.IndexFromContainer(item);
        if (end < 0 || end == dragAnchor) return;
        for (int i = Math.Min(dragAnchor, end); i <= Math.Max(dragAnchor, end); i++) if (!ElementsList.SelectedItems.Contains(ElementsList.Items[i])) ElementsList.SelectedItems.Add(ElementsList.Items[i]);
    }

    private void EndSelectionDrag(object sender, MouseButtonEventArgs e) => dragAnchor = -1;

    private void ElementsRightClick(object sender, MouseButtonEventArgs e)
    {
        var item = RowContainer(e.OriginalSource);
        if (item == null) return;
        var row = ElementsList.ItemContainerGenerator.ItemFromContainer(item) as ExplorerRow;
        if (row == null || (row.Element == null && row.Kind is not ("texture" or "document"))) return;
        if (!ElementsList.SelectedItems.Contains(row))
        {
            ElementsList.SelectedItems.Clear();
            ElementsList.SelectedItems.Add(row);
        }
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename… (F2)" };
        rename.Click += (_, _) => QuickRename(row);
        menu.Items.Add(rename);
        if (row.Kind == "document")
        {
            var removeXml = new MenuItem { Header = "Delete XML…" };
            removeXml.Click += (_, _) => DeleteXmlSelection();
            menu.Items.Add(removeXml);
            menu.PlacementTarget = item;
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }
        if (row.Kind is "point" or "segment")
        {
            var remove = new MenuItem { Header = row.Kind == "segment" ? "Delete selected segments…" : "Delete selected points…" };
            remove.Click += (_, _) => DeleteSelection();
            menu.Items.Add(remove);
            menu.PlacementTarget = item;
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }
        if (row.Kind == "Trail" && TrailFor(row.Element) == null)
        {
            var edit = new MenuItem { Header = "Make Editable" };
            edit.Click += (_, _) => ImportEditableTrail(row);
            menu.Items.Add(edit);
        }
        if (row.Kind is "texture")
        {
            menu.PlacementTarget = item;
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }
        var category = new MenuItem { Header = "Move to different category…" };
        category.Click += (_, _) => PrepareMove("category");
        menu.Items.Add(category);
        var xml = new MenuItem { Header = "Move to XML…" };
        xml.Click += (_, _) => PrepareMove("xml");
        menu.Items.Add(xml);
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "Delete…" };
        delete.Click += (_, _) => DeleteSelection();
        menu.Items.Add(delete);
        menu.PlacementTarget = item;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void PrepareMove(string operation)
    {
        if (!TrySyncXml()) return;
        var selection = ElementsList.SelectedItems.Cast<ExplorerRow>().Where(r => r.Kind is "POI" or "Trail" or "MarkerCategory").ToList();
        if (selection.Count == 0) return;
        var categoryPaths = selection.Where(r => r.Kind == "MarkerCategory").Select(r => CategoryFullPath(r.Element!)).ToList();
        var targets = operation == "xml" ? pages.Select(p => p.Name).ToList() : Categories().Keys.Where(p => !categoryPaths.Any(c => p == c || p.StartsWith(c + '.', StringComparison.Ordinal))).ToList();
        var dialog = new MovePickerWindow(operation, selection.Count, targets) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var changed = PackEditing.Move(pages.Select(p => p.Xml).ToList(), SelectionReferences(selection), operation, dialog.Destination, pages.FindIndex(p => p.Name == dialog.Destination));
            ApplyDocuments(changed);
            if (operation == "xml")
            {
                OpenTab(pages.First(p => p.Name == dialog.Destination).Tab);
                var destinationRow = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.DocumentName == dialog.Destination && r.Kind == "document");
                if (destinationRow != null) FocusRow(destinationRow, false);
            }
            else
            {
                var destinationRow = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.Key == "category:" + dialog.Destination);
                if (destinationRow != null) FocusRow(destinationRow);
            }
            StatusText.Text = $"Moved {selection.Count} selected items → {dialog.Destination}. XML updated in memory.";
        }
        catch (InvalidOperationException ex)
        {
            Notice("Move blocked", ex.Message);
        }
    }

    private void ShowOverlay(object sender, RoutedEventArgs e)
    {
        if (projectFile == null || pages.Count == 0)
        {
            Notice("No XML target", "Create/open a project and add an XML document first.");
            return;
        }
        PollGame();
        if (!mumbleSession.IsPlayable || gameClient.Handle == 0)
        {
            Notice("Waiting for GW2", "Enter a playable map and wait for MumbleLink to connect before opening the overlay.");
            return;
        }
        if (!TrySyncXml()) return;
        if (overlay == null)
        {
            overlay = new OverlayWindow(gameClient.ProcessId);
            previousOverlayDeleteDown = NativeGameWindow.DeleteDown;
            overlay.SetTrailSmoothness(trailSampler.Smoothness);
            overlay.TrailSmoothnessChanged += SetTrailSmoothness;
            worldMarkers = new WorldMarkerWindow();
            overlay.MarkerRequested += RecordMarker;
            overlay.PreviewKeyDown += ProjectShortcut;
            overlay.PreviewMouseDown += (_, _) => FlushHistoryCheckpoint();
            overlay.RepositionRequested += RepositionMarker;
            overlay.RenameAssetRequested += RenameOverlayAsset;
            overlay.NewTrailRequested += NewTrail;
            overlay.StartTrailRequested += StartTrailRecording;
            overlay.PauseTrailRequested += PauseTrailRecording;
            overlay.NewSegmentRequested += NewTrailSegment;
            overlay.StopTrailRequested += () =>
            {
                StopTrailRecording();
                RefreshExplorer();
            };
            overlay.TrailChosen += ChooseTrail;
            overlay.SegmentSelected += SelectTrailSegment;
            overlay.PointsSelected += SelectTrailPoints;
            overlay.DeletePointsRequested += points =>
            {
                if (overlay != null) WithDeleteOwner(overlay, () => DeleteTrailPoints(points));
            };
            overlay.DeleteAssetRequested += asset =>
            {
                if (overlay != null) WithDeleteOwner(overlay, () => DeleteOverlayAsset(asset));
            };
            overlay.DeleteSegmentRequested += segment =>
            {
                if (overlay != null) WithDeleteOwner(overlay, () =>
                    {
                        SelectTrailSegment(segment);
                        DeleteTrailSegments();
                    });
            };
            overlay.CategoryRequested += AddOverlayCategory;
            worldMarkers.MarkerSelected += SelectWorldMarker;
            worldMarkers.TrailSelected += SelectWorldTrail;
            worldMarkers.SelectionCleared += ClearWorldSelection;
            worldMarkers.MarkerMoved += MoveWorldMarker;
            worldMarkers.PointsMoved += ApplyPointTransform;
            worldMarkers.BeginMove = () =>
            {
                FlushHistoryCheckpoint();
                xmlTimer.Stop();
                return !workspaceBusy && mumbleSession.IsPlayable && (!trailRecording || trailPaused) && TrySyncXml();
            };
            overlay.AssetSelected += asset =>
            {
                var row = RevealRow(asset.DocumentName, asset.Key);
                if (row != null) FocusRow(row, activateDetached: false);
            };
            overlay.Closed += (_, _) =>
            {
                StopTrailRecording();
                overlay = null;
                StopWorldRendering();
                worldMarkers?.Close();
                worldMarkers = null;
            };
            overlay.EnsureHandle();
            StartWorldRendering();
        }
        RefreshOverlayWorkspace();
        var selectedPage = pages.FirstOrDefault(p => p.Name == selected?.DocumentName) ?? pages.FirstOrDefault(p => p.Tab.IsSelected) ?? pages.First();
        string? targetPath = selected?.Element?.Name == "MarkerCategory" ? CategoryFullPath(selected.Element) : selected?.Element == null ? null : Value(selected.Element, "type");
        overlay.SetTarget(selectedPage.Name, targetPath);
        UpdateOverlayVisibility();
        StatusText.Text = "Overlay ready over GW2 and Taco Stuffer. Other active applications hide it.";
    }

    private void ShowGameTab(object sender, RoutedEventArgs e)
    {
        if (projectFile == null)
        {
            Notice("No project", "Create or load a project first.");
            return;
        }
        EnterInAppLayout();
    }

    private void ResetLayout(object sender, RoutedEventArgs e)
    {
        LeftColumn.Width = new GridLength(320);
        RightColumn.Width = new GridLength(380);
        if (pages.Count > 0) OpenTab(pages[0].Tab, Documents);
    }

    private void NewProject(object sender, RoutedEventArgs e) => CreateProject();

    private void ImportTaco(object sender, RoutedEventArgs e) => ImportPack();

    private void BuildPreview(object sender, RoutedEventArgs e)
    {
        if (pages.Count == 0)
        {
            Notice("No XML documents", "Add an XML document before compiling the pack.");
            return;
        }
        // Include the current endpoint even between density-sampled points.
        // Building does not stop recording or create a new segment.
        FlushTrailSample();
        if (!SaveCurrentProject() || projectFile == null) return;
        string output;
        try
        {
            output = ProjectStore.ExportPath(projectFile, CurrentSnapshot());
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        }
        catch (Exception ex) when (ProjectError(ex))
        {
            Notice("Export location", ex.Message);
            return;
        }
        try
        {
            var result = TestPackBuilder.Build(output, pages.Select(p => (p.Name, p.FormattedXml)).ToList(), projectFolder, editableTrails, importedAssets, assetPaths);
            StatusText.Text = $"Test .taco built: {result.XmlFiles} XMLs, {result.Assets} assets, {result.Placeholders.Count} placeholders.";
            UpdateBuildStatus();
            Notice("Test Taco stuffed", output + "\n\n" + result.XmlFiles + " XML documents and " + result.Assets + " assets.\n" + result.Placeholders.Count + " placeholder files generated.\n\nThe archive includes a test-build report. Missing images use a magenta PNG; missing trails are empty TRL headers, not recorded routes. Editable trails compile to TRL; their project sources stay editable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException or ArgumentException)
        {
            Notice("Test build failed", ex.Message);
        }
    }

    private void Notice(string title, string message)
    {
        new NoticeWindow(title, message) { Owner = this }.ShowDialog();
    }

    private void HeaderDrag(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase) return;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : null;
        }
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindow(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
