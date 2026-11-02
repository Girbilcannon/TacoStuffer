using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TacoStuffer.Views;

public sealed class AboutWindow : PreviewDialog
{
    public AboutWindow(string version) : base("About Taco Stuffer", 440)
    {
        ShowInTaskbar = false;
        Icon = new BitmapImage(new Uri("pack://application:,,,/TacoStuffer;component/Assets/Official/TacoStuffer.ico"));
        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/TacoStuffer;component/Assets/Official/PNG/TacoStuffer_256x256.png")),
            Width = 192,
            Height = 192,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14)
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        Body.Children.Add(logo);
        void Centered(string text, double size, Brush brush, Thickness margin) => Body.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = brush,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin
        });
        Centered("Taco Stuffer", 25, Foreground, new Thickness(0));
        Centered("Version " + version, 14, (Brush)FindResource("AccentBrush"), new Thickness(0, 6, 0, 0));
        Centered("Marker Pack Authoring Suite", 13, Foreground, new Thickness(0, 9, 0, 0));
        Centered("by: Girbilcannon.8259", 14, Foreground, new Thickness(0, 10, 0, 18));
        var notices = new Button { Content = "Third-party notices…", Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Center };
        notices.Click += (_, _) => {
            using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("TacoStuffer.ThirdPartyNotices.txt");
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            new NoticeWindow("Third-party notices", reader.ReadToEnd()) { Owner = this }.ShowDialog();
        };
        Body.Children.Add(notices);
        var badge = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/TacoStuffer;component/Assets/Official/AI-Assisted.png")),
            Width = 153,
            Height = 28,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "AI assisted: yes"
        };
        System.Windows.Automation.AutomationProperties.SetName(badge, "AI assisted: yes");
        Body.Children.Add(badge);
        var close = new Button { Content = "Close", IsCancel = true, Margin = new Thickness(0, 20, 0, 0), MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close(); Body.Children.Add(close);
    }
}
