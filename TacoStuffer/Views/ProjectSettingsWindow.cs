using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

public sealed class ProjectSettingsWindow : PreviewDialog
{
    public ProjectAssetPaths Paths { get; private set; }
    public string OutputFolder { get; private set; }
    public bool IncludeDocumentation { get; private set; }
    public ProjectSettingsWindow(string projectRoot, ProjectAssetPaths current, string currentOutputFolder, bool includeDocumentation = true) : base("Project Settings", 610)
    {
        Paths = current;
        OutputFolder = currentOutputFolder;
        IncludeDocumentation = includeDocumentation;
        Label("Compiled .taco output folder · optional");
        var output = Input(currentOutputFolder);
        Action("Browse .taco output location…", (_, _) =>
        {
            var browse = new OpenFolderDialog
            {
                Title = "Choose compiled .taco output folder",
                InitialDirectory = Directory.Exists(output.Text.Trim()) ? output.Text.Trim() : projectRoot
            };
            if (browse.ShowDialog(this) == true) output.Text = browse.FolderName;
        });
        Label("Leave blank to use this project's EXPORT folder. The selected folder is used for future builds; existing .taco files stay in their current location.");
        var includeDocs = new CheckBox { Content = "Include documentation in compiled .taco", IsChecked = includeDocumentation, Margin = new Thickness(0, 12, 0, 4) };
        Body.Children.Add(includeDocs);
        Label("Includes Markdown and plain-text documents. Uncheck to keep build notes only in your project. Scripts are included separately.");
        Label("Asset folders · relative to this project");
        Label("Choose a folder inside the project, or use . for the project root. Pickers include subfolders. These defaults do not move existing files or change XML references.");
        TextBox Field(string label, string value)
        {
            Label(label);
            var input = Input(value);
            Action("Browse…", (_, _) =>
            {
                var browse = new OpenFolderDialog { Title = label, InitialDirectory = projectRoot };
                if (browse.ShowDialog(this) != true) return;
                string relative = Path.GetRelativePath(projectRoot, browse.FolderName).Replace('\\', '/');
                try
                {
                    input.Text = ProjectAssetPaths.Folder(relative);
                }
                catch (InvalidOperationException ex)
                {
                    new NoticeWindow("Project folder required", ex.Message) { Owner = this }.ShowDialog();
                }
            });
            return input;
        }
        var icons = Field("Marker Icons Path", current.MarkerIcons);
        var textures = Field("Trail Textures Path", current.TrailTextures);
        var trails = Field("Trail File (.trl) Path", current.TrailFiles);
        Label("Existing custom paths still render from XML even when they are outside the picker folder. Save the project to retain these settings.");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Body.Children.Add(status);
        Action("Defaults", (_, _) =>
        {
            output.Text = "";
            includeDocs.IsChecked = true;
            icons.Text = textures.Text = "TEXTURES";
            trails.Text = "DATA";
        });
        Action("Apply", (_, _) =>
        {
            try
            {
                string folder = output.Text.Trim();
                if (folder.Length != 0 && !Path.IsPathFullyQualified(folder))
                    throw new InvalidOperationException("The .taco output location must be an absolute folder path, or blank to use EXPORT.");
                Paths = new ProjectAssetPaths(icons.Text, textures.Text, trails.Text).Validated();
                IncludeDocumentation = includeDocs.IsChecked == true;
                OutputFolder = folder.Length == 0 ? "" : Path.GetFullPath(folder);
                DialogResult = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or NotSupportedException)
            {
                status.Text = ex.Message;
            }
        });
        Action("Cancel", (_, _) => DialogResult = false);
    }
}

public sealed class ImageDestinationWindow : PreviewDialog
{
    public string? Folder { get; private set; }
    public ImageDestinationWindow(ProjectAssetPaths paths) : base("Import Textures/Icons", 500)
    {
        Label("Choose the destination for the selected images.");
        Action("Marker icons · " + paths.MarkerIcons, (_, _) =>
        {
            Folder = paths.MarkerIcons;
            DialogResult = true;
        });
        Action("Trail textures · " + paths.TrailTextures, (_, _) =>
        {
            Folder = paths.TrailTextures;
            DialogResult = true;
        });
        Action("Cancel", (_, _) => DialogResult = false);
    }
}
