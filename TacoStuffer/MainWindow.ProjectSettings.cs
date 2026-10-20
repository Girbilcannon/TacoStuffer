using System.IO;
using System.Windows;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private void ShowProjectSettings(object sender, RoutedEventArgs e)
    {
        if (projectFile == null || projectFolder == null)
        {
            Notice("No project", "Create or open a project first.");
            return;
        }
        var dialog = new ProjectSettingsWindow(projectFolder, assetPaths, tacoOutputFolder, includeDocumentation) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _ = ProjectStore.ExportPath(projectFile, CurrentSnapshot() with { AssetPaths = dialog.Paths, OutputFolder = dialog.OutputFolder });
            foreach (string path in new[] { dialog.Paths.MarkerIcons, dialog.Paths.TrailTextures, dialog.Paths.TrailFiles }) Directory.CreateDirectory(Path.Combine(projectFolder, path));
            assetPaths = dialog.Paths;
            tacoOutputFolder = dialog.OutputFolder;
            includeDocumentation = dialog.IncludeDocumentation;
            if (selected?.Element != null) ShowAttributes();
            StatusText.Text = "Project folders updated. Save the project to retain them; future builds use the selected .taco output folder.";
        }
        catch (Exception ex) when (ProjectError(ex))
        {
            Notice("Project settings", ex.Message);
        }
    }
}
