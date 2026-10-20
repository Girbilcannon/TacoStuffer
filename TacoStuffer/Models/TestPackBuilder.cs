using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record TestPackResult(int XmlFiles, int Assets, IReadOnlyList<string> Placeholders);

public static class TestPackBuilder
{
    // PNG fallback is a visible magenta tile, not a renamed empty image file.
    private static readonly byte[] PlaceholderPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAALElEQVR4nGPQ5df9j4z/M/xHwYTkGYaBAaRqQJcfDgYMfCwMvAEDHwsDbgAA9isynzCdjzYAAAAASUVORK5CYII=");
    private static readonly byte[] DefaultTrailPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAm0lEQVR4nO2WsQ3AMAgE2b/ICpknUyWtG8BPnkeRYsnt32FjZLOvrvM67nWPwmUSHlgisgunS6BgqggSTJWoVkU5DUYlpQz2XUJ5nd2cZiueUsiQDRJHRD7TXYFusCdiE/BVYor9fql7J4TLJLLp1iqyC6dLoGCqCBJMlahWRTkNRiWlDPZdQnmd3ZxmK55SyJANEkfk/w+M/wce92Cybjs7oZ0AAAAASUVORK5CYII=");
    private static string PackPath(string value)
    {
        string path = value.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidOperationException("Use a relative pack path without parent traversal: " + value);
        return path;
    }

