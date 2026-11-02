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
        var dialog = new ProjectSettingsWindow(projectFolder, assetPaths, tacoOutputFolder, includeDocumentation, () => BrowseAssets(true, false), () => assetPaths, alternateOutputs) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _ = ProjectStore.ExportPath(projectFile, CurrentSnapshot() with { AssetPaths = dialog.Paths, OutputFolder = dialog.OutputFolder });
            assetPaths = AssetFolders.ExistingDefaults(projectFolder, dialog.Paths);
            tacoOutputFolder = dialog.OutputFolder;
            alternateOutputs = dialog.AlternateOutputs;
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
