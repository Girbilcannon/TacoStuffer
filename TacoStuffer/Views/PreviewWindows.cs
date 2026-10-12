using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

public abstract class PreviewDialog : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(24) };
    protected PreviewDialog(string title, double width = 500)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("BackgroundBrush");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var frame = new Border { BorderBrush = (Brush)Application.Current.FindResource("LineBrush"), BorderThickness = new Thickness(1) };
        var dock = new DockPanel();
        var caption = new DockPanel { Background = (Brush)Application.Current.FindResource("PanelBrush"), LastChildFill = true };
        var close = new Button { Content = "×", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(5) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        caption.Children.Add(close);
        caption.Children.Add(new TextBlock { Text = title, Margin = new Thickness(14, 10, 0, 10), VerticalAlignment = VerticalAlignment.Center });
        caption.MouseLeftButtonDown += (_, e) =>
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is System.Windows.Controls.Primitives.ButtonBase) return;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
            }
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        DockPanel.SetDock(caption, Dock.Top);
        dock.Children.Add(caption);
        dock.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        frame.Child = dock;
        Content = frame;
    }

    protected void Label(string text) => Body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 6) });

    protected TextBox Input(string value)
    {
        var input = new TextBox { Text = value };
        Body.Children.Add(input);
        return input;
    }

    protected Button Action(string text, RoutedEventHandler handler)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 18, 0, 0) };
        b.Click += handler;
        Body.Children.Add(b);
        return b;
    }
}

public sealed class NoticeWindow : PreviewDialog
{
    public NoticeWindow(string title, string message) : base(title)
    {
        Label(message);
        Action("OK", (_, _) => Close());
    }
}

