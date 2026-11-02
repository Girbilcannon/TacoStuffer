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
    public List<string> AlternateOutputs { get; private set; } = [];
    public bool IncludeDocumentation { get; private set; }
    public ProjectSettingsWindow(string projectRoot, ProjectAssetPaths current, string currentOutputFolder, bool includeDocumentation = true, Action? openAssets = null, Func<ProjectAssetPaths>? currentPaths = null, IReadOnlyList<string>? alternateOutputs = null) : base("Project Settings", 610)
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
        Label("Leave blank to use this project's EXPORT folder.");
        var alternates = new List<TextBox>();
        for (int i=0;i<2;i++)
        {
            Label("Alternate marker pack folder " + (i+1) + " · optional");
            var input = Input(alternateOutputs?.ElementAtOrDefault(i) ?? ""); alternates.Add(input);
            Action("Browse alternate location…",(_,_) => {
                var browse = new OpenFolderDialog { Title = "Choose an alternate marker pack folder", InitialDirectory = Directory.Exists(input.Text.Trim()) ? input.Text.Trim() : projectRoot };
                if (browse.ShowDialog(this) == true) input.Text = browse.FolderName;
            });
        }
        Label("Stuff Taco Now writes the same pack to every configured location. Empty or duplicate locations are skipped.");
        var includeDocs = new CheckBox { Content = "Include documentation in compiled .taco", IsChecked = includeDocumentation, Margin = new Thickness(0, 12, 0, 4) };
        Body.Children.Add(includeDocs);
        Label("Includes Markdown and plain-text documents. Uncheck to keep build notes only in your project. Scripts are included separately.");
        Label("Project assets");
        Label("Import images and TRL files, insert trails, and choose default icon, texture, and trail folders in Project Assets.");
        Action("Assets…",(_,_) => openAssets?.Invoke());
        Label("Right-click folders in Assets to choose defaults for XML, docs, scripts, icons, textures and trail data.");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Body.Children.Add(status);
        Action("Defaults", (_, _) =>
        {
            output.Text = "";
            includeDocs.IsChecked = true;

            foreach (var input in alternates) input.Text = "";
        });
        Action("Apply", (_, _) =>
        {
            try
            {
                string folder = output.Text.Trim();
                if (folder.Length != 0 && !Path.IsPathFullyQualified(folder))
                    throw new InvalidOperationException("The .taco output location must be an absolute folder path, or blank to use EXPORT.");
                var imagePaths = currentPaths?.Invoke() ?? current;
                Paths = imagePaths.Validated();
                AlternateOutputs = alternates.Select(t => t.Text.Trim()).ToList();
                if (AlternateOutputs.Any(p => p.Length > 0 && !Path.IsPathFullyQualified(p))) throw new InvalidOperationException("Alternate output locations must be absolute folder paths or blank.");
                AlternateOutputs = AlternateOutputs.Select(p => p.Length == 0 ? "" : Path.GetFullPath(p)).ToList();
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