    public static TestPackResult Build(string output, IReadOnlyList<(string Name, string Xml)> documents, string? projectFolder, IReadOnlyList<EditableTrail>? editableTrails = null, IReadOnlyList<string>? importedAssets = null, ProjectAssetPaths? assetPaths = null, IReadOnlyList<ProjectCode>? codeDocuments = null, bool includeDocumentation = true)
    {
        var folders = (assetPaths ?? new()).Validated();
        var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var placeholders = new List<string>();
        var xmls = documents.Select(d => (Name: PackPath(d.Name), Xml: XDocument.Parse(d.Xml))).ToList();
        if (xmls.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != xmls.Count) throw new InvalidOperationException("XML filenames must be unique.");
        var editableByGuid = new Dictionary<string, EditableTrail>(StringComparer.Ordinal);
        foreach (var editable in editableTrails ?? Array.Empty<EditableTrail>())
        {
            EditableTrails.Validate(editable);
            if (!editableByGuid.TryAdd(editable.Guid, editable)) throw new InvalidOperationException("Duplicate editable trail GUID: " + editable.Guid);
        }
        var referenceCounts = xmls.SelectMany(d => d.Xml.Descendants("Trail"))
        .GroupBy(t => (string?)t.Attribute("GUID") ?? "", StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var editable in editableByGuid.Values)
        {
            referenceCounts.TryGetValue(editable.Guid, out int matches);
            if (matches != 1)
                throw new InvalidOperationException($"Recorded trail {editable.Number:0000} must have exactly one XML Trail element with GUID {editable.Guid}; found {matches}. Restore its GUID or remove the recorded trail before building.");
        }
        if (projectFolder != null)
        {
            foreach (string folder in new[] { folders.MarkerIcons, folders.TrailTextures, folders.TrailFiles }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(projectFolder, folder);
                if (!Directory.Exists(path)) continue;
                foreach (string file in ProjectAssetPaths.Files(projectFolder, folder))
                {
                    string relative = PackPath(Path.GetRelativePath(projectFolder, file));
                    if (!includeDocumentation && ProjectDocuments.IsDocumentation(relative)) continue;
                    if (!xmls.Any(d => d.Name.Equals(relative, StringComparison.OrdinalIgnoreCase))) entries.TryAdd(relative, File.ReadAllBytes(file));
                }
            }
        }
        if (projectFolder != null)
            foreach (string imported in importedAssets ?? [])
            {
                string path = TacoImporter.PackPath(imported);
                if (!includeDocumentation && ProjectDocuments.IsDocumentation(path)) continue;
                string top = path.Split('/')[0];
                if (top.Equals("EXPORT", StringComparison.OrdinalIgnoreCase) || top.Equals(EditableTrails.Folder, StringComparison.OrdinalIgnoreCase) || top.Equals("CUSTOM_ATTRIBUTES", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cilantro", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Project storage cannot be packaged: " + path);
                string assetRoot = Path.GetFullPath(projectFolder);
                string full = Path.Combine(assetRoot, path.Replace('/', Path.DirectorySeparatorChar));
                string check = full;
                while (!check.Equals(assetRoot, StringComparison.OrdinalIgnoreCase))
                {
                    if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked imported assets cannot be packaged: " + path);
                    check = Path.GetDirectoryName(check)!;
                }
                if (!entries.ContainsKey(path)) entries.Add(path, File.ReadAllBytes(full));
            }
        foreach (var code in codeDocuments ?? [])
        {
            string path = PackPath(code.Name);
            if (ProjectDocuments.Kind(path) == null) throw new InvalidOperationException("Unsupported source/document: " + path);
            if (!includeDocumentation && ProjectDocuments.IsDocumentation(path)) continue;
            // Use the current tracked text, including closed tabs, over any disk copy.
            entries[path] = Encoding.UTF8.GetBytes(code.Content);
        }
        var categories = xmls.SelectMany(d => d.Xml.Descendants("MarkerCategory")).GroupBy(CategoryOperations.PathOf).ToDictionary(g => g.Key, g => g.First());
        var compiledPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in xmls)
        {
            foreach (var trail in doc.Xml.Descendants("Trail"))
            {
                string guid = (string?)trail.Attribute("GUID") ?? "";
                if (!editableByGuid.TryGetValue(guid, out var editable)) continue;
                string path = PackPath((string?)trail.Attribute("trailData") ?? ProjectAssetPaths.FilePath(folders.TrailFiles, $"trail_{editable.Number:0000}.trl"));
                if (!path.EndsWith(".trl", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Trail data must use .trl: " + path);
                if (!uint.TryParse((string?)trail.Attribute("MapID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint xmlMap) || xmlMap != editable.MapId)
                    throw new InvalidOperationException("Editable trail MapID differs from its XML or is invalid: " + guid);
                if (compiledPaths.TryGetValue(path, out var other) && other != guid) throw new InvalidOperationException("Two editable trails target the same TRL path: " + path);
                compiledPaths[path] = guid;
                if (AttributeRules.Effective(trail, "texture", "", categories).Length == 0)
                {
                    string fallbackTexture = ProjectAssetPaths.FilePath(folders.TrailTextures, "__TacoStuffer_DefaultTrail.png");
                    trail.SetAttributeValue("texture", fallbackTexture);
                    entries.TryAdd(fallbackTexture, DefaultTrailPng);
                }
                trail.SetAttributeValue("trailData", path);
                entries[path] = EditableTrails.Compile(editable);
            }
            foreach (var attribute in doc.Xml.Descendants().Attributes().Where(a => a.Name.LocalName is "iconFile" or "texture" or "trailData").ToList())
            {
                if (attribute.Value.Length == 0) continue;
                string path = PackPath(attribute.Value);
                attribute.Value = path;
                if (entries.ContainsKey(path)) continue;
                if (projectFolder != null)
                {
                    string root = Path.GetFullPath(projectFolder), full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(full))
                    {
                        string check = full;
                        while (!check.Equals(root, StringComparison.OrdinalIgnoreCase))
                        {
                            if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked XML assets cannot be packaged: " + path);
                            check = Path.GetDirectoryName(check)!;
                        }
                        entries.Add(path, File.ReadAllBytes(full));
                        continue;
                    }
                }
                if (attribute.Name.LocalName == "trailData")
                {
                    if (!path.EndsWith(".trl", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Trail data must use a .trl path: " + path);
                    using var data = new MemoryStream();
                    using var writer = new BinaryWriter(data);
                    writer.Write(0); // TacO TRAILFILEVERSION, followed by map ID and XYZ float triples.
                    int.TryParse((string?)attribute.Parent?.Attribute("MapID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int map);
                    writer.Write(map);
                    writer.Flush();
                    entries.Add(path, data.ToArray());
                    placeholders.Add(path + " — empty trail, map " + map + "; no recorded points");
                }
                else
                {
                    // Do not put PNG bytes in a missing DDS/JPG file. Adjust only
                    // the exported snapshot to a unique PNG fallback instead.
                    if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".placeholder.png";
                    attribute.Value = path;
                    if (!entries.ContainsKey(path))
                    {
                        entries.Add(path, PlaceholderPng);
                        placeholders.Add(path + " — placeholder image");
                    }
                }
            }
        }
        int assets = entries.Count;
        foreach (var doc in xmls)
        {
            // Formatting is regenerated after asset-path substitutions.
            foreach (var container in doc.Xml.Descendants())
            {
                if (container.Name == "MarkerCategory" && container.Nodes().All(n => n is XText text && string.IsNullOrWhiteSpace(text.Value)))
                {
                    container.RemoveNodes();
                    continue;
                }
                if (container.Elements().Any() && !container.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
                    foreach (var whitespace in container.Nodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList()) whitespace.Remove();
            }
            if (!entries.TryAdd(doc.Name, Encoding.UTF8.GetBytes(doc.Xml.ToString()))) throw new InvalidOperationException("Asset/XML path collision: " + doc.Name);
        }
        const string reportName = "__TacoStuffer_TestBuild.txt";
        if (entries.ContainsKey(reportName)) throw new InvalidOperationException("Reserved test report filename.");
        entries.Add(reportName, Encoding.UTF8.GetBytes("TACO STUFFER TEST PACK — not a production build\n\n" + xmls.Count + " XML documents; " + assets + " assets.\nClosed XML tabs are included. Configured asset folders and XML-linked files are copied; linked editable trails compile to TRL. EDITABLE_TRAILS is excluded.\nEmpty TRL placeholders contain only version/map headers, no route points; map 0 means unknown.\nRepeated category declarations may establish shared order. Compatibility must be tested in each target reader.\n\nGenerated placeholders:\n" + (placeholders.Count == 0 ? "None" : string.Join('\n', placeholders))));
        string finalPath = Path.GetFullPath(output);
        string temp = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    foreach (var entry in entries)
                    {
                        using var bytes = zip.CreateEntry(entry.Key, CompressionLevel.Optimal).Open();
                        bytes.Write(entry.Value);
                    }
            File.Move(temp, finalPath, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return new TestPackResult(xmls.Count, assets, placeholders);
    }

    private static IEnumerable<string> FilesWithoutLinks(string folder)
    {
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked asset folders are not supported: " + folder);
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked assets are not supported: " + entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var file in FilesWithoutLinks(entry)) yield return file;
            }
            else yield return entry;
        }
    }
}
