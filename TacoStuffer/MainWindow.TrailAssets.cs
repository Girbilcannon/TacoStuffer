using System.IO;
using System.Xml.Linq;
using TacoStuffer.Models;

namespace TacoStuffer;

public partial class MainWindow
{
    private void InsertTrailAssets(IReadOnlyList<string> paths, string category, bool makeEditable)
    {
        if (projectFolder == null || !TrySyncXml()) throw new InvalidOperationException("Resolve XML errors before inserting trails.");
        var documents = pages.Select(p => p.Xml).ToList();
        var plan = TrailAssetInsertion.Plan(projectFolder, paths, documents, category, assetPaths.TrailFiles, nextTrailNumber, editableTrails, makeEditable);
        var target = pages.First(p => p.Xml.Descendants("MarkerCategory").Any(c => CategoryFullPath(c) == category));
        var created = new List<string>();
        try
        {
            foreach (var item in plan.Where(p => p.Copy))
            {
                string full = AssetLibrary.Resolve(projectFolder,item.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                using (var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
                {
                    created.Add(full);
                    file.Write(item.Bytes);
                }
            }
        }
        catch
        {
            foreach (string file in created) File.Delete(file);
            throw;
        }
        FlushHistoryCheckpoint();
        var pois = target.Xml.Root!.Element("POIs");
        if (pois == null) { pois = new XElement("POIs"); target.Xml.Root.Add(pois); }
        foreach (var item in plan)
        {
            pois.Add(item.Element);
            if (item.Editable != null) editableTrails.Add(item.Editable);
            if (!importedAssets.Contains(item.Path,StringComparer.OrdinalIgnoreCase)) importedAssets.Add(item.Path);
        }
        nextTrailNumber = Math.Max(nextTrailNumber, editableTrails.Select(t => t.Number + 1).DefaultIfEmpty(1).Max());
        WritePages();
        QueueHistoryCheckpoint("Insert TRL assets");
        StatusText.Text = $"Inserted {plan.Count} trail(s) into {category}. Save the project to retain them.";
    }
}
