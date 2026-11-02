using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TacoStuffer.Models;
using System.Windows.Interop;
using System.Windows.Media;
using TacoStuffer.Services;

namespace TacoStuffer.Views;

// A separate layered HWND is required to draw above the native game child.
// Only the background is translucent; text and editing controls stay opaque.
public sealed class HostedCodeWindow : Window
{
    private readonly ContentControl editorHost = new();
    private readonly TextBlock caption = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border surface;
    private (int X, int Y, int Width, int Height)? lastBounds;
    private NativeGameWindow.Rect? viewportBounds;
    private double dockedFraction = 1.0 / 3;
    private readonly Button floatButton = new() { Content = "Float", Padding = new Thickness(7,2,7,2), ToolTip = "Detach code and move it to another monitor" };
    private readonly Thumb cornerGrip;
    public bool IsFloating { get; private set; }
    public HostedCodeWindow(Action hide)
    {
        Title = "Code — Taco Stuffer";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 350;
        Height = 500;
        MinWidth = 240; MinHeight = 200;
        var layout = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(10, 6, 6, 6) };
        var close = new Button { Content = "×", Padding = new Thickness(7, 2, 7, 2), ToolTip = "Hide code; keep editing state" };
        close.Click += (_, _) => hide();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        DockPanel.SetDock(floatButton, Dock.Right); header.Children.Add(floatButton);
        floatButton.Click += (_,_) => SetFloating(!IsFloating);
        caption.ToolTip = "Drag this title to float the code window. Use Dock to return it over the game.";
        caption.MouseLeftButtonDown += (_,e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            SetFloating(true);
            DragMove();
            e.Handled = true;
        };
        header.Children.Add(caption);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var controls = new DockPanel { Margin = new Thickness(10, 0, 10, 6) };
        var label = new TextBlock { Text = "Background", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        controls.Children.Add(label);
        var opacity = new Slider { Minimum = 15, Maximum = 100, Value = 80, ToolTip = "Background opacity; code text stays readable" };
        controls.Children.Add(opacity);
        DockPanel.SetDock(controls, Dock.Top);
        layout.Children.Add(controls);
        layout.Children.Add(editorHost);
        surface = new Border { Background = BackgroundAt(80), BorderBrush = new SolidColorBrush(Color.FromArgb(150, 131, 190, 174)), BorderThickness = new Thickness(1), Child = layout };
        opacity.ValueChanged += (_, _) => surface.Background = BackgroundAt(opacity.Value);
        var workspace = new Grid();
        surface.Margin = new Thickness(5,0,0,0); workspace.Children.Add(surface);
        var leftGrip = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.SizeWE,
            Background = new SolidColorBrush(Color.FromArgb(150,102,217,140)), ToolTip = "Drag left to widen the code window" };
        leftGrip.DragDelta += (_,e) =>
        {
            if (IsFloating)
            {
                double right = Left + ActualWidth;
                Width = Math.Max(MinWidth,ActualWidth - e.HorizontalChange);
                Left = right - Width;
            }
            else if (viewportBounds is { } bounds)
            {
                dockedFraction = HostedCodeLayout.ResizeFraction(dockedFraction,e.HorizontalChange,VisualTreeHelper.GetDpi(this).DpiScaleX,bounds.Right-bounds.Left);
                lastBounds = null; Fit(bounds);
            }
        };
        workspace.Children.Add(leftGrip);
        cornerGrip = new Thumb { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE, Background = new SolidColorBrush(Color.FromRgb(102,217,140)), Visibility = Visibility.Collapsed,
            ToolTip = "Resize floating code window" };
        cornerGrip.DragDelta += (_,e) =>
        {
            Width = Math.Max(MinWidth,ActualWidth + e.HorizontalChange);
            Height = Math.Max(MinHeight,ActualHeight + e.VerticalChange);
        };
        workspace.Children.Add(cornerGrip);
        Content = workspace;
    }

    private void SetFloating(bool floating)
    {
        if (IsFloating == floating) return;
        IsFloating = floating;
        floatButton.Content = floating ? "Dock" : "Float";
        floatButton.ToolTip = floating ? "Return code to the right side of the hosted game" : "Detach code and move it to another monitor";
        cornerGrip.Visibility = floating ? Visibility.Visible : Visibility.Collapsed;
        lastBounds = null;
        if (!floating && viewportBounds is { } bounds) Fit(bounds);
    }

    private static Brush BackgroundAt(double percent) => new SolidColorBrush(Color.FromArgb((byte)Math.Round(percent * 2.55), 17, 25, 32));

    public void SetEditor(FrameworkElement editor, string name)
    {
        editorHost.Content = editor;
        Rename(name);
    }

    public void Rename(string name)
    {
        caption.Text = name;
        Title = name + " — Taco Stuffer";
    }

    public void ReleaseEditor() => editorHost.Content = null;

    public void Fit(NativeGameWindow.Rect bounds)
    {
        viewportBounds = bounds;
        if (IsFloating) return; // Polling must never snap a floating editor back over the game.
        int width = HostedCodeLayout.DockedWidth(bounds.Right - bounds.Left,dockedFraction), height = Math.Max(1, bounds.Bottom - bounds.Top);
        var target = (bounds.Right - width, bounds.Top, width, height);
        if (lastBounds == target) return;
        if (NativeGameWindow.SetWindowPos(new WindowInteropHelper(this).Handle, 0, target.Item1, target.Item2, width, height, 0x0014)) lastBounds = target;
    }
}
