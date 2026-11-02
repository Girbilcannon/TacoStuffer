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
    public ProjectSettingsWindow(string projectRoot, ProjectAssetPaths current, string currentOutputFolder, bool includeDocumentation = true, IReadOnlyList<string>? alternateOutputs = null) : base("Project Settings", 610)
    {
        SizeToContent = SizeToContent.Manual;
        Height = Math.Min(560, MaxHeight);
        MinHeight = Math.Min(340, Height);
        MinWidth = 500;
        ResizeMode = ResizeMode.CanResizeWithGrip;
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
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Body.Children.Add(status);
        var defaultsButton = Action("Defaults", (_, _) =>
        {
            output.Text = "";
            includeDocs.IsChecked = true;

            foreach (var input in alternates) input.Text = "";
        });
        var applyButton = Action("Apply", (_, _) =>
        {
            try
            {
                string folder = output.Text.Trim();
                if (folder.Length != 0 && !Path.IsPathFullyQualified(folder))
                    throw new InvalidOperationException("The .taco output location must be an absolute folder path, or blank to use EXPORT.");
                Paths = current.Validated();
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
        var cancelButton = Action("Cancel", (_, _) => DialogResult = false);
        applyButton.IsDefault = true;
        cancelButton.IsCancel = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 16) };
        foreach (var button in new[] { defaultsButton, applyButton, cancelButton })
        {
            Body.Children.Remove(button);
            button.Margin = new Thickness(8, 0, 0, 0);
            button.MinWidth = 90;
            buttons.Children.Add(button);
        }
        var layout = (DockPanel)((Border)Content).Child;
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Insert(1, buttons);
    }
}