public sealed class PreferencesWindow : PreviewDialog
{
    public EditorPreferences Preferences { get; }
    public event System.Action<EditorPreferences>? VisualPreviewChanged;
    private readonly Dictionary<string, Button> buttons = [];
    private string? capturing;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    public PreferencesWindow(EditorPreferences preferences, bool showGame = false) : base("Preferences", 650)
    {
        Preferences = preferences.Copy();
        var markerNames = new CheckBox { Content = "Show Marker Names", IsChecked = Preferences.ShowMarkerNames, Margin = new Thickness(0, 0, 0, 8) };
        var selectedNames = new CheckBox { Content = "Only Show For Selected", IsChecked = Preferences.OnlyShowForSelected, Margin = new Thickness(24, 0, 0, 12) };
        var sections = new TabControl { Height = 440 };
        var visuals = new StackPanel { Margin = new Thickness(12) };
        visuals.Children.Add(markerNames);
        visuals.Children.Add(selectedNames);
        var nameDistanceLabel = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        var nameDistance = new Slider
        {
            Minimum = 0, Maximum = EditorPreferences.MaxMarkerNameDistance, Value = EditorPreferences.ValidNameDistance(Preferences.MarkerNameDistanceThreshold),
            TickFrequency = 10, SmallChange = 10, LargeChange = 100, IsSnapToTickEnabled = true,
            ToolTip = "Maximum distance from your character in meters. Changes preview immediately."
        };
        bool updatingVisuals = false;
        void PreviewNames()
        {
            if (updatingVisuals) return;
            Preferences.ShowMarkerNames = markerNames.IsChecked == true;
            Preferences.OnlyShowForSelected = selectedNames.IsChecked == true;
            Preferences.MarkerNameDistanceThreshold = (float)nameDistance.Value;
            selectedNames.IsEnabled = nameDistance.IsEnabled = Preferences.ShowMarkerNames;
            nameDistanceLabel.Text = "Marker Name Distance Threshold · " + nameDistance.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m";
            VisualPreviewChanged?.Invoke(Preferences);
        }
        markerNames.Checked += (_, _) => PreviewNames();
        markerNames.Unchecked += (_, _) => PreviewNames();
        selectedNames.Checked += (_, _) => PreviewNames();
        selectedNames.Unchecked += (_, _) => PreviewNames();
        nameDistance.ValueChanged += (_, _) => PreviewNames();
        visuals.Children.Add(nameDistanceLabel);
        visuals.Children.Add(nameDistance);
        PreviewNames();
        visuals.Children.Add(new TextBlock { Text = "Trail Overlay Selection", Margin = new Thickness(0, 18, 0, 8) });
        var trailModes = new Dictionary<TrailSelectionMode, RadioButton>();
        foreach (var mode in Enum.GetValues<TrailSelectionMode>())
        {
            var choice = new RadioButton
            {
                Content = mode switch { TrailSelectionMode.Trail => "Trail", TrailSelectionMode.TrailSegment => "Trail Segment", _ => "Nearest Point" },
                GroupName = "TrailOverlaySelection", IsChecked = Preferences.TrailOverlaySelection == mode, Margin = new Thickness(16, 0, 0, 8)
            };
            choice.Checked += (_, _) =>
            {
                if (updatingVisuals) return;
                Preferences.TrailOverlaySelection = mode;
                VisualPreviewChanged?.Invoke(Preferences);
            };
            trailModes[mode] = choice;
            visuals.Children.Add(choice);
        }
        visuals.Children.Add(new TextBlock { Text = "Preview multipliers only; exported XML sizes stay unchanged.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var visualSliders = new List<Slider>();
        void AddMultiplier(string label, float initial, System.Action<float> set)
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var readout = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var slider = new Slider { Minimum = .25, Maximum = 4, TickFrequency = .05, SmallChange = .05, LargeChange = .25, IsSnapToTickEnabled = true, Value = EditorPreferences.ValidMultiplier(initial), VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, _) =>
            {
                set((float)slider.Value);
                readout.Text = slider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";
            };
            readout.Text = slider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(readout, 2);
            row.Children.Add(slider);
            row.Children.Add(readout);
            visualSliders.Add(slider);
            visuals.Children.Add(row);
        }
        AddMultiplier("Marker Game Scale", Preferences.MarkerGameScale, value => Preferences.MarkerGameScale = value);
        AddMultiplier("Marker Map Scale", Preferences.MarkerMapScale, value => Preferences.MarkerMapScale = value);
        AddMultiplier("Marker Mini Map Scale", Preferences.MarkerMinimapScale, value => Preferences.MarkerMinimapScale = value);
        AddMultiplier("Trail Game Width", Preferences.TrailGameWidth, value => Preferences.TrailGameWidth = value);
        AddMultiplier("Trail Map Width", Preferences.TrailMapWidth, value => Preferences.TrailMapWidth = value);
        AddMultiplier("Trail Mini Map Width", Preferences.TrailMinimapWidth, value => Preferences.TrailMinimapWidth = value);
        sections.Items.Add(new TabItem { Header = "Visual Settings", Content = new ScrollViewer { Content = visuals, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var gameSettings = new StackPanel { Margin = new Thickness(12) };
        gameSettings.Children.Add(new TextBlock { Text = "Guild Wars 2 executable path", Margin = new Thickness(0, 0, 0, 8) });
        var executable = new TextBox { Text = Preferences.GameExecutablePath, Margin = new Thickness(0, 0, 0, 8) };
        gameSettings.Children.Add(executable);
        var browseGame = new Button { Content = "Browse…", HorizontalAlignment = HorizontalAlignment.Left };
        browseGame.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Select Guild Wars 2", Filter = "Guild Wars 2 executable|Gw2-64.exe;Gw2.exe;GuildWars2.exe", CheckFileExists = true };
            if (picker.ShowDialog(this) == true) executable.Text = picker.FileName;
        };
        gameSettings.Children.Add(browseGame);
        gameSettings.Children.Add(new TextBlock { Text = "Play in App uses this path to launch GW2 in windowed mode. Attaching to an existing windowed client does not need a path. This preference applies to every project.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        sections.Items.Add(new TabItem { Header = "Game", Content = gameSettings });
        var bindings = new StackPanel { Margin = new Thickness(12) };
        foreach (var action in EditorPreferences.Actions)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
            row.Children.Add(new TextBlock { Text = action.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var capture = new Button { Content = Preferences.Keybinds.GetValueOrDefault(action.Id)?.Display ?? "Unassigned", Margin = new Thickness(5, 0, 5, 0) };
            buttons[action.Id] = capture;
            Grid.SetColumn(capture, 1);
            row.Children.Add(capture);
            capture.Click += (_, _) =>
            {
                capturing = action.Id;
                RefreshButtons();
                capture.Content = "Press shortcut…";
                status.Text = "Press a key with optional Ctrl, Shift or Alt. Esc cancels.";
            };
            var clear = new Button { Content = "Clear" };
            Grid.SetColumn(clear, 2);
            row.Children.Add(clear);
            clear.Click += (_, _) =>
            {
                capturing = null;
                Preferences.Keybinds.Remove(action.Id);
                RefreshButtons();
                status.Text = "";
            };
            bindings.Children.Add(row);
        }
        sections.Items.Add(new TabItem { Header = "Custom Keybinds", Content = new ScrollViewer { Content = bindings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        if (showGame) sections.SelectedIndex = 1;
        sections.SelectionChanged += (_, _) =>
        {
            capturing = null;
            RefreshButtons();
        };
        Body.Children.Add(sections);
        Body.Children.Add(status);
        Label("Shortcuts work in the editor and in GW2 while the overlay is active. Game shortcuts also reach the game, so choose unused combinations.");
        PreviewKeyDown += (_, e) =>
        {
            if (capturing == null) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                capturing = null;
                RefreshButtons();
                status.Text = "";
                return;
            }
            var chord = new KeyChord(KeyInterop.VirtualKeyFromKey(key), (int)Keyboard.Modifiers, key.ToString());
            if (!EditorPreferences.Valid(chord)) return;
            if (Preferences.Conflict(capturing, chord) is { } error)
            {
                status.Text = error;
                return;
            }
            Preferences.Keybinds[capturing] = chord;
            capturing = null;
            RefreshButtons();
            status.Text = "Shortcut assigned.";
        };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var save = new Button { Content = "Save Preferences", Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) =>
        {
            string path = executable.Text.Trim().Trim('"');
            if (path.Length > 0 && (!File.Exists(path) || !new[] { "Gw2-64.exe", "Gw2.exe", "GuildWars2.exe" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            {
                status.Text = "Select an existing Guild Wars 2 executable, or leave the path blank.";
                return;
            }
            Preferences.GameExecutablePath = path;
            PreviewNames();
            Preferences.NormalizeVisuals();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var defaults = new Button { Content = "Defaults", ToolTip = "Reset visual settings and clear custom keybinds. Save Preferences to retain them." };
        defaults.Click += (_, _) =>
        {
            capturing = null;
            Preferences.ResetDefaults();
            updatingVisuals = true;
            try
            {
                markerNames.IsChecked = Preferences.ShowMarkerNames;
                selectedNames.IsChecked = Preferences.OnlyShowForSelected;
                nameDistance.Value = Preferences.MarkerNameDistanceThreshold;
                trailModes[Preferences.TrailOverlaySelection].IsChecked = true;
            }
            finally { updatingVisuals = false; }
            PreviewNames();
            foreach (var slider in visualSliders) slider.Value = 1;
            RefreshButtons();
            status.Text = "Defaults restored. Save Preferences to retain them.";
        };
        footer.Children.Add(save);
        footer.Children.Add(cancel);
        footer.Children.Add(defaults);
        Body.Children.Add(footer);
    }

    private void RefreshButtons()
    {
        foreach (var pair in buttons) pair.Value.Content = Preferences.Keybinds.GetValueOrDefault(pair.Key)?.Display ?? "Unassigned";
    }
}

public sealed class HistoryWindow : PreviewDialog
{
    public HistoryWindow(HistoryJournal<EditorState> history, System.Action undo, System.Action redo) : base("Edit History", 560)
    {
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, Height = 310, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Body.Children.Add(scroll);
        var undoButton = new Button { Content = "Undo", Margin = new Thickness(0, 18, 0, 0) };
        var redoButton = new Button { Content = "Redo", Margin = new Thickness(0, 8, 0, 0) };
        Body.Children.Add(undoButton);
        Body.Children.Add(redoButton);
        undoButton.Click += (_, _) =>
        {
            undo();
            Render();
        };
        redoButton.Click += (_, _) =>
        {
            redo();
            Render();
        };
        void Render()
        {
            list.Children.Clear();
            undoButton.IsEnabled = history.CanUndo;
            redoButton.IsEnabled = history.CanRedo;
            foreach (var entry in history.RedoEntries) list.Children.Add(new TextBlock { Text = "Redo · " + entry.Label, Opacity = .55, Margin = new Thickness(0, 4, 0, 4) });
            list.Children.Add(new TextBlock { Text = "CURRENT STATE", Foreground = (Brush)Application.Current.FindResource("AccentBrush"), Margin = new Thickness(0, 8, 0, 8) });
            foreach (var entry in history.UndoEntries.Reverse()) list.Children.Add(new TextBlock { Text = entry.Label, Margin = new Thickness(0, 4, 0, 4) });
        }
        Render();
        Action("Close", (_, _) => Close());
    }
}

public sealed class NewProjectWindow : PreviewDialog
{
    private readonly TextBox name;
    private readonly TextBox folder;
    private readonly TextBox output;
    public string ProjectName => name.Text.Trim();
    public string ProjectLocation => folder.Text.Trim();
    public string OutputLocation => output.Text.Trim();
    public NewProjectWindow(string title = "Create Project") : base(title, 560)
    {
        Label("NEW PROJECT");
        Label("Project name · spaces allowed");
        name = Input("My Markers");
        Label("Project folder location");
        folder = Input(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TacoStuffer"));
        Action("Browse project location…", (_, _) =>
        {
            var d = new OpenFolderDialog();
            if (d.ShowDialog(this) == true) folder.Text = d.FolderName;
        });
        Label("Compiled .taco output folder · optional");
        output = Input("");
        Action("Browse .taco output location…", (_, _) =>
        {
            var d = new OpenFolderDialog();
            if (d.ShowDialog(this) == true) output.Text = d.FolderName;
        });
        Label("Output filename · preview");
        var file = Input("MyMarkers.taco");
        file.IsReadOnly = true;
        name.TextChanged += (_, _) => file.Text = ProjectStore.TacoName(ProjectName);
        Label("Creates .cilantro, XML, DATA, TEXTURES and CUSTOM_ATTRIBUTES.\nLeave the output folder blank to export into the project EXPORT folder. EXPORT is created only when building with this fallback and is never packaged.");
        Action("Create Project", (_, _) =>
        {
            if (ProjectStore.ValidFileName(ProjectName) && ProjectLocation.Length > 0) DialogResult = true;
        });
    }
}

public sealed class UnsavedProjectWindow : PreviewDialog
{
    public string Choice { get; private set; } = "cancel";
    public UnsavedProjectWindow() : base("Unsaved project edits", 480)
    {
        Label("Save your current project edits before continuing?");
        Action("Save", (_, _) =>
        {
            Choice = "save";
            DialogResult = true;
        });
        Action("Discard edits", (_, _) =>
        {
            Choice = "discard";
            DialogResult = true;
        });
        Action("Cancel", (_, _) => DialogResult = false);
    }
}

public sealed class AttributePickerWindow : PreviewDialog
{
    private readonly List<(CheckBox Choice, AttributeDefinition Group)> choices = [];
    public List<AttributeDefinition> SelectedGroups { get; } = [];
    public AttributePickerWindow(List<AttributeDefinition> definitions, Action reload) : base("Add Attributes", 490)
    {
        foreach (bool custom in new[] { false, true })
        {
            Label(custom ? "CUSTOM" : "STANDARD / TAQUITO");
            foreach (var group in definitions.Where(g => g.IsCustom == custom && !g.Key.Equals("visibility", StringComparison.OrdinalIgnoreCase)))
            {
                var c = new CheckBox { Content = group.DisplayName, ToolTip = string.Join(", ", group.Members.Select(m => m.Name)) };
                choices.Add((c, group));
                if (group.Key.Equals("fade", StringComparison.OrdinalIgnoreCase))
                {
                    var row = new DockPanel { LastChildFill = true };
                    var link = new Hyperlink(new Run("IMPORTANT"))
                    {
                        Foreground = (Brush)Application.Current.FindResource("TortillaBrush"),
                        ToolTip = "Read the Fade compatibility note"
                    };
                    link.Click += (_, _) => new NoticeWindow("Fade compatibility", "If you are using Taco markers, trails, or their parent categories the Fade attribute is required to be visible in game. Blish HUD, Taquito, and Taimi HUD do not require this attribute.") { Owner = this, Topmost = Topmost }.ShowDialog();
                    var note = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    note.Inlines.Add(link);
                    DockPanel.SetDock(note, Dock.Right);
                    row.Children.Add(note);
                    row.Children.Add(c);
                    Body.Children.Add(row);
                }
                else Body.Children.Add(c);
            }
            if (custom && !definitions.Any(g => g.IsCustom)) Label("No custom definitions loaded.");
        }
        Action("Reload Custom Definitions…", (_, _) =>
        {
            reload();
            DialogResult = false;
        });
        Action("Add selected attributes", (_, _) =>
        {
            SelectedGroups.AddRange(choices.Where(c => c.Choice.IsChecked == true).Select(c => c.Group));
            DialogResult = true;
        });
    }
}

public sealed class CustomExportWindow : PreviewDialog
{
    public AttributeDefinition? Definition { get; private set; }
    public CustomExportWindow(List<XAttribute> attributes) : base("Export Custom Attributes", 540)
    {
        Label("Control name (file identifier)");
        var key = Input("myCustomControl");
        Label("Display name");
        var display = Input("My Custom Control");
        Label("Attributes to include · Optional checkbox controls whether a member may be disabled");
        var members = new List<(XAttribute Attribute, CheckBox Include, CheckBox Optional)>();
        foreach (var a in attributes)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var optional = new CheckBox { Content = "Optional", IsChecked = false };
            DockPanel.SetDock(optional, Dock.Right);
            row.Children.Add(optional);
            var include = new CheckBox { Content = a.Name.LocalName, IsChecked = true };
            row.Children.Add(include);
            Body.Children.Add(row);
            members.Add((a, include, optional));
        }
        Label("Values become defaults. Existing marker XML is retained. Definitions are XML files under CUSTOM_ATTRIBUTES and can be transferred to another project.");
        Action("Export definition…", (_, _) =>
        {
            string id = key.Text.Trim();
            try
            {
                XmlConvert.VerifyNCName(id);
            }
            catch (XmlException)
            {
                return;
            }
            var selected = members.Where(m => m.Include.IsChecked == true).Select(m => new AttributeMember(m.Attribute.Name.LocalName, m.Attribute.Value, "string", m.Optional.IsChecked == true)).ToList();
            if (selected.Count == 0) return;
            Definition = new AttributeDefinition(id, display.Text.Trim(), "POI,Trail,MarkerCategory", selected, true);
            DialogResult = true;
        });
    }
}

public sealed class MovePickerWindow : PreviewDialog
{
    private readonly ComboBox choices;
    public string Destination => choices.SelectedItem as string ?? "";
    public MovePickerWindow(string operation, int count, List<string> targets) : base(operation == "xml" ? "Move to XML…" : "Move to different category…", 530)
    {
        Label($"{count} selected items");
        Label(operation == "xml" ? "Destination XML document" : "Destination category");
        choices = new ComboBox { ItemsSource = targets, SelectedIndex = targets.Count > 0 ? 0 : -1 };
        Body.Children.Add(choices);
        Label(operation == "xml" ? "Move the selected declarations and their assigned markers/trails to this XML. Required parent categories are retained or added. Compatible existing categories are merged." : "Move categories beneath this parent across the pack; their assigned elements follow the new path. Unrelated selected markers/trails join this category in their current XML.");
        Action("Move selected items", (_, _) =>
        {
            if (Destination.Length != 0) DialogResult = true;
        });
    }
}

public sealed class DeleteConfirmWindow : PreviewDialog
{
    public DeleteConfirmWindow(int categories, int markers, int trails) : base("Delete selected elements", 520)
    {
        Label($"Remove {categories} category declarations, {markers} markers and {trails} trails?");
        Label("Deleting a category removes its shared declarations, descendants and assigned elements across all XML documents. Texture and trail files remain on disk. This edit can be undone from Edit → Undo.");
        Action("Cancel", (_, _) => DialogResult = false);
        Action("Delete", (_, _) => DialogResult = true);
    }
}

public sealed class RenameWindow : PreviewDialog
{
    private readonly TextBox name;
    private readonly TextBox display;
    public string ItemName => name.Text.Trim();
    public string DisplayName => display.Text.Trim();
    public RenameWindow(string title, string currentName, string currentDisplayName) : base(title, 480)
    {
        Label("Name");
        name = Input(currentName);
        Label("DisplayName");
        display = Input(currentDisplayName);
        Action("Apply", (_, _) =>
        {
            if (ItemName.Length != 0) DialogResult = true;
        });
        Loaded += (_, _) =>
        {
            name.Focus();
            name.SelectAll();
        };
    }
}

public sealed class XmlRenameWindow : PreviewDialog
{
    private readonly TextBox file;
    public string FileName => file.Text.Trim();
    public XmlRenameWindow(string currentName) : base("Rename XML file", 480)
    {
        Label("XML filename · .xml is added if omitted");
        file = Input(currentName);
        Action("Rename", (_, _) =>
        {
            if (FileName.Length > 0) DialogResult = true;
        });
        Loaded += (_, _) =>
        {
            file.Focus();
            file.Select(0, Path.GetFileNameWithoutExtension(currentName).Length);
        };
    }
}

public sealed class ConfirmActionWindow : PreviewDialog
{
    public ConfirmActionWindow(string title, string message) : base(title, 500)
    {
        Label(message);
        Action("Delete", (_, _) =>
        {
            DialogResult = true;
        });
        Action("Cancel", (_, _) =>
        {
            DialogResult = false;
        });
    }
}
