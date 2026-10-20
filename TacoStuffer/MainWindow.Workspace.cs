using System.IO;
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
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private readonly Dictionary<TabItem, DetachedDocumentWindow> detachedTabs = [];
    private readonly DispatcherTimer sourceSelectionTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Dictionary<string, string> textureLabels = new(StringComparer.OrdinalIgnoreCase);
    private bool selectingFromCode;
    private bool selectingFromExplorer;
    private string? draggingCategoryPath;
    private Point categoryDragOrigin;
    private readonly List<string> assetExtensions = [".png", ".jpg", ".jpeg", ".dds", ".bmp", ".tga"];
    private void InitializeWorkspace()
    {
        SetTabHeader(GameTab, "Game");
        ComparisonDocuments.SelectionChanged += DocumentChanged;
        AddTab.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            CreateXmlPage();
        };
        sourceSelectionTimer.Tick += (_, _) =>
        {
            sourceSelectionTimer.Stop();
            SelectFromXml();
        };
        Closed += (_, _) =>
        {
            sourceSelectionTimer.Stop();
            foreach (var host in detachedTabs.Values.ToList()) host.Close();
        };
    }

    private void WireEditorSelection(XmlPage page)
    {
        SetTabHeader(page.Tab, page.Name);
        page.Tab.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2)
            {
                e.Handled = true;
                RenameXml(page);
            }
        };
        page.Editor.SelectionChanged += (_, _) =>
        {
            if (updating || selectingFromExplorer || selectingFromCode || !page.Editor.IsKeyboardFocusWithin) return;
            sourceSelectionTimer.Stop();
            sourceSelectionTimer.Start();
        };
    }

    private TabControl? TabOwner(TabItem tab) => Documents.Items.Contains(tab) ? Documents : ComparisonDocuments.Items.Contains(tab) ? ComparisonDocuments : null;

    private void SetTabHeader(TabItem tab, string label)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(9, 0, 0, 0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, ToolTip = "Close view; retain document" };
        close.Click += (_, e) =>
        {
            e.Handled = true;
            CloseTab(tab);
        };
        header.Children.Add(close);
        tab.Header = header;
        var menu = new ContextMenu();
        void Add(string name, Action action)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        var xmlPage = pages.FirstOrDefault(p => p.Tab == tab);
        if (xmlPage != null)
        {
            Add("Rename XML… (F2)", () => RenameXml(xmlPage));
            Add("Delete XML…", () =>
            {
                var row = RevealRow(xmlPage.Name, xmlPage.Name);
                if (row != null)
                {
                    FocusRow(row);
                    DeleteXmlSelection();
                }
            });
        }
        var codePage = codePages.FirstOrDefault(p => p.Tab == tab);
        if (codePage != null)
        {
            Add("Rename document… (F2)", () => RenameCodeDocument(codePage));
            Add("Delete document…", () => DeleteCodeDocuments([codePage]));
        }
        Add("Close tab", () => CloseTab(tab));
        if (tab == GameTab)
        {
            tab.ContextMenu = menu;
            return;
        }
        Add("Move to right comparison pane", () => OpenTab(tab, ComparisonDocuments));
        Add("Move to main pane", () => OpenTab(tab, Documents));
        Add("Detach to separate window", () => DetachTab(tab));
        tab.ContextMenu = menu;
        Point origin = default;
        bool armed = false;
        header.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Ancestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource) != null) return;
            origin = e.GetPosition(this);
            armed = true;
            var owner = TabOwner(tab);
            if (owner != null) owner.SelectedItem = tab;
            header.CaptureMouse();
        };
        header.PreviewMouseMove += (_, e) =>
        {
            if (!armed || e.LeftButton != MouseButtonState.Pressed) return;
            Point point = e.GetPosition(this);
            if (Math.Abs(point.Y - origin.Y) < 45) return;
            armed = false;
            header.ReleaseMouseCapture();
            DetachTab(tab);
            e.Handled = true;
        };
        header.PreviewMouseLeftButtonUp += (_, _) =>
        {
            armed = false;
            if (header.IsMouseCaptured) header.ReleaseMouseCapture();
        };
    }

    private void OpenTab(TabItem tab, TabControl? destination = null, bool activateDetached = true)
    {
        if (inAppLayout && tab != GameTab && tab != AddTab)
        {
            OpenHostedDocument(tab);
            return;
        }
        if (detachedTabs.TryGetValue(tab, out var host))
        {
            if (destination == null)
            {
                documentUse[tab] = ++documentUseNumber;
                if (activateDetached)
                {
                    host.Show();
                    host.Activate();
                }
                return;
            }
            host.Close();
        }
        bool wasOpen = ViewOpen(tab);
        PrepareDocumentView(tab);
        destination ??= TabOwner(tab) ?? Documents;
        bool prior = updating;
        updating = true;
        try
        {
            var owner = TabOwner(tab);
            if (owner != null && owner != destination) owner.Items.Remove(tab);
            tab.Visibility = Visibility.Visible;
            if (!destination.Items.Contains(tab))
            {
                if (destination == Documents) destination.Items.Insert(Documents.Items.IndexOf(AddTab), tab);
                else destination.Items.Add(tab);
            }
            destination.SelectedItem = tab;
            UpdateComparisonLayout();
        }
        finally
        {
            updating = prior;
        }
        if (!wasOpen) QueueViewRefresh();
    }

    private void CloseTab(TabItem tab)
    {
        if (tab == AddTab) return;
        if (tab == GameTab && !ReturnGameToDesktop()) return;
        if (detachedTabs.TryGetValue(tab, out var host))
        {
            host.Close();
            UnloadEditor(tab);
            QueueViewRefresh();
            return;
        }
        bool prior = updating;
        updating = true;
        try
        {
            TabOwner(tab)?.Items.Remove(tab);
            UpdateComparisonLayout();
        }
        finally
        {
            updating = prior;
        }
        UnloadEditor(tab);
        QueueViewRefresh();
    }

    private void UpdateComparisonLayout()
    {
        bool open = !inAppLayout && ComparisonDocuments.Items.Count > 0;
        CompareColumn.Width = open ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        CompareSplitterColumn.Width = new GridLength(open ? 8 : 0);
        CompareSplitter.Visibility = ComparisonDocuments.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DetachTab(TabItem tab)
    {
        if (tab == GameTab || tab == AddTab || detachedTabs.ContainsKey(tab) || tab.Content is not UIElement body) return;
        CloseTab(tab);
        PrepareDocumentView(tab);
        tab.Content = null;
        string label = pages.FirstOrDefault(p => p.Tab == tab)?.Name ?? "Game";
        DetachedDocumentWindow? window = null;
        window = new DetachedDocumentWindow(label, body, () =>
        {
            window!.Close();
            OpenTab(tab, Documents);
        }) { Owner = this };
        detachedTabs[tab] = window;
        window.PreviewKeyDown += ProjectShortcut;
        window.PreviewMouseDown += (_, _) => FlushHistoryCheckpoint();
        window.Closed += (_, _) =>
        {
            tab.Content = window.ReleaseDocument();
            detachedTabs.Remove(tab);
            UnloadEditor(tab);
            QueueViewRefresh();
        };
        window.Activated += (_, _) => documentUse[tab] = ++documentUseNumber;
        window.Show();
    }

    private void SelectFromXml()
    {
        if (updating || selectingFromExplorer || selectingFromCode) return;
        var page = pages.FirstOrDefault(p => p.Editor.IsKeyboardFocusWithin);
        if (page == null) return;
        string source = PageSource(page);
        try
        {
            if (xmlTimer.IsEnabled)
            {
                xmlTimer.Stop();
                if (!TrySyncXml()) return;
            }
            int offset = new TextRange(page.Editor.Document.ContentStart, page.Editor.Selection.Start).Text.Length;
            var node = page.SourceElement(source, offset);
            if (node == null) return;
            string key = ElementKey(node);
            var row = RevealRow(page.Name, key);
            if (row == null) return;
            selectingFromCode = true;
            bool prior = updating;
            updating = true;
            try
            {
                ElementsList.SelectedItems.Clear();
                ElementsList.SelectedItems.Add(row);
                ElementsList.ScrollIntoView(row);
                selected = row;
            }
            finally
            {
                updating = prior;
            }
            ShowAttributes();
            StatusText.Text = "XML selection → Elements and Attributes";
        }
        catch (XmlException)
        { /* Incomplete typing keeps the last valid selection. */
        }
        finally
        {
            selectingFromCode = false;
        }
    }

    private static T? Ancestor<T>(object? source) where T : DependencyObject
    {
        DependencyObject? current = source as DependencyObject;
        while (current != null)
        {
            if (current is T found) return found;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : current is FrameworkContentElement content ? content.Parent : null;
        }
        return null;
    }

    private void FocusRow(ExplorerRow row, bool highlightXml = true, bool activateDetached = true)
    {
        row = RevealRow(row.DocumentName, row.Key) ?? row;
        bool prior = updating;
        updating = true;
        try
        {
            ElementsList.SelectedItems.Clear();
            ElementsList.SelectedItems.Add(row);
            ElementsList.ScrollIntoView(row);
            selected = row;
        }
        finally
        {
            updating = prior;
        }
        SyncOverlaySelection();
        if (row.Kind == "texture")
        {
            ShowTextureAttributes(row);
            return;
        }
        var page = pages.FirstOrDefault(p => p.Name == row.DocumentName);
        if (page != null) OpenTab(page.Tab, activateDetached: activateDetached);
        if (row.Element == null)
        {
            AttributesHost.Children.Clear();
            SelectionTitle.Text = page?.Name ?? row.Label;
            CategoryPath.Text = "";
        }
        if (row.Element != null)
        {
            ShowAttributes();
            if (highlightXml && page != null)
            {
                selectingFromExplorer = true;
                try
                {
                    SelectElementText(page.Editor, row.Element);
                }
                finally
                {
                    selectingFromExplorer = false;
                }
            }
        }
    }

    private void CategoryDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        var node = Ancestor<TreeViewItem>(e.OriginalSource);
        draggingCategoryPath = node?.Tag is ExplorerRow row && row.Element != null ? CategoryFullPath(row.Element) : null;
        categoryDragOrigin = e.GetPosition(CategoryTree);
    }

    private void CategoryDragMove(object sender, MouseEventArgs e)
    {
        if (draggingCategoryPath == null || e.MiddleButton != MouseButtonState.Pressed) return;
        Point at = e.GetPosition(CategoryTree);
        if (Math.Abs(at.X - categoryDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - categoryDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        string path = draggingCategoryPath;
        draggingCategoryPath = null;
        try
        {
            DragDrop.DoDragDrop(CategoryTree, new DataObject("TacoStuffer.Category", path), DragDropEffects.Move);
        }
        finally
        {
            ClearDropGuide();
        }
    }

    private void CategoryDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        draggingCategoryPath = null;
        ClearDropGuide();
    }

    private (string Path, CategoryDropPosition Position)? DropTarget(DragEventArgs e)
    {
        var node = Ancestor<TreeViewItem>(e.OriginalSource);
        if (node?.Tag is not ExplorerRow row || row.Element == null) return null;
        var header = node.Header as FrameworkElement;
        if (header == null) return (CategoryFullPath(row.Element), CategoryDropPosition.Child);
        double y = e.GetPosition(header).Y;
        double height = Math.Max(header.ActualHeight, 20);
        var position = y < height * .25 ? CategoryDropPosition.Before : y > height * .75 ? CategoryDropPosition.After : CategoryDropPosition.Child;
        return (CategoryFullPath(row.Element), position);
    }

    private void CategoryDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        ClearDropGuide();
        if (e.Data.GetData("TacoStuffer.Category") is not string source || DropTarget(e) is not { } target) return;
        if (source == target.Path || target.Path.StartsWith(source + '.', StringComparison.Ordinal) || !source.Contains('.')) return;
        if (target.Position != CategoryDropPosition.Child && !target.Path.Contains('.')) return;
        var node = Ancestor<TreeViewItem>(e.OriginalSource);
        if (node?.Header is FrameworkElement header) ShowDropGuide(header, target.Position);
        e.Effects = DragDropEffects.Move;
        StatusText.Text = $"Move {source} · {target.Position} · {target.Path}";
    }

    private void CategoryDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropGuide();
        if (e.Data.GetData("TacoStuffer.Category") is not string source || DropTarget(e) is not { } target || !TrySyncXml()) return;
        try
        {
            var changed = CategoryOperations.Move(pages.Select(p => p.Xml).ToList(), source, target.Path, target.Position, out string path, categoryOrder);
            categoryOrder = CategoryOrder.Move(categoryOrder, source, path, target.Path, target.Position);
            for (int i = 0; i < pages.Count; i++) pages[i].Xml = changed[i];
            if (selected?.Element?.Name == "MarkerCategory") selected = selected with { Key = "category:" + path };
            WritePages();
            var row = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.Key == "category:" + path);
            if (row != null) FocusRow(row);
            StatusText.Text = "Category moved in memory: " + path;
        }
        catch (InvalidOperationException ex)
        {
            Notice("Category move", ex.Message);
        }
    }

    private void ExplorerKeyDown(object sender, KeyEventArgs e)
    {
        if (selected == null) return;
        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            DeleteSelection();
        }
        else if (e.Key == Key.F2 && selected.Kind is not ("point" or "segment"))
        {
            e.Handled = true;
            QuickRename(selected);
        }
        else if (selected.HasChildren && ((e.Key == Key.Left && selected.Expanded) || (e.Key == Key.Right && !selected.Expanded)))
        {
            e.Handled = true;
            ToggleHierarchyRow(selected);
        }
    }

    private void CategoryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && CategoryTree.SelectedItem is TreeViewItem { Tag: ExplorerRow row })
        {
            e.Handled = true;
            QuickRename(row);
        }
    }

    private void AddCategoryContext(TreeViewItem node, ExplorerRow row)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename… (F2)" };
        if (row.Element != null && Value(row.Element, "copy").Length > 0)
        {
            var copy = new MenuItem { Header = "Test Copy (category action)" };
            copy.Click += (_, _) => TestElementCopy(row.Element);
            menu.Items.Add(copy);
        }
        rename.Click += (_, _) => QuickRename(row);
        menu.Items.Add(rename);
        var child = new MenuItem { Header = "New child category…" };
        child.Click += (_, _) => AddChildCategory(row);
        menu.Items.Add(child);
        var delete = new MenuItem { Header = "Delete category…" };
        delete.Click += (_, _) =>
        {
            FocusRow(row, false);
            DeleteSelection();
        };
        menu.Items.Add(delete);
        node.ContextMenu = menu;
    }

    private void AddChildCategory(ExplorerRow row)
    {
        if (!TrySyncXml()) return;
        string path = row.Element == null ? "" : CategoryFullPath(row.Element);
        var page = pages.FirstOrDefault(p => p.Name == row.DocumentName);
        var parent = page?.Xml.Descendants("MarkerCategory").FirstOrDefault(c => CategoryFullPath(c) == path);
        if (parent == null) return;
        var dialog = new RenameWindow("New category", "newcategory", "New Category") { Owner = DeleteDialogOwner, Topmost = DeleteDialogOwner.Topmost };
        if (dialog.ShowDialog() != true) return;
        if (!ValidLeaf(dialog.ItemName) || Categories().ContainsKey(path + "." + dialog.ItemName))
        {
            Notice("Category name", "Use a unique category name without dots.");
            return;
        }
        parent.Add(new XElement("MarkerCategory", new XAttribute("name", dialog.ItemName), new XAttribute("DisplayName", dialog.DisplayName)));
        WritePages();
        var added = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.DocumentName == page!.Name && r.Key == "category:" + path + "." + dialog.ItemName);
        if (added != null) FocusRow(added);
    }

    private void AddCategory(object sender, RoutedEventArgs e)
    {
        if (!TrySyncXml()) return;
        var page = pages.FirstOrDefault(p => p.Name == selected?.DocumentName)
            ?? pages.FirstOrDefault(p => p.Tab == Documents.SelectedItem || p.Tab == ComparisonDocuments.SelectedItem)
            ?? pages.FirstOrDefault();
        if (page == null) return;
        XElement? parent = selected?.Element?.Name == "MarkerCategory" ? selected.Element : null;
        if (parent == null && selected?.Element != null)
        {
            string path = Value(selected.Element, "type");
            parent = page.Xml.Descendants("MarkerCategory").FirstOrDefault(c => CategoryFullPath(c) == path);
        }
        parent ??= page.Xml.Root!.Elements("MarkerCategory").FirstOrDefault();
        if (parent == null)
        {
            Notice("New category", "Add a pack heading to this XML first.");
            return;
        }
        AddChildCategory(new ExplorerRow("", page.Name, parent, 0, "category", "category:" + CategoryFullPath(parent)));
    }

    private static bool ValidLeaf(string name)
    {
        try
        {
            XmlConvert.VerifyNCName(name);
            return !name.Contains('.');
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private void RenameOverlayAsset(OverlayAsset asset)
    {
        var owner = overlay;
        if (owner == null || workspaceBusy || asset.Kind is not ("POI" or "Trail")) return;
        FlushHistoryCheckpoint();
        if (!TrySyncXml()) return;
        var element = pages.FirstOrDefault(p => p.Name == asset.DocumentName)?.Xml.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == asset.Kind && ElementKey(e) == asset.Key);
        if (element == null) return;
        // The overlay owns the modal dialog so names can be edited without opening the editor.
        var dialog = new RenameWindow("Rename " + (asset.Kind == "POI" ? "marker" : "trail"),
            Value(element, "name", AssetStem(element)), Value(element, "DisplayName", AssetStem(element)))
        { Owner = owner, Topmost = owner.Topmost };
        if (dialog.ShowDialog() != true || !TrySyncXml()) return;
        // Synchronization may replace XML elements. Resolve the same identity again before writing.
        element = pages.FirstOrDefault(p => p.Name == asset.DocumentName)?.Xml.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == asset.Kind && ElementKey(e) == asset.Key);
        if (element == null) return;
        SetValue(element, "name", dialog.ItemName);
        SetValue(element, "DisplayName", dialog.DisplayName);
        WritePages();
        QueueHistoryCheckpoint("Rename " + (asset.Kind == "POI" ? "marker" : "trail"));
    }

    private void QuickRename(ExplorerRow row)
    {
        if (row.Kind == "code")
        {
            var code = codePages.FirstOrDefault(p => p.Name == row.DocumentName);
            if (code != null) RenameCodeDocument(code);
            return;
        }
        if (row.Kind == "document")
        {
            var page = pages.FirstOrDefault(p => p.Name == row.DocumentName);
            if (page != null) RenameXml(page);
            return;
        }
        SyncOverlaySelection();
        if (row.Kind == "texture")
        {
            RenameTexture(row);
            return;
        }
        if (row.Element == null || !TrySyncXml()) return;
        var actual = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.DocumentName == row.DocumentName && r.Key == row.Key);
        if (actual?.Element == null) return;
        selected = actual;
        var dialog = new RenameWindow("Rename " + actual.Kind, Value(actual.Element, "name", AssetStem(actual.Element)), Value(actual.Element, "DisplayName", AssetStem(actual.Element))) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (actual.Kind == "MarkerCategory")
        {
            if (!ValidLeaf(dialog.ItemName))
            {
                Notice("Invalid name", "Use a category name without dots.");
                return;
            }
            string old = CategoryFullPath(actual.Element);
            string newPath = old.Contains('.') ? old[..(old.LastIndexOf('.') + 1)] + dialog.ItemName : dialog.ItemName;
            if (old != newPath && Categories().ContainsKey(newPath))
            {
                Notice("Duplicate name", "That category already exists.");
                return;
            }
            RenameCategory(dialog.ItemName);
            RenameDisplayName(dialog.DisplayName);
        }
        else
        {
            SetValue(actual.Element, "name", dialog.ItemName);
            SetValue(actual.Element, "DisplayName", dialog.DisplayName);
            WritePages();
        }
    }

    private void RenameXml(XmlPage page)
    {
        if (!TrySyncXml()) return;
        var dialog = new XmlRenameWindow(page.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.FileName;
        if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) name += ".xml";
        string stem = Path.GetFileNameWithoutExtension(name);
        string device = stem.Split('.')[0].ToUpperInvariant();
        bool reserved = device is "CON" or "PRN" or "AUX" or "NUL" || (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] is >= '1' and <= '9');
        if (string.IsNullOrWhiteSpace(stem) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || stem.EndsWith('.') || stem.EndsWith(' ') || reserved)
        {
            Notice("Invalid XML filename", "Use a valid Windows filename without folders or a reserved device name.");
            return;
        }
        if (pages.Any(p => p != page && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Notice("Duplicate XML filename", "Another XML already uses this name, including closed documents.");
            return;
        }
        string oldName = page.Name;
        var collapsed = collapsedElements.Select(k => k.StartsWith(oldName + "|", StringComparison.Ordinal) ? name + k[oldName.Length..] : k).ToList();
        collapsedElements.Clear();
        foreach (var key in collapsed) collapsedElements.Add(key);
        page.Name = name;
        SetTabHeader(page.Tab, name);
        if (detachedTabs.TryGetValue(page.Tab, out var host)) host.RenameDocument(name);
        selected = new ExplorerRow("▤ " + name, name, null, 0, "document", name);
        RefreshExplorer();
        var row = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.Kind == "document" && r.DocumentName == name);
        if (row != null) FocusRow(row, false);
        StatusText.Text = "XML renamed to " + name + ". The next test .taco build uses this filename.";
    }

    private static string AssetStem(XElement element) => element.Name.LocalName == "MarkerCategory" ? CategoryName(element) : Path.GetFileNameWithoutExtension(Value(element, element.Name.LocalName == "Trail" ? "trailData" : "iconFile", element.Name.LocalName));

    private readonly Dictionary<string, (DateTime Time, List<string> Paths)> assetPickerCache = [];
    private List<string> TexturePaths(string attribute)
    {
        if (projectFolder == null) return [];
        string folder = attribute.Equals("iconFile", StringComparison.OrdinalIgnoreCase) ? assetPaths.MarkerIcons : assetPaths.TrailTextures;
        string key = projectFolder + "|" + folder;
        if (assetPickerCache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromSeconds(30)) return new(cached.Paths);
        try
        {
            var paths = ProjectAssetPaths.Files(projectFolder, folder).Where(p => assetExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())).Select(p => Path.GetRelativePath(projectFolder, p).Replace('\\', '/')).OrderBy(p => p).ToList();
            assetPickerCache[key] = (DateTime.UtcNow, paths);
            return new(paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }
    }

    private void AssetField(string name, string current)
    {
        AttributesHost.Children.Add(new TextBlock { Text = name, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 6, 0, 4) });
        var paths = TexturePaths(name);
        if (!paths.Contains(current, StringComparer.OrdinalIgnoreCase) && current.Length > 0) paths.Insert(0, current);
        var picker = new ComboBox { ItemsSource = paths, SelectedItem = paths.FirstOrDefault(p => p.Equals(current, StringComparison.OrdinalIgnoreCase)), ToolTip = "Configured project folder and subfolders. Existing XML paths remain available." };
        picker.SelectionChanged += (_, _) =>
        {
            if (!rebuildingAttributes && !updating && picker.SelectedItem is string path && path != current) ChangeAttribute(name, path);
        };
        AttributesHost.Children.Add(picker);
        var import = new Button { Content = "Import Textures/Icons…", Margin = new Thickness(0, 8, 0, 0) };
        import.Click += (_, _) => ImportImages(name.Equals("iconFile", StringComparison.OrdinalIgnoreCase) ? assetPaths.MarkerIcons : assetPaths.TrailTextures);
        AttributesHost.Children.Add(import);
    }

    private void ImportTextures(object sender, RoutedEventArgs e)
    {
        if (projectFile == null)
        {
            Notice("No project", "Create or load a project before importing textures.");
            return;
        }
        string destination = assetPaths.MarkerIcons;
        if (!destination.Equals(assetPaths.TrailTextures, StringComparison.OrdinalIgnoreCase))
        {
            var chooser = new ImageDestinationWindow(assetPaths) { Owner = this };
            if (chooser.ShowDialog() != true || chooser.Folder == null) return;
            destination = chooser.Folder;
        }
        ImportImages(destination);
    }

    private void ImportImages(string destination)
    {
        if (projectFile == null)
        {
            Notice("No project", "Create or load a project before importing textures.");
            return;
        }
        var pick = new OpenFileDialog { Title = "Import Textures/Icons", Multiselect = true, Filter = "Images (*.png;*.jpg;*.jpeg;*.dds;*.bmp;*.tga)|*.png;*.jpg;*.jpeg;*.dds;*.bmp;*.tga|All files (*.*)|*.*" };
        if (pick.ShowDialog(this) != true) return;
        if (projectFolder == null)
        {
            var location = new OpenFolderDialog { Title = "Choose project folder; images will be copied into TEXTURES" };
            if (location.ShowDialog(this) != true) return;
            projectFolder = location.FolderName;
        }
        var errors = new List<string>();
        int count = 0;
        try
        {
            Directory.CreateDirectory(Path.Combine(projectFolder, destination));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice("Import failed", ex.Message);
            return;
        }
        foreach (string source in pick.FileNames)
        {
            try
            {
                string extension = Path.GetExtension(source);
                if (!assetExtensions.Contains(extension.ToLowerInvariant()))
                {
                    errors.Add(Path.GetFileName(source) + ": unsupported image extension");
                    continue;
                }
                string path = Path.Combine(projectFolder, destination, Path.GetFileName(source));
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    continue;
                }
                for (int i = 1; File.Exists(path); i++) path = Path.Combine(projectFolder, destination, Path.GetFileNameWithoutExtension(source) + "_" + i + extension);
                File.Copy(source, path, false);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(Path.GetFileName(source) + ": " + ex.Message);
            }
        }
        assetPickerCache.Clear();
        RefreshExplorer();
        StatusText.Text = $"Imported {count} texture/icon file(s) into {destination}. Existing files were preserved.";
        if (errors.Count > 0) Notice("Import results", string.Join("\n", errors));
    }

    private void ShowTextureAttributes(ExplorerRow row)
    {
        rebuildingAttributes = true;
        AttributesHost.Children.Clear();
        SelectionTitle.Text = textureLabels.GetValueOrDefault(row.Key, Path.GetFileName(row.Key));
        CategoryPath.Text = row.Key;
        Section("TEXTURE / ICON");
        AttributesHost.Children.Add(new TextBlock { Text = "Name renames the imported file and updates XML image references. DisplayName is an in-memory editor label until project persistence is added.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush") });
        var rename = new Button { Content = "Rename… (F2)", Margin = new Thickness(0, 12, 0, 0) };
        rename.Click += (_, _) => RenameTexture(row);
        AttributesHost.Children.Add(rename);
        Field("DisplayName", textureLabels.GetValueOrDefault(row.Key, Path.GetFileNameWithoutExtension(row.Key)), value =>
        {
            textureLabels[row.Key] = value;
            RefreshExplorer();
        });
        rebuildingAttributes = false;
    }

    private void RenameTexture(ExplorerRow row)
    {
        if (projectFolder == null || !TrySyncXml()) return;
        var dialog = new RenameWindow("Rename texture/icon", Path.GetFileName(row.Key), textureLabels.GetValueOrDefault(row.Key, Path.GetFileNameWithoutExtension(row.Key))) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.ItemName;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
        {
            Notice("Invalid filename", "Choose a valid image filename.");
            return;
        }
        string original = Path.GetFullPath(Path.Combine(projectFolder, row.Key.Replace('/', Path.DirectorySeparatorChar)));
        string destination = Path.Combine(Path.GetDirectoryName(original)!, name);
        if (!Path.HasExtension(name)) destination += Path.GetExtension(original);
        if (!Path.GetExtension(destination).Equals(Path.GetExtension(original), StringComparison.OrdinalIgnoreCase))
        {
            Notice("Image format", "Renaming does not convert image formats. Keep the existing extension.");
            return;
        }
        string path = Path.GetRelativePath(projectFolder, destination).Replace('\\', '/');
        try
        {
            if (!string.Equals(original, destination, StringComparison.Ordinal))
            {
                if (File.Exists(destination))
                {
                    Notice("Filename already exists", "Choose a different image filename.");
                    return;
                }
                File.Move(original, destination);
            }
            foreach (var a in pages.SelectMany(p => p.Xml.Descendants().Attributes()).Where(a => (a.Name.LocalName.Equals("iconFile", StringComparison.OrdinalIgnoreCase) || a.Name.LocalName.Equals("texture", StringComparison.OrdinalIgnoreCase)) && a.Value.Replace('\\', '/').Equals(row.Key, StringComparison.OrdinalIgnoreCase))) a.Value = path;
            textureLabels.Remove(row.Key);
            textureLabels[path] = dialog.DisplayName;
            selected = row with { Key = path };
            WritePages();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice("Rename failed", ex.Message);
        }
    }

    private static string? AttributeTypeError(IEnumerable<XDocument> documents)
    {
        foreach (var element in documents.SelectMany(d => d.Descendants()))
        {
            if (element.Name.LocalName == "POI" && element.Attributes().Any(a => a.Name.LocalName.Equals("texture", StringComparison.OrdinalIgnoreCase))) return "texture is a trail attribute. Use iconFile for a marker.";
            if (element.Name.LocalName == "Trail" && element.Attributes().Any(a => a.Name.LocalName.Equals("iconFile", StringComparison.OrdinalIgnoreCase))) return "iconFile is a marker attribute. Use texture for a trail.";
        }
        return null;
    }
}
