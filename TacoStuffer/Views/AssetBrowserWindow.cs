using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Windows.Threading;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

internal sealed class AssetBrowserWindow : PreviewDialog
{
    private readonly string root;
    private ProjectAssetPaths defaults;
    private readonly List<string> registeredFolders;
    private readonly Action<IReadOnlyList<string>> saveFolders;
    private readonly Action<string,string> setFolderDefault;
    private readonly Action<IReadOnlyList<string>> deleteAssets;
    private readonly Func<string,string,ProjectAssetPaths> renameFolder;
    private readonly Action reconcile;
    private readonly Func<string,string,ProjectAssetPaths> externalFolderRename;
    private readonly Queue<(string Source,string Destination)> folderRenames = new();
    private FileSystemWatcher? watcher;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private int operationDepth;
    private bool rebuildingFolders;
    private string defaultIcons, defaultTextures, defaultTrails;
    private readonly Action<IReadOnlyList<string>, string, bool> insertTrails;
    private readonly ComboBox category;
    private readonly Func<List<PathChoice>> getCategories;
    private readonly Func<string,bool> trackedPath;
    private readonly CheckBox makeEditable = new() { Content = "Make editable", IsChecked = true, VerticalAlignment = VerticalAlignment.Center,
        ToolTip = "Convert the TRL into editable trail segments and points. Unchecked keeps the original binary file as a reference." };
    private readonly ComboBox fileFilter = new() { MinWidth = 120, Margin = new Thickness(0,0,8,0) };
    private readonly Button select;
    private readonly StackPanel insertOptions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6,0,8,0) };
    private readonly bool choosing;
    private readonly Dictionary<string, TextBlock> folderStars = new(StringComparer.OrdinalIgnoreCase);
    private readonly TreeView folders = new();
    private readonly ListBox files = new() { SelectionMode = SelectionMode.Extended };
    private readonly TextBox search = new() { ToolTip = "Search asset filenames in this folder and its subfolders" };
    private readonly TextBlock location = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Action<IReadOnlyList<string>, string> moveAssets;
    private readonly Func<string, string, ProjectAssetPaths> moveFolder;
    private readonly Func<string, ProjectAssetPaths> deleteFolder;
    private string? draggingFolder;
    private Point folderDragStart;
    private string folder;
    private Point dragStart;
    private string? pressedAsset;
    private bool preserveSelection;
    private readonly Action<string> imported;
    public string? SelectedAsset { get; private set; }
    private sealed record Asset(string Path, string Name, long Stamp);
    public AssetBrowserWindow(string root, string initialFolder, bool choosing,
        Action<IReadOnlyList<string>, string> moveAssets, Action<string> imported, ProjectAssetPaths defaults, Func<string, string, ProjectAssetPaths> moveFolder, Func<string, ProjectAssetPaths> deleteFolder,
        List<PathChoice> categories, Action<IReadOnlyList<string>, string, bool> insertTrails, string? initialCategory,
        IReadOnlyList<string> registeredFolders, Action<IReadOnlyList<string>> saveFolders, Action<string,string> setFolderDefault,
        Action<IReadOnlyList<string>> deleteAssets, Func<string,string,ProjectAssetPaths> renameFolder, Action reconcile, Func<string,string,ProjectAssetPaths> externalFolderRename, Func<List<PathChoice>> getCategories, Func<string,bool> trackedPath) : base("Project Assets", 940)
    {
        this.root = root; this.defaults = defaults;
        this.registeredFolders = registeredFolders.ToList(); this.saveFolders = saveFolders;
        this.setFolderDefault = setFolderDefault; this.deleteAssets = deleteAssets; this.renameFolder = renameFolder; this.reconcile = reconcile; this.externalFolderRename = externalFolderRename;
        this.choosing = choosing; this.insertTrails = insertTrails;
        this.getCategories = getCategories; this.trackedPath = trackedPath;
        category = PathChoicePresentation.Combo(categories, initialCategory);
        category.MinWidth = 150; category.MaxWidth = 270; category.Margin = new Thickness(0,0,10,0);
        category.ToolTip = "Insert the selected TRL files into this project category.";
        if (category.SelectedItem == null && categories.Count > 0) category.SelectedIndex = 0;
        this.moveFolder = moveFolder; this.deleteFolder = deleteFolder;
        defaultIcons = defaults.MarkerIcons; defaultTextures = defaults.TrailTextures; defaultTrails = defaults.TrailFiles;
        this.imported = imported;
        this.moveAssets = moveAssets;
        folder = ProjectAssetPaths.Folder(initialFolder);
        Width = 940; Height = 680; MinWidth = 650; MinHeight = 460;
        SizeToContent = SizeToContent.Manual; ResizeMode = ResizeMode.CanResizeWithGrip;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0)
        });
        // Use a local tree template: selection is a pointer, never a system-color fill.
        var folderStyle = (Style)System.Windows.Markup.XamlReader.Parse(FolderStyle);
        folderStyle.BasedOn = (Style)FindResource(typeof(TreeViewItem));
        folders.Resources[typeof(TreeViewItem)] = folderStyle;
        // Replace the body's vertical stack with a stretching workspace.
        var dock = (DockPanel)((Border)Content).Child;
        dock.Children.RemoveAt(1);
        var workspace = new DockPanel { Margin = new Thickness(14) };
        dock.Children.Add(workspace);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); workspace.Children.Add(footer);
        select = new Button { Content = choosing ? "Select" : "Close", MinWidth = 95, IsDefault = true };
        DockPanel.SetDock(select, Dock.Right); footer.Children.Add(select);
        select.Click += (_, _) => Safely(PerformSelection);
        DockPanel.SetDock(insertOptions, Dock.Right); footer.Children.Add(insertOptions);
        insertOptions.Children.Add(category); insertOptions.Children.Add(makeEditable);
        insertOptions.Visibility = Visibility.Collapsed;
        footer.Children.Add(status);
        files.SelectionChanged += (_, _) => UpdateSelectionAction();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(top, Dock.Top); workspace.Children.Add(top);
        var import = new Button { Content = "Import…", Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(import, Dock.Right); top.Children.Add(import); import.Click += (_, _) => Safely(Import);
        fileFilter.ItemsSource = new[] { "All assets", "Images", "Trails (.trl)", "Documents" };
        fileFilter.SelectedIndex = choosing ? 1 : 0;
        DockPanel.SetDock(fileFilter, Dock.Left); top.Children.Add(fileFilter);
        fileFilter.SelectionChanged += (_, _) => RefreshFiles();
        var searchHost = new Grid();
        var placeholder = new TextBlock { Text = "Filter Search", Foreground = (Brush)FindResource("MutedBrush"), Opacity = .65,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        searchHost.Children.Add(search); searchHost.Children.Add(placeholder); top.Children.Add(searchHost);
        search.TextChanged += (_, _) => { placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RefreshFiles(); };
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new GridLength(235) }); layout.ColumnDefinitions.Add(new() { Width = new GridLength(6) }); layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); workspace.Children.Add(layout);
        var left = new DockPanel(); layout.Children.Add(left);
        var folderActions = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(folderActions, Dock.Bottom); left.Children.Add(folderActions);
        var create = new Button { Content = "New Folder…", Margin = new Thickness(0, 0, 0, 6) };
        folderActions.Children.Add(create); create.Click += (_, _) => Safely(CreateFolder);
        var addExisting = new Button { Content = "Add Existing Folder…", Margin = new Thickness(0,0,0,8) };
        addExisting.Click += (_, _) => Safely(AddExistingFolder); folderActions.Children.Add(addExisting);
        folderActions.Children.Add(new TextBlock { Text = "Right-click folders to make them default folder types.",
            Foreground = (Brush)FindResource("MutedBrush"), Opacity = .75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2,4,2,0) });
        left.Children.Add(folders); folders.SelectedItemChanged += (_, e) => { if (e.NewValue is TreeViewItem { Tag: string path }) { folder = path; if (!rebuildingFolders) RefreshFiles(); } };
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(splitter, 1); layout.Children.Add(splitter);
        var right = new DockPanel { Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(right, 2); layout.Children.Add(right);
        location.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(location, Dock.Top); right.Children.Add(location); right.Children.Add(files);
        var template = new DataTemplate(typeof(Asset));
        var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(Image.WidthProperty, 64d); image.SetValue(Image.HeightProperty, 64d); image.SetValue(Image.MarginProperty, new Thickness(6));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Path") { Converter = new ThumbnailConverter(root) }); row.AppendChild(image);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name")); name.SetBinding(TextBlock.ToolTipProperty, new System.Windows.Data.Binding("Path")); row.AppendChild(name); template.VisualTree = row; files.ItemTemplate = template;
        VirtualizingStackPanel.SetIsVirtualizing(files, true); VirtualizingStackPanel.SetVirtualizationMode(files, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(files, true);
        var fileMenu = new ContextMenu();
        var deleteFiles = new MenuItem { Header = "Delete selected assets…" };
        deleteFiles.Click += (_, _) => Safely(DeleteSelectedAssets); fileMenu.Items.Add(deleteFiles);
        var importDocuments = new MenuItem { Header = "Import selected documents into project" };
        importDocuments.Click += (_, _) => Safely(() => {
            foreach (var asset in files.SelectedItems.Cast<Asset>().Where(a => AssetLibrary.IsDocument(a.Path)).ToList()) imported(asset.Path);
            RefreshFolders();
            status.Text = "Documents imported into the Outliner. Save Project to retain them.";
        });
        fileMenu.Items.Add(importDocuments); files.ContextMenu = fileMenu;
        fileMenu.Opened += (_, _) => { deleteFiles.IsEnabled = files.SelectedItems.Count > 0;
            importDocuments.IsEnabled = files.SelectedItems.Cast<Asset>().Any(a => AssetLibrary.IsDocument(a.Path)); };
        files.PreviewMouseRightButtonDown += (_, e) => {
            if (Ancestor<ListBoxItem>(e.OriginalSource) is { } item && !item.IsSelected) { files.SelectedItems.Clear(); item.IsSelected = true; }
        };
        files.PreviewKeyDown += (_, e) => { if (e.Key == Key.Delete) { e.Handled = true; Safely(DeleteSelectedAssets); } };
        files.MouseDoubleClick += (_, e) => { if (Ancestor<ListBoxItem>(e.OriginalSource) != null && files.SelectedItem is Asset a && AssetLibrary.IsImage(a.Path) && (string)select.Content == "Select") Choose(); };
        files.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(files);
            var item = Ancestor<ListBoxItem>(e.OriginalSource);
            pressedAsset = (item?.DataContext as Asset)?.Path;
            preserveSelection = item?.IsSelected == true && files.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && e.ClickCount == 1;
            if (preserveSelection) e.Handled = true; // Keep the group intact until drag or mouse-up.
        };
        files.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (preserveSelection && pressedAsset != null)
            {
                var clicked = files.Items.Cast<Asset>().FirstOrDefault(a => a.Path == pressedAsset);
                files.SelectedItems.Clear(); files.SelectedItem = clicked;
            }
            pressedAsset = null; preserveSelection = false;
        };
        files.MouseMove += (_, e) =>
        {
            if (pressedAsset == null || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(files) - dragStart).Length < 6) return;
            var selected = files.SelectedItems.Cast<Asset>().Select(a => a.Path).ToArray();
            if (!selected.Contains(pressedAsset)) return;
            if (selected.Any(p => !AssetLibrary.IsImage(p) && !AssetLibrary.IsTrail(p))) { status.Text = "Move tracked documents from the Outliner."; return; }
            pressedAsset = null; preserveSelection = false;
            DragDrop.DoDragDrop(files, new DataObject("TacoStuffer.Assets", selected), DragDropEffects.Move);
        };
        folders.AllowDrop = true;
        folders.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            var item = Ancestor<TreeViewItem>(e.OriginalSource);
            draggingFolder = item?.Tag is string path && path != "." ? path : null;
            if (item != null) item.IsSelected = true;
            folderDragStart = e.GetPosition(folders); e.Handled = true;
        };
        folders.PreviewMouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) draggingFolder = null; };
        folders.MouseMove += (_, e) =>
        {
            if (draggingFolder == null || e.MiddleButton != MouseButtonState.Pressed || (e.GetPosition(folders) - folderDragStart).Length < 6) return;
            string source = draggingFolder; draggingFolder = null;
            DragDrop.DoDragDrop(folders, new DataObject("TacoStuffer.AssetFolder", source), DragDropEffects.Move);
        };
        folders.DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.None;
            if (Ancestor<TreeViewItem>(e.OriginalSource)?.Tag is string target)
            {
                if (e.Data.GetDataPresent("TacoStuffer.Assets")) e.Effects = DragDropEffects.Move;
                else if (e.Data.GetData("TacoStuffer.AssetFolder") is string source)
                    try { AssetFolders.Destination(root, source, target); e.Effects = DragDropEffects.Move; }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { status.Text = ex.Message; }
            }
            e.Handled = true;
        };
        folders.Drop += (_, e) =>
        {
            if (Ancestor<TreeViewItem>(e.OriginalSource)?.Tag is string target)
            {
                if (e.Data.GetData("TacoStuffer.Assets") is string[] paths)
                    Safely(() => { moveAssets(paths, target); RefreshFiles(); status.Text = "Assets moved; XML references updated. Save the project to retain the updated XML."; });
                else if (e.Data.GetData("TacoStuffer.AssetFolder") is string source)
                    Safely(() =>
                    {
                        string destination = AssetFolders.Destination(root, source, target);
                        UpdateDefaults(moveFolder(source, target));
                        RebaseRegistered(source,destination);
                        folder = AssetFolders.Relocate(folder, source, destination);
                        RefreshFolders(); status.Text = "Folder moved; references and default folders updated. Save the project.";
                    });
            }
            e.Handled = true;
        };
        refreshTimer.Tick += (_, _) => {
            refreshTimer.Stop();
            if (operationDepth > 0) { refreshTimer.Start(); return; }
            Safely(RefreshFromDisk);
        };
        void QueueRefresh() => Dispatcher.BeginInvoke(new Action(() => { if (watcher == null) return; refreshTimer.Stop(); refreshTimer.Start(); }));
        watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        watcher.Created += (_, _) => QueueRefresh(); watcher.Deleted += (_, _) => QueueRefresh();
        watcher.Changed += (_, _) => QueueRefresh();
        watcher.Renamed += (_, e) => Dispatcher.BeginInvoke(new Action(() => {
            if (watcher == null) return;
            if (Directory.Exists(e.FullPath))
            {
                string source = Path.GetRelativePath(root,e.OldFullPath).Replace('\\','/'), destination = Path.GetRelativePath(root,e.FullPath).Replace('\\','/');
                try { ProjectAssetPaths.Folder(source); ProjectAssetPaths.Folder(destination); folderRenames.Enqueue((source,destination)); }
                catch (InvalidOperationException) { }
            }
            QueueRefresh();
        }));
        watcher.Error += (_, _) => QueueRefresh(); watcher.EnableRaisingEvents = true;
        Closed += (_, _) => { watcher?.Dispose(); watcher = null; refreshTimer.Stop(); };
        Activated += (_, _) => Safely(RefreshFromDisk);
        Safely(RefreshFolders);
    }
    private static T? Ancestor<T>(object source) where T : DependencyObject
    {
        for (DependencyObject? p = source as DependencyObject; p != null; p = p is Visual ? VisualTreeHelper.GetParent(p) : null) if (p is T t) return t;
        return null;
    }
    private void Safely(Action action)
    {
        operationDepth++;
        try { action(); }
        catch (OperationCanceledException) { status.Text = "Operation cancelled."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or AggregateException or NotSupportedException or System.Xml.XmlException) { status.Text = ex.Message; }
        finally { operationDepth--; }
    }
    private void RefreshFromDisk()
    {
        while (folderRenames.TryPeek(out var move))
        {
            // Renames initiated here were already rebased by the command callback.
            if (defaults.All.Concat(registeredFolders).Append(folder).Any(p => AssetFolders.Contains(move.Source,p))
                || defaults.All.Concat(registeredFolders).Where(p => p != ".").Any(p => AssetFolders.Contains(p,move.Source)))
            {
                UpdateDefaults(externalFolderRename(move.Source,move.Destination));
                RebaseRegistered(move.Source,move.Destination);
                folder = AssetFolders.Relocate(folder,move.Source,move.Destination);
            }
            folderRenames.Dequeue();
        }
        reconcile(); RefreshFolders();
    }
    private void RefreshFolders()
    {
        folder = AssetFolders.Existing(root, folder);
        var choices = getCategories();
        if (!category.Items.Cast<PathChoice>().SequenceEqual(choices))
        {
            string? selectedCategory = category.SelectedValue as string;
            category.ItemsSource = choices; category.SelectedValue = selectedCategory;
            if (category.SelectedItem == null && choices.Count > 0) category.SelectedIndex = 0;
        }
        rebuildingFolders = true;
        try
        {
        var visible = defaults.All.Concat(registeredFolders).Where(p => p != ".").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Remember(TreeViewItem item) { if (item.IsExpanded && item.Tag is string path) expanded.Add(path); foreach (TreeViewItem child in item.Items) Remember(child); }
        foreach (TreeViewItem item in folders.Items) Remember(item);
        folderStars.Clear();
        folders.Items.Clear();
        TreeViewItem Build(string relative)
        {
            var item = new TreeViewItem { Header = relative == "." ? "Project" : Path.GetFileName(relative), Tag = relative, IsExpanded = relative == "." || expanded.Contains(relative) || folder.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase) };
            var header = new Grid();
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var star = new TextBlock { Text = "★", Foreground = new SolidColorBrush(Color.FromRgb(102, 217, 140)), Margin = new Thickness(6, 0, 0, 0) };
            folderStars[relative] = star; Grid.SetColumn(star,1); header.Children.Add(star);
            var folderName = new TextBlock { Text = relative == "." ? "Project" : Path.GetFileName(relative) };
            header.Children.Add(folderName);
            item.Header = header;
            item.PreviewMouseRightButtonDown += (_, e) => { if (Ancestor<TreeViewItem>(e.OriginalSource) == item) { item.IsSelected = true; e.Handled = true; } };
            var menu = new ContextMenu();
            var rename = new MenuItem { Header = "Rename Folder…", IsEnabled = relative != "." };
            rename.Click += (_, _) => Safely(() => RenameFolder(relative)); menu.Items.Add(rename);
            var delete = new MenuItem { Header = "Delete Folder…", IsEnabled = relative != "." };
            delete.Click += (_, _) => Safely(() => DeleteFolder(relative)); menu.Items.Add(delete);
            var makeDefault = new MenuItem { Header = "Make Default" }; menu.Items.Add(makeDefault);
            foreach (string role in new[] { "Core XML", "Docs", "Scripts", "Icons (markers)", "Textures (trails)", "Trail Data" })
            {
                string selectedRole = role;
                var option = new MenuItem { Header = role };
                option.Click += (_, _) => Safely(() => SetFolderDefault(selectedRole,relative)); makeDefault.Items.Add(option);
            }
            item.ToolTip = "Middle-click drag to move this folder inside another folder. Drop on Project to move it to the root.";
            item.ContextMenu = menu;
            foreach (var path in Directory.EnumerateDirectories(AssetLibrary.Resolve(root, relative)).OrderBy(Path.GetFileName))
            {
                string child = Path.GetRelativePath(root, path).Replace('\\', '/');
                try { ProjectAssetPaths.Folder(child); AssetLibrary.Resolve(root, child); }
                catch (InvalidOperationException) { continue; }
                if (!Path.GetFileName(path).StartsWith('.') && visible.Any(p => AssetFolders.Contains(p,child) || AssetFolders.Contains(child,p))) item.Items.Add(Build(child));
            }
            if (relative.Equals(folder, StringComparison.OrdinalIgnoreCase)) item.IsSelected = true;
            return item;
        }
        folders.Items.Add(Build(".")); RefreshStars();
        } finally { rebuildingFolders = false; }
        RefreshFiles();
    }
    private void UpdateDefaults(ProjectAssetPaths paths)
    {
        defaults = paths;
        defaultIcons = paths.MarkerIcons; defaultTextures = paths.TrailTextures; defaultTrails = paths.TrailFiles;
    }
    private void DeleteFolder(string path)
    {
        int count = AssetFolders.Files(root, path).Count;
        var confirm = new ConfirmActionWindow("Delete asset folder", "Delete " + path + " and its " + count + " file(s)? Windows will be asked to move them to the Recycle Bin. Tracked documents will leave the Outliner. References to removed assets will need replacement.") { Owner = this, Topmost = true };
        if (confirm.ShowDialog() != true) return;
        UpdateDefaults(deleteFolder(path));
        folder = AssetFolders.Existing(root, folder);
        RefreshFolders(); status.Text = "Folder deleted. Save the project to retain updated defaults.";
    }
    private void UpdateSelectionAction()
    {
        bool trails = AssetFolders.Contains(defaultTrails,folder) && files.SelectedItems.Count > 0 && files.SelectedItems.Cast<Asset>().All(a => AssetLibrary.IsTrail(a.Path));
        bool images = choosing && (AssetFolders.Contains(defaultIcons,folder) || AssetFolders.Contains(defaultTextures,folder));
        select.Content = trails ? "Insert" : images ? "Select" : "Close";
        insertOptions.Visibility = trails ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PerformSelection()
    {
        var selected = files.SelectedItems.Cast<Asset>().ToList();
        if (insertOptions.Visibility == Visibility.Visible)
        {
            if (selected.Any(a => !AssetLibrary.IsTrail(a.Path))) throw new InvalidOperationException("Select TRL files only to insert trails.");
            if (category.SelectedValue is not string target || target.Length == 0) throw new InvalidOperationException("Choose a project category before inserting trails.");
            insertTrails(selected.Select(a => a.Path).ToList(), target, makeEditable.IsChecked == true);
            status.Text = "Inserted " + selected.Count + " trail(s). Save the project to retain them.";
            files.SelectedItems.Clear();
        }
        else if ((string)select.Content == "Select") Choose(); else Close();
    }
    private void RefreshStars()
    {
        foreach (var pair in folderStars)
        {
            bool icons = pair.Key.Equals(defaultIcons, StringComparison.OrdinalIgnoreCase);
            bool textures = pair.Key.Equals(defaultTextures, StringComparison.OrdinalIgnoreCase);
            bool trails = pair.Key.Equals(defaultTrails, StringComparison.OrdinalIgnoreCase);
            bool docs = pair.Key.Equals(defaults.Docs,StringComparison.OrdinalIgnoreCase), scripts = pair.Key.Equals(defaults.Scripts,StringComparison.OrdinalIgnoreCase), xml = pair.Key.Equals(defaults.CoreXml,StringComparison.OrdinalIgnoreCase);
            pair.Value.Visibility = icons || textures || trails || docs || scripts || xml ? Visibility.Visible : Visibility.Hidden;
            pair.Value.ToolTip = "Default folder for " + string.Join(", ", new[] { icons ? "icons" : null, textures ? "textures" : null, trails ? "trail data" : null, docs ? "docs" : null, scripts ? "scripts" : null, xml ? "core XML" : null }.OfType<string>());
        }
    }
    private const string FolderStyle = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TreeViewItem">
          <Setter Property="Foreground" Value="#E6EBF3"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TreeViewItem">
            <Grid Background="Transparent"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
              <Grid.ColumnDefinitions><ColumnDefinition Width="18"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
              <ToggleButton x:Name="Expander" IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Focusable="False" Width="18" Height="24">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"><TextBlock x:Name="Glyph" Text="▸" Foreground="#9BA8BA" VerticalAlignment="Center" HorizontalAlignment="Center"/></Border>
                  <ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Glyph" Property="Text" Value="▾"/></Trigger></ControlTemplate.Triggers>
                </ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <Border Grid.Column="1" Background="Transparent" Padding="0,5"><StackPanel Orientation="Horizontal">
                <TextBlock x:Name="Pointer" Text="▶" Foreground="#66D98C" Margin="0,0,5,0" Visibility="Hidden"/>
                <ContentPresenter x:Name="PART_Header" ContentSource="Header"/>
              </StackPanel></Border>
              <ItemsPresenter x:Name="Children" Grid.Row="1" Grid.Column="1"/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsExpanded" Value="False"><Setter TargetName="Children" Property="Visibility" Value="Collapsed"/></Trigger>
              <Trigger Property="HasItems" Value="False"><Setter TargetName="Expander" Property="Visibility" Value="Hidden"/></Trigger>
              <Trigger Property="IsSelected" Value="True"><Setter TargetName="Pointer" Property="Visibility" Value="Visible"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """;
    private void RefreshFiles() => Safely(() =>
    {
        location.Text = folder + (search.Text.Length > 0 ? " · search includes subfolders" : "");
        var candidates = search.Text.Length > 0 ? ProjectAssetPaths.Files(root, folder) : Directory.EnumerateFiles(AssetLibrary.Resolve(root, folder));
        var selectedPaths = files.SelectedItems.Cast<Asset>().Select(a => a.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Allowed(string p) => fileFilter.SelectedIndex switch {
            1 => AssetLibrary.IsImage(p), 2 => AssetLibrary.IsTrail(p), 3 => AssetLibrary.IsDocument(p),
            _ => !Path.GetFileName(p).StartsWith('.') && !p.EndsWith(".cilantro",StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".taco",StringComparison.OrdinalIgnoreCase) };
        var list = candidates.Where(p => { string relative = Path.GetRelativePath(root,p).Replace('\\','/'); try { ProjectAssetPaths.Folder(relative); AssetLibrary.Resolve(root,relative); return true; } catch (InvalidOperationException) { return false; } }).Where(Allowed).Where(p => Path.GetFileName(p).Contains(search.Text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName).Select(p => new Asset(Path.GetRelativePath(root, p).Replace('\\', '/'), Path.GetFileName(p), File.GetLastWriteTimeUtc(p).Ticks)).ToList();
        if (!files.Items.Cast<Asset>().SequenceEqual(list))
        {
            files.ItemsSource = list;
            foreach (var asset in list.Where(a => selectedPaths.Contains(a.Path))) files.SelectedItems.Add(asset);
        }
        UpdateSelectionAction();
    });
    private void Choose()
    {
        if (files.SelectedItem is not Asset asset) { status.Text = "Select an image first."; return; }
        if (!AssetLibrary.IsImage(asset.Path)) { status.Text = "Choose an image for this attribute. TRL files use Insert; documents are shown for reference."; return; }
        SelectedAsset = asset.Path; DialogResult = true;
    }
    private void CreateFolder()
    {
        var dialog = new AssetFolderWindow { Owner = this, Topmost = true };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.FolderName.Trim();
        if (!ProjectStore.ValidFileName(name) || name is "." or "..") throw new InvalidOperationException("Enter a single folder name.");
        string relative = ProjectAssetPaths.FilePath(folder, name); ProjectAssetPaths.Folder(relative);
        string target = AssetLibrary.Resolve(root, relative);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("That name already exists.");
        Directory.CreateDirectory(target); RegisterFolder(relative); folder = relative; RefreshFolders();
    }
    private void Import()
    {
        var picker = new OpenFileDialog { Multiselect = true, Filter = "Project assets and documents|*.png;*.jpg;*.jpeg;*.bmp;*.dds;*.tga;*.trl;*.lua;*.md;*.markdown;*.txt;*.xml|Trail files (*.trl)|*.trl|Documents|*.lua;*.md;*.markdown;*.txt;*.xml|Image assets|*.png;*.jpg;*.jpeg;*.bmp;*.dds;*.tga" };
        if (picker.ShowDialog(this) != true) return;
        var added = new List<string>(); var errors = new List<string>();
        foreach (string path in picker.FileNames)
        {
            try
            {
                bool trail = AssetLibrary.IsTrail(path);
                if (!trail && !AssetLibrary.IsImage(path) && !AssetLibrary.IsDocument(path)) continue;
                if (Path.GetExtension(path).Equals(".xml",StringComparison.OrdinalIgnoreCase) && ReadPackXml(path).Root?.Name != "OverlayData") throw new InvalidOperationException("Pack XML must have an OverlayData root.");
                if (trail)
                {
                    var validated = TacoImporter.ReadTrail(File.ReadAllBytes(path),1,"import-validation");
                    if (!validated.Segments.Any(s => s.Points.Count > 0)) throw new InvalidOperationException("TRL contains no points.");
                }
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string destination = trail ? defaultTrails : ext == ".lua" ? defaults.Scripts : ext is ".md" or ".markdown" or ".txt" ? defaults.Docs : ext == ".xml" ? defaults.CoreXml : folder;
                string originalRelative = Path.GetRelativePath(root,Path.GetFullPath(path)).Replace('\\','/');
                if (File.Exists(path) && AssetFolders.Contains(destination,originalRelative) && !originalRelative.StartsWith(".."))
                { imported(originalRelative); added.Add(originalRelative); continue; }
                Directory.CreateDirectory(AssetLibrary.Resolve(root,destination));
                string name = Path.GetFileName(path), target = AssetLibrary.Resolve(root, ProjectAssetPaths.FilePath(destination, name));
                for (int i = 1; File.Exists(target) || Directory.Exists(target) || trackedPath(Path.GetRelativePath(root,target).Replace('\\','/')); i++) target = AssetLibrary.Resolve(root, ProjectAssetPaths.FilePath(destination, Path.GetFileNameWithoutExtension(name) + "_" + i + Path.GetExtension(name)));
                File.Copy(path, target, false);
                string relative = Path.GetRelativePath(root, target).Replace('\\', '/');
                try { imported(relative); } catch { File.Delete(target); throw; }
                added.Add(relative);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException) { errors.Add(Path.GetFileName(path) + ": " + ex.Message); }
        }
        bool hasTrails = added.Any(AssetLibrary.IsTrail);
        if (hasTrails) { folder = defaultTrails; fileFilter.SelectedIndex = 2; }
        else if (added.Count > 0) { folder = AssetFolders.Parent(added[^1]); fileFilter.SelectedIndex = 0; }
        RefreshFolders();
        foreach (var asset in files.Items.Cast<Asset>().Where(a => added.Contains(a.Path,StringComparer.OrdinalIgnoreCase))) files.SelectedItems.Add(asset);
        status.Text = "Imported " + added.Count + " asset(s). " + (hasTrails ? "Choose a category and click Insert. " : "") + string.Join("; ", errors);
    }
    private static System.Xml.Linq.XDocument ReadPackXml(string file)
    {
        using var reader = System.Xml.XmlReader.Create(file,new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        return System.Xml.Linq.XDocument.Load(reader);
    }
    private void RegisterFolder(string path)
    {
        if (!registeredFolders.Contains(path,StringComparer.OrdinalIgnoreCase)) registeredFolders.Add(path);
        saveFolders(registeredFolders.ToList());
    }
    private void AddExistingFolder()
    {
        var picker = new OpenFolderDialog { Title = "Add an existing folder inside this project", InitialDirectory = root };
        if (picker.ShowDialog(this) != true) return;
        string relative = ProjectAssetPaths.Folder(Path.GetRelativePath(root,picker.FolderName).Replace('\\','/'));
        _ = AssetLibrary.Resolve(root,relative); RegisterFolder(relative); folder = relative; RefreshFolders();
    }
    private void RenameFolder(string relative)
    {
        var dialog = new AssetFolderWindow(Path.GetFileName(relative),true) { Owner = this, Topmost = true };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.FolderName.Trim();
        if (!ProjectStore.ValidFileName(name)) throw new InvalidOperationException("Enter a valid single folder name.");
        string destination = ProjectAssetPaths.FilePath(AssetFolders.Parent(relative),name);
        UpdateDefaults(renameFolder(relative,name));
        RebaseRegistered(relative,destination); folder = AssetFolders.Relocate(folder,relative,destination); RefreshFolders();
    }
    private void RebaseRegistered(string source,string destination)
    {
        for (int i=0;i<registeredFolders.Count;i++) registeredFolders[i] = AssetFolders.Relocate(registeredFolders[i],source,destination);
        RegisterFolder(destination);
    }
    private void SetFolderDefault(string role,string path)
    {
        setFolderDefault(role,path);
        defaults = role switch {
            "Core XML" => defaults with { CoreXml = path }, "Docs" => defaults with { Docs = path },
            "Scripts" => defaults with { Scripts = path }, "Icons (markers)" => defaults with { MarkerIcons = path },
            "Textures (trails)" => defaults with { TrailTextures = path }, _ => defaults with { TrailFiles = path } };
        UpdateDefaults(defaults); RefreshFolders();
        status.Text = "Default " + role + " folder: " + path + ". Save Project to retain it.";
    }
    private void DeleteSelectedAssets()
    {
        var paths = files.SelectedItems.Cast<Asset>().Select(a => a.Path).ToList(); if (paths.Count == 0) return;
        var dialog = new ConfirmActionWindow("Delete assets", "Delete " + paths.Count + " selected file(s) from the project folder? Files are sent to the Windows Recycle Bin. Tracked documents leave the Outliner; references to deleted images or trail data will need replacement.") { Owner = this, Topmost = true };
        if (dialog.ShowDialog() != true) return;
        deleteAssets(paths); RefreshFolders(); status.Text = "Assets deleted from disk. Save Project to retain document changes.";
    }
    internal static ImageSource? Thumbnail(string root, string path)
    {
        try
        {
            string full = AssetLibrary.Resolve(root, path);
            if (!AssetLibrary.IsImage(path))
            {
                var group = new DrawingGroup();
                using (var dc = group.Open())
                {
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(36,48,59)), new Pen(Brushes.SeaGreen,2),new Rect(8,2,48,60),4,4);
                    var text = new FormattedText(Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Consolas"),14,Brushes.White,1);
                    dc.DrawText(text,new Point(12,22));
                }
                group.Freeze(); return new DrawingImage(group);
            }
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bitmap.DecodePixelWidth = 96;
            bitmap.UriSource = new Uri(full); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or System.Runtime.InteropServices.COMException) { return null; }
    }
    private sealed class ThumbnailConverter(string root) : System.Windows.Data.IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is string path ? Thumbnail(root, path) : null;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}

internal sealed class AssetFolderWindow : PreviewDialog
{
    public string FolderName { get; private set; } = "";
    public AssetFolderWindow(string initialName = "New Folder", bool renaming = false) : base(renaming ? "Rename folder" : "New asset folder", 420)
    {
        Label("Folder name"); var name = Input(initialName);
        Action(renaming ? "Rename" : "Create", (_, _) => { FolderName = name.Text; DialogResult = true; });
        Action("Cancel", (_, _) => DialogResult = false);
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}
