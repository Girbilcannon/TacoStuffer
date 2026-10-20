using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TacoStuffer.Services;

namespace TacoStuffer.Views;

// A non-activating viewport HUD, separate from the authoring panel and world renderer.
public sealed class InteractionHudWindow : Window
{
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(12) };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(12, 0, 12, 8) };
    private readonly Button copy = new() { Content = "Copy text", Margin = new Thickness(12, 0, 12, 12) };
    private string content = "", description = "";
    public event Action? Dismissed;
    public event Action? CopyRequested;
    public InteractionHudWindow(bool isInfo, bool layoutPreview = false)
    {
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        var body = new StackPanel();
        var header = new DockPanel { Background = new SolidColorBrush(Color.FromArgb(245, 30, 32, 36)) };
        var close = new Button { Content = "×", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(5) };
        close.Click += (_, _) => { if (layoutPreview) Close(); else { Hide(); Dismissed?.Invoke(); } };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = layoutPreview ? "Info · Layout preview (not a range test)" : isInfo ? "Info" : "Copy", Foreground = Brushes.White, Margin = new Thickness(12, 8, 0, 8) });
        body.Children.Add(header);
        body.Children.Add(new ScrollViewer { Content = text, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(detail);
        if (!isInfo) body.Children.Add(copy);
        copy.Click += (_, _) => CopyRequested?.Invoke();
        Content = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray,
            Background = new SolidColorBrush(Color.FromArgb(220, 42, 44, 48)), Child = body };
        SourceInitialized += (_, _) => NativeGameWindow.DisableActivation(new WindowInteropHelper(this).Handle);
    }

    public void SetText(string value, string descriptionText = "", bool canCopy = false)
    {
        if (content != value) { content = value; text.Text = value; }
        if (description != descriptionText) { description = descriptionText; detail.Text = descriptionText; }
        detail.Visibility = descriptionText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        copy.IsEnabled = canCopy;
    }

    public void Present(NativeGameWindow.Rect game, nint panel, bool top)
    {
        if (!IsVisible) Show();
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = Math.Min(game.Right - game.Left, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        int height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        int x = game.Left + (game.Right - game.Left - width) / 2;
        int y = top ? game.Top + 30 : Math.Max(game.Top, game.Bottom - height - 40);
        NativeGameWindow.PlaceOverlay(new WindowInteropHelper(this).Handle, panel, x, y, width, height, true);
    }
}
