using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace TacoStuffer.Views;

public sealed class DetachedDocumentWindow : Window
{
    private readonly DockPanel layout = new();
    private readonly UIElement document;
    private readonly TextBlock captionText;
    public DetachedDocumentWindow(string title, UIElement content, Action dock)
    {
        Title = title + " — Taco Stuffer";
        Width = 850;
        Height = 720;
        MinWidth = 450;
        MinHeight = 300;
        Background = (Brush)Application.Current.FindResource("BackgroundBrush");
        Foreground = Brushes.White;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
        var bar = new DockPanel { Background = (Brush)Application.Current.FindResource("PanelBrush"), Margin = new Thickness(0, 0, 0, 4) };
        var close = new Button { Content = "×", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(5) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        var restore = new Button { Content = "Return to main", Margin = new Thickness(5) };
        restore.Click += (_, _) => dock();
        DockPanel.SetDock(restore, Dock.Right);
        bar.Children.Add(restore);
        captionText = new TextBlock { Text = title, Margin = new Thickness(15), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(captionText);
        bar.MouseLeftButtonDown += (_, e) =>
        {
            DependencyObject? node = e.OriginalSource as DependencyObject;
            while (node != null)
            {
                if (node is System.Windows.Controls.Primitives.ButtonBase) return;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
            }
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        DockPanel.SetDock(bar, Dock.Top);
        layout.Children.Add(bar);
        document = content;
        layout.Children.Add(document);
        Content = layout;
    }

    public UIElement ReleaseDocument()
    {
        layout.Children.Remove(document);
        return document;
    }

    public void RenameDocument(string name)
    {
        Title = name + " — Taco Stuffer";
        captionText.Text = name;
    }
}
