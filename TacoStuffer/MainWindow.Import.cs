using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private sealed record CodePage(string Name, ICSharpCode.AvalonEdit.TextEditor Editor, TabItem Tab, CodeDocumentView View);

    private readonly List<CodePage> codePages = [];
    private readonly List<string> importedAssets = [];
    private readonly Dictionary<string, (DateTime Stamp, EditableTrail Trail)> rawTrailPreview = new(StringComparer.OrdinalIgnoreCase);
    private void RegisterCode(ProjectCode source)
    {
        var view = new CodeDocumentView(source.Name, source.Content, () => projectFolder);
        var editor = view.Editor;
        var tab = new TabItem { Content = view };
        codePages.Add(new(source.Name, editor, tab, view));
        view.Editor.TextChanged += (_, _) => { if (!updating) StatusText.Text = view.DocumentName + " modified · Save Project to retain changes."; };
        view.PreviewKeyDown += ProjectShortcut;
        SetTabHeader(tab, source.Name);
        tab.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F2 && codePages.FirstOrDefault(p => p.Tab == tab) is { } current)
            { e.Handled = true; RenameCodeDocument(current); }
        };
        // Code remains discoverable in Elements, including after its tab closes.
    }

    private async void ImportPack()
    {
        if (!AllowProjectChange()) return;
        var dialog = new ImportTacoWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            string source = dialog.SourceFile, name = dialog.ProjectName, storage = dialog.ProjectLocation, output = dialog.OutputLocation;
            bool convert = false;
            StopTrailRecording();
            overlay?.Close();
            workspaceBusy = true;
            IsEnabled = false;
            var result = await System.Threading.Tasks.Task.Run(() => TacoImporter.Import(source, name, storage, output, convert));
            ApplyProject(result.ProjectFile, result.Snapshot);
            StatusText.Text = $"Imported {result.Snapshot.Documents.Count} XML documents; {result.ConvertedTrails} editable trails; {result.SkippedTrails.Count} skipped conversions. Original .taco retained.";
            if (result.SkippedTrails.Count > 0)
            {
                IsEnabled = true;
                Notice("Import completed with skipped trails", $"{result.ConvertedTrails} trails converted into editable points. {result.SkippedTrails.Count} references were left unconverted.\n\nTheir original XML references and any available .trl files were kept. Missing files are still missing and cannot be previewed.\n\n" + string.Join("\n", result.SkippedTrails.Take(12)) + (result.SkippedTrails.Count > 12 ? "\n…" : "") + "\n\nA full conversion report was saved in the project folder.");
            }
        }
        catch (Exception ex) when (ProjectError(ex))
        {
            Notice("Import failed", ex.Message);
        }
        finally
        {
            workspaceBusy = false;
            IsEnabled = true;
        }
    }

    private EditableTrail? RawTrailFor(XElement element, IReadOnlyDictionary<string, XElement> categories)
    {
        if (projectFolder == null) return null;
        try
        {
            string xmlMap = AttributeRules.Effective(element, "MapID", "", categories);
            if (uint.TryParse(xmlMap, out uint knownMap) && knownMap != gameFrame?.MapId) return null;
            string relative = TacoImporter.PackPath(AttributeRules.Effective(element, "trailData", "", categories));
            string file = Path.Combine(projectFolder, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) return null;
            DateTime stamp = File.GetLastWriteTimeUtc(file);
            if (!rawTrailPreview.TryGetValue(file, out var cached) || cached.Stamp != stamp)
            {
                using (var header = new System.IO.BinaryReader(File.OpenRead(file))) if (header.BaseStream.Length < 8 || header.ReadInt32() != 0 || header.ReadUInt32() != gameFrame?.MapId) return null;
                string guid = Value(element, "GUID", "raw:" + relative);
                long previewNumber = cached.Trail is { } previous ? previous.Number : long.MaxValue - rawTrailPreview.Count;
                var trail = TacoImporter.ReadTrail(File.ReadAllBytes(file), previewNumber, guid);
                rawTrailPreview[file] = cached = (stamp, trail);
            }
            return cached.Trail;
        }
        catch (Exception ex) when (ProjectError(ex))
        {
            System.Diagnostics.Debug.WriteLine("Binary trail preview: " + ex.Message);
            return null;
        }
    }
}
