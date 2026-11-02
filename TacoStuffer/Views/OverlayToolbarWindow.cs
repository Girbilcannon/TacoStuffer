using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TacoStuffer.Models;
using TacoStuffer.Services;

namespace TacoStuffer.Views;

// Separate HWND: hiding/minimizing the control panel never hides this HUD.
// No activation keeps tool clicks from taking keyboard focus away from the game.
public sealed class OverlayToolbarWindow : Window
{
    public event Action<string>? Command;
    public event Action? InteractionFinished;
    public event Action<ToolbarPosition>? PositionRemembered;
    private ToolbarPosition? savedPosition;
    private readonly Dictionary<string, Button> buttons = [];
    private bool positioned;
    private nint Handle => new WindowInteropHelper(this).Handle;
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(202, 211, 209));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(104, 220, 136));
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(255, 218, 63));
    private readonly HashSet<string> active = [];
    private readonly Dictionary<string, (bool Enabled, bool Active, bool Hover)> painted = [];
    private static readonly Brush ActiveBackground = FrozenBackground(255, 218, 63);
    private static readonly Brush HoverBackground = FrozenBackground(104, 220, 136);
    private static Brush FrozenBackground(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(45, r, g, b)); brush.Freeze(); return brush;
    }

    public OverlayToolbarWindow(ToolbarPosition? position = null)
    {
        savedPosition = position;
        Title = "Taco Stuffer Toolbar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        var frame = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.FromArgb(228, 29, 36, 36)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 107, 139, 126)),
            BorderThickness = new Thickness(1)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        frame.Child = row; Content = frame;
        var grip = new TextBlock
        {
            Text = "⠿",
            Foreground = Gray,
            FontSize = 19,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag toolbar"
        };
        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 1) return;
            try { DragMove(); }
            finally { RememberPosition(); InteractionFinished?.Invoke(); }
            e.Handled = true;
        };
        row.Children.Add(grip);
        Section(row, "Testing Tools", [
            ("interactions", "Test marker interactions · activate behaviors, category actions and clipboard triggers", "M 4,4 L 20,4 20,16 12,16 7,21 7,16 4,16 Z M 8,8 L 16,8 M 8,12 L 13,12"),
            ("checkpoints", "Test checkpoints · click again to cancel", "M 5,22 L 5,2 M 5,3 L 20,3 17,8 20,13 5,13")]);
        Section(row, "Edit Tools", [
            ("Select", "Selection tool · Shift adds/ranges; Ctrl toggles", "M 4,2 L 19,13 12,14 9,21 Z"),
            ("Marquee", "Marquee select · drag a box; Shift adds, Ctrl toggles; Esc exits", "M 3,7 L 3,3 7,3 M 11,3 L 15,3 M 19,3 L 22,3 22,7 M 22,11 L 22,15 M 22,19 L 22,22 18,22 M 14,22 L 10,22 M 3,11 L 3,15 M 3,19 L 3,22 6,22 M 8,8 L 16,14 12,15 10,20 Z"),
            ("InsertPoint", "Insert trail points · click an editable trail; Esc exits", "M 2,18 L 7,10 13,16 22,5 M 5,4 L 13,4 M 9,0 L 9,8"),
            ("RemovePoint", "Remove trail point · reconnects its neighbors; Undo is available", "M 2,18 L 7,10 13,16 22,5 M 3,3 L 13,3"),
            ("SplitSegment", "Split trail segment · remove an interior point and leave a gap; Undo is available", "M 2,12 L 8,12 M 16,12 L 22,12 M 8,9 L 8,15 M 16,9 L 16,15 M 12,2 L 12,5 M 12,19 L 12,22"),
            ("MergeSegments", "Merge segments · choose two segments by their endpoints, then Enter; Outliner order defines flow", "M 1,19 L 7,13 11,13 M 13,11 L 17,11 23,5 M 8,7 L 8,3 16,3 16,7 M 8,17 L 8,21 16,21 16,17"),
            ("ReverseSegment", "Reverse trail segment · click any point; Undo is available", "M 3,7 L 21,7 M 16,2 L 21,7 16,12 M 21,17 L 3,17 M 8,12 L 3,17 8,22")]);
        Section(row, "Trails", [
            ("start", "Start trail recording · create a trail in the overlay first", "M 5,3 L 21,12 5,21 Z"),
            ("pause", "Pause trail recording", "M 6,3 L 6,21 M 17,3 L 17,21"),
            ("continue", "Continue trail recording", "M 2,4 L 12,12 2,20 Z M 12,4 L 22,12 12,20 Z"),
            ("segment", "Start new segment · pause recording first", "M 3,3 L 18,12 3,21 Z M 21,3 L 21,21"),
            ("stop", "Stop trail recording", "M 4,4 L 20,4 20,20 4,20 Z")]);
        Section(row, "Markers", [
            ("marker", "Add marker at character · uses the overlay recording target", "M 12,3 L 12,21 M 3,12 L 21,12"),
            ("reposition", "Reposition selected marker to character", "M 13,3 A 9,9 0 1 0 21,11 M 3,12 L 16,12 M 12,7 L 17,12 12,17 M 17,2 L 23,2 23,8")]);
        var close = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            Foreground = Gray,
            BorderThickness = new Thickness(0),
            Focusable = false,
            ToolTip = "Close toolbar"
        };
        close.Click += (_, _) => { RememberPosition(); InteractionFinished?.Invoke(); Close(); }; row.Children.Add(close);
        Closing += (_, _) => RememberPosition();
        SourceInitialized += (_, _) =>
        {
            NativeGameWindow.DisableActivation(Handle, clickThrough: false);
            HwndSource.FromHwnd(Handle)?.AddHook(NoActivate);
        };
    }
    private void Section(Panel parent, string heading, (string Id, string Tip, string Path)[] items)
    {
        if (buttons.Count > 0) parent.Children.Add(new Border { Width = 1, Margin = new Thickness(7, 4, 7, 3), Background = new SolidColorBrush(Color.FromArgb(100, 175, 192, 184)) });
        var section = new StackPanel(); parent.Children.Add(section);
        section.Children.Add(new TextBlock { Text = heading, Foreground = Gray, FontSize = 11, Margin = new Thickness(2, 0, 0, 5) });
        var strip = new StackPanel { Orientation = Orientation.Horizontal }; section.Children.Add(strip);
        foreach (var item in items)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(item.Path),
                Stroke = Gray,
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform
            };
            var button = new Button
            {
                Width = 35,
                Height = 35,
                Margin = new Thickness(1),
                Padding = new Thickness(5),
                Content = icon,
                Focusable = false,
                ToolTip = item.Tip,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            // Local template avoids application-wide button hover/selection colors.
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content); button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            button.Click += (_, _) =>
            {
                try { Command?.Invoke(item.Id); }
                finally { InteractionFinished?.Invoke(); }
            };
            button.MouseEnter += (_, _) => Paint(item.Id); button.MouseLeave += (_, _) => Paint(item.Id);
            button.IsEnabledChanged += (_, _) => Paint(item.Id);
            buttons.Add(item.Id, button); strip.Children.Add(button);
            ToolTipService.SetInitialShowDelay(button, 350);
        }
    }
    private void Paint(string id)
    {
        var button = buttons[id];
        var state = (button.IsEnabled, active.Contains(id), button.IsMouseOver);
        if (painted.TryGetValue(id, out var previous) && previous == state) return;
        painted[id] = state;
        var icon = (System.Windows.Shapes.Path)button.Content;
        icon.Stroke = active.Contains(id) ? Yellow : button.IsMouseOver ? Green : Gray;
        button.Opacity = button.IsEnabled ? 1 : .28;
        button.Background = active.Contains(id) ? ActiveBackground
            : button.IsMouseOver ? HoverBackground : Brushes.Transparent;
    }
    public void SetState(OverlayEditTool tool, bool interactions, bool checkpoints, IReadOnlyDictionary<string, bool> enabled)
    {
        active.Clear(); active.Add(tool.ToString());
        if (interactions) active.Add("interactions"); if (checkpoints) active.Add("checkpoints");
        foreach (var item in buttons) { item.Value.IsEnabled = !enabled.TryGetValue(item.Key, out bool value) || value; Paint(item.Key); }
    }
    public void Open() { if (!IsVisible) Show(); }
    private void RememberPosition()
    {
        if (!positioned || Handle == 0 || !NativeGameWindow.GetWindowRect(Handle, out var rect)) return;
        var position = new ToolbarPosition(rect.Left, rect.Top);
        if (position == savedPosition) return;
        savedPosition = position;
        PositionRemembered?.Invoke(position);
    }
    public void UpdateVisibility(GameClientWindow game, bool enabled)
    {
        if (!enabled || !NativeGameWindow.IsForeground(game, Handle, Environment.ProcessId) || !game.TryBounds(out var bounds)) { Hide(); return; }
        if (!IsVisible) Show();
        if (!positioned)
        {
            UpdateLayout();
            var scale = VisualTreeHelper.GetDpi(this);
            int width = (int)Math.Ceiling(ActualWidth * scale.DpiScaleX);
            int height = (int)Math.Ceiling(ActualHeight * scale.DpiScaleY);
            int x = savedPosition?.X ?? bounds.Left + 25;
            int y = savedPosition?.Y ?? bounds.Top + 45;
            var work = NativeGameWindow.MonitorWorkAreaAt(x, y, width, height, bounds);
            // Keep a remembered toolbar reachable if its old monitor was removed.
            x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
            y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
            NativeGameWindow.SetWindowPos(Handle, 0, x, y, width, height, 0x0014);
            positioned = true;
            RememberPosition();
        }
    }
    private nint NoActivate(nint hwnd, int message, nint w, nint l, ref bool handled)
    { if (message == 0x21) { handled = true; return new nint(3); } return 0; }
}
