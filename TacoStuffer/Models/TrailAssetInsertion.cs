using System.IO;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record InsertedTrailAsset(string Source, string Path, byte[] Bytes, XElement Element, EditableTrail? Editable, bool Copy);

public static class TrailAssetInsertion
{
    // Validate and plan the entire batch before copying files or writing XML.
    public static IReadOnlyList<InsertedTrailAsset> Plan(string root, IEnumerable<string> paths, IReadOnlyList<XDocument> documents,
        string category, string defaultFolder, long nextNumber, IReadOnlyList<EditableTrail> existing, bool makeEditable)
    {
        if (!documents.SelectMany(d => d.Descendants("MarkerCategory")).Any(c => CategoryOperations.PathOf(c) == category))
            throw new InvalidOperationException("Choose an existing project category before inserting trails.");
        defaultFolder = ProjectAssetPaths.Folder(defaultFolder);
        var elements = documents.SelectMany(d => d.Descendants()).ToList();
        var names = elements.Where(e => e.Name.LocalName is "POI" or "Trail").Select(e => (string?)e.Attribute("name") ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var occupied = elements.Attributes().Where(a => a.Name.LocalName.Equals("trailData", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Value.Replace('\\','/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long number = Math.Max(nextNumber, existing.Select(t => t.Number + 1).DefaultIfEmpty(1).Max());
        var result = new List<InsertedTrailAsset>();
        foreach (string source in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!AssetLibrary.IsTrail(source)) throw new InvalidOperationException("Select TRL files only for insertion.");
            string full = AssetLibrary.Resolve(root, source);
            byte[] bytes = File.ReadAllBytes(full);
            string guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var trail = TacoImporter.ReadTrail(bytes, number++, guid);
            if (!trail.Segments.Any(s => s.Points.Count > 0)) throw new InvalidOperationException(Path.GetFileName(source) + " contains no trail points.");
            string stem = Path.GetFileNameWithoutExtension(source);
            string nameBase = string.Concat(stem.Select(c => char.IsWhiteSpace(c) ? '_' : c));
            if (nameBase.Length == 0) nameBase = "trail";
            string name = nameBase;
            for (int i = 1; !names.Add(name); i++) name = nameBase + "_" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            string path = TacoImporter.PackPath(source);
            bool copy = occupied.Contains(path);
            if (copy)
            {
                string candidate = ProjectAssetPaths.FilePath(defaultFolder, stem + ".trl");
                for (int i = 1; occupied.Contains(candidate) || File.Exists(AssetLibrary.Resolve(root,candidate)) || Directory.Exists(AssetLibrary.Resolve(root,candidate)); i++)
                    candidate = ProjectAssetPaths.FilePath(defaultFolder, stem + "_" + i + ".trl");
                path = candidate;
            }
            occupied.Add(path);
            var element = new XElement("Trail", new XAttribute("name",name), new XAttribute("DisplayName",stem), new XAttribute("GUID",guid),
                new XAttribute("MapID",trail.MapId), new XAttribute("type",category), new XAttribute("trailData",path));
            result.Add(new(source,path,bytes,element,makeEditable ? trail : null,copy));
        }
        if (result.Count == 0) throw new InvalidOperationException("Select one or more TRL files first.");
        return result;
    }
}
