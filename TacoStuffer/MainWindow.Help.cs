using System.Windows;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private static string ReleaseVersion => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.7";
    private void ShowAbout(object sender, RoutedEventArgs e) => new AboutWindow(ReleaseVersion) { Owner = this }.ShowDialog();
}
