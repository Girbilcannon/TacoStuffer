using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

public sealed class ImportTacoWindow : PreviewDialog
{
    private readonly TextBox source, name, storage, output;
    public string SourceFile => source.Text;
    public string ProjectName => name.Text.Trim();
    public string ProjectLocation => storage.Text.Trim();
    public string OutputLocation => output.Text.Trim();
    public ImportTacoWindow() : base("Import .taco File", 620)
    {
        Label("SELECT PACK");
        source = Input("");
        source.IsReadOnly = true;
        Label("Project name · suggested from the main category heading");
        name = Input("");
        Label("Project storage location");
        storage = Input(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TacoStuffer"));
        Action("Browse project location…", (_, _) =>
        {
            var d = new OpenFolderDialog();
            if (d.ShowDialog(this) == true) storage.Text = d.FolderName;
        });
        Label("Compiled .taco output folder · optional");
        output = Input("");
        Action("Browse output location…", (_, _) =>
        {
            var d = new OpenFolderDialog();
            if (d.ShowDialog(this) == true) output.Text = d.FolderName;
        });
        Label("Leave output blank to build into the new project's EXPORT folder. The original pack is left untouched during import.");
        Label("Trail files are imported unchanged. To edit points, right-click a trail in Elements and choose Make Editable. Only that trail is converted; its original binary file is retained.");
        Label("Lua support is planned for a future update. Lua files can be edited as code, but Taco Stuffer does not execute them or preview their effects.");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Body.Children.Add(status);
        var import = Action("Import Project", (_, _) =>
        {
            if (!File.Exists(SourceFile))
            {
                status.Text = "Select a .taco file first.";
                return;
            }
            if (!ProjectStore.ValidFileName(ProjectName))
            {
                status.Text = "Enter a valid Windows project name. Spaces are allowed.";
                return;
            }
            if (!Path.IsPathFullyQualified(ProjectLocation) || (OutputLocation.Length > 0 && !Path.IsPathFullyQualified(OutputLocation)))
            {
                status.Text = "Choose absolute folder locations.";
                return;
            }
            DialogResult = true;
        });
        var select = new Button { Content = "Select .taco File…", Margin = new Thickness(0, 8, 0, 0) };
        Body.Children.Insert(2, select);
        bool userNamed = false, suggesting = false;
        name.TextChanged += (_, _) =>
        {
            if (!suggesting) userNamed = true;
        };
        select.Click += async (_, _) =>
        {
            var open = new OpenFileDialog { Title = "Select a marker pack", Filter = "TacO marker pack (*.taco)|*.taco" };
            if (open.ShowDialog(this) != true) return;
            source.Text = open.FileName;
            select.IsEnabled = import.IsEnabled = false;
            status.Text = "Reading pack heading…";
            try
            {
                string heading = await System.Threading.Tasks.Task.Run(() => TacoImporter.SuggestedName(open.FileName));
                if (!userNamed)
                {
                    suggesting = true;
                    name.Text = heading;
                    suggesting = false;
                }
                status.Text = "Ready to import. The project name does not rename existing categories.";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException or ArgumentException)
            {
                source.Text = "";
                status.Text = "Could not read this pack: " + ex.Message;
            }
            finally
            {
                select.IsEnabled = import.IsEnabled = true;
            }
        };
        Action("Cancel", (_, _) => DialogResult = false);
    }
}
