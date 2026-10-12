using System.Windows;
using System.Windows.Controls;
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
    public HostedCodeWindow(Action hide)
    {
        Title = "XML — Taco Stuffer";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 350;
        Height = 500;
        var layout = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(10, 6, 6, 6) };
        var close = new Button { Content = "×", Padding = new Thickness(7, 2, 7, 2), ToolTip = "Hide XML; keep editing state" };
        close.Click += (_, _) => hide();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
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
        Content = surface;
    }

    private static Brush BackgroundAt(double percent) => new SolidColorBrush(Color.FromArgb((byte)Math.Round(percent * 2.55), 17, 25, 32));

    public void SetEditor(RichTextBox editor, string name)
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
        int width = Math.Max(1, (bounds.Right - bounds.Left) / 3), height = Math.Max(1, bounds.Bottom - bounds.Top);
        var target = (bounds.Right - width, bounds.Top, width, height);
        if (lastBounds == target) return;
        if (NativeGameWindow.SetWindowPos(new WindowInteropHelper(this).Handle, 0, target.Item1, target.Item2, width, height, 0x0014)) lastBounds = target;
    }
}
