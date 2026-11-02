using System.Windows;
using System.Diagnostics;
using System.ComponentModel;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private static string ReleaseVersion => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.7";
    private void OpenDocumentation(object sender, RoutedEventArgs e) => OpenHelpLink("https://girbilcannon.github.io/TacoStuffer/");
    private void OpenDiscord(object sender, RoutedEventArgs e) => OpenHelpLink("https://discord.gg/KU8jwACydZ");

    private void OpenHelpLink(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { Notice("Could not open browser", "Open this address in your browser:\n" + address + "\n\n" + ex.Message); }
    }

    private void ShowAbout(object sender, RoutedEventArgs e) => new AboutWindow(ReleaseVersion) { Owner = this }.ShowDialog();
}
