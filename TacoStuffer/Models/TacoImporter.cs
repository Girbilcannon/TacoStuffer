using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record TacoImportResult(string ProjectFile, ProjectSnapshot Snapshot, int ConvertedTrails, IReadOnlyList<string> SkippedTrails);

public static class TacoImporter
{
    public static string PackPath(string value)
    {
        string path = value.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(p => !ProjectStore.ValidFileName(p)))
            throw new InvalidOperationException("Invalid relative pack path: " + value);
        return path;
    }

    private static XDocument Xml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    // Treat archive names and sizes as untrusted input. Validate relative paths,
    // duplicate names and actual decompressed byte counts before creating a project.
    private static Dictionary<string, byte[]> ReadPack(string file)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(file);
        long total = 0;
        if (zip.Entries.Count > 100000) throw new InvalidOperationException("This archive has too many entries.");
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            string path = PackPath(entry.FullName);
            if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidOperationException("Linked pack entries cannot be imported.");
            if (entry.Length < 0 || entry.Length > 256 * 1024 * 1024 || entry.Length > 512L * 1024 * 1024 - total) throw new InvalidOperationException("This pack exceeds the 512 MB import limit or contains an asset larger than 256 MB.");
            total += entry.Length;
            if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && entry.Length > 32 * 1024 * 1024) throw new InvalidOperationException("An XML document exceeds the 32 MB import limit.");
            using var source = entry.Open();
            using var target = new MemoryStream();
            byte[] buffer = new byte[65536];
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (target.Length + count > entry.Length) throw new InvalidOperationException("Archive entry exceeds its declared size: " + path);
                target.Write(buffer, 0, count);
            }
            if (target.Length != entry.Length) throw new InvalidOperationException("Truncated archive entry: " + path);
            if (!result.TryAdd(path, target.ToArray())) throw new InvalidOperationException("Duplicate pack path: " + path);
        }
        return result;
    }

    public static string SuggestedName(string file)
    {
        var entries = ReadPack(file);
        foreach (var entry in entries.Where(e => e.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var xml = Xml(entry.Value);
            if (xml.Root?.Name.LocalName != "OverlayData") continue;
            var category = xml.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "MarkerCategory");
            string heading = (string?)category?.Attribute("DisplayName") ?? (string?)category?.Attribute("name") ?? "";
            if (ProjectStore.ValidFileName(heading)) return heading;
        }
        return Path.GetFileNameWithoutExtension(file);
    }

    public static EditableTrail ReadTrail(byte[] bytes, long number, string guid)
    {
        if (bytes.Length < 8 || (bytes.Length - 8) % 12 != 0) throw new InvalidOperationException("Invalid TRL length.");
        using var reader = new BinaryReader(new MemoryStream(bytes));
        if (reader.ReadInt32() != 0) throw new InvalidOperationException("Unsupported TRL version; expected version 0.");
        var trail = new EditableTrail { Number = number, Guid = guid, MapId = reader.ReadUInt32() };
        TrailSegment? segment = null;
        while (reader.BaseStream.Position < bytes.Length)
        {
            var point = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (!EditableTrails.Finite(point)) throw new InvalidOperationException("TRL contains non-finite coordinates.");
            if (point == Vector3.Zero)
            {
                segment = null;
                continue;
            }
            segment ??= trail.AddSegment();
            segment.Smoothness = segment.SourceSmoothness = 8;
            // Preserve every stored point, including coincident points.
            segment.Points.Add(new TrailPoint { Id = segment.NextPoint++, Position = point });
        }
        EditableTrails.Validate(trail);
        return trail;
    }

    public static TacoImportResult Import(string source, string name, string storage, string output, bool convert)
    {
        var entries = ReadPack(source);
        var documents = new List<(string Name, XDocument Xml)>();
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            string top = entry.Key.Split('/')[0];
            if (top.Equals("EXPORT", StringComparison.OrdinalIgnoreCase) || top.Equals(EditableTrails.Folder, StringComparison.OrdinalIgnoreCase) || top.Equals("CUSTOM_ATTRIBUTES", StringComparison.OrdinalIgnoreCase) || entry.Key.EndsWith(".cilantro", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Pack entry conflicts with project storage: " + entry.Key);
            if (entry.Key.Equals("__TacoStuffer_TestBuild.txt", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                var xml = Xml(entry.Value);
                if (xml.Root?.Name.LocalName == "OverlayData")
                {
                    foreach (var e in xml.Root.DescendantsAndSelf())
                    foreach (string canonical in new[] { "OverlayData", "MarkerCategory", "POIs", "POI", "Trail" })
                    if (e.Name.LocalName.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                    {
                        e.Name = canonical;
                        break;
                    }
                    foreach (var attribute in xml.Descendants().Attributes().ToList())
                    foreach (string canonical in new[] { "GUID", "MapID", "DisplayName", "type", "trailData", "iconFile", "texture", "xpos", "ypos", "zpos" })
                    if (attribute.Name.LocalName.Equals(canonical, StringComparison.OrdinalIgnoreCase) && attribute.Name.LocalName != canonical)
                    {
                        if (attribute.Parent!.Attribute(canonical) != null) throw new InvalidOperationException("Duplicate attribute with differing case: " + canonical);
                        var parent = attribute.Parent!;
                        string value = attribute.Value;
                        attribute.Remove();
                        parent.SetAttributeValue(canonical, value);
                        break;
                    }
                    documents.Add((entry.Key, xml));
                    continue;
                }
            }
            assets.Add(entry.Key, entry.Value);
        }
        if (documents.Count == 0) throw new InvalidOperationException("No OverlayData XML files were found in this pack.");
        var categories = documents.SelectMany(d => d.Xml.Descendants("MarkerCategory")).GroupBy(CategoryOperations.PathOf).ToDictionary(g => g.Key, g => g.First());
        var trails = new List<EditableTrail>();
        var usedGuids = documents.SelectMany(d => d.Xml.Descendants().Attributes("GUID")).Select(a => a.Value).ToHashSet(StringComparer.Ordinal);
        var skipped = new List<string>();
        var convertedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var elements = documents.SelectMany(d => d.Xml.Descendants("Trail")).ToList();
        var guidCounts = elements.Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("GUID"))).GroupBy(e => (string)e.Attribute("GUID")!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        string SourcePath(XElement e)
        {
            string value = AttributeRules.Effective(e, "trailData", "", categories);
            try
            {
                return PackPath(value);
            }
            catch (InvalidOperationException)
            {
                return value;
            }
        }
        var pathCounts = elements.GroupBy(SourcePath, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        if (convert)
            foreach (var element in elements)
            {
                string originalPath = AttributeRules.Effective(element, "trailData", "", categories);
                try
                {
                    string path = PackPath(originalPath);
                    if (!path.EndsWith(".trl", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The reference is not a .trl file.");
                    if (!entries.TryGetValue(path, out var bytes)) throw new InvalidOperationException("The trail file is missing from the pack.");
                    string guid = (string?)element.Attribute("GUID") ?? "";
                    if (!string.IsNullOrWhiteSpace(guid) && guidCounts[guid] > 1) throw new InvalidOperationException("Duplicate trail GUID; keeping every matching reference unconverted.");
                    if (string.IsNullOrWhiteSpace(guid)) do
                        {
                            guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                        }
                        while (!usedGuids.Add(guid));
                    var trail = ReadTrail(bytes, trails.Count + 1, guid);
                    string map = AttributeRules.Effective(element, "MapID", "", categories);
                    if (map.Length > 0 && (!uint.TryParse(map, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) || id != trail.MapId)) throw new InvalidOperationException("XML and TRL MapIDs differ.");
                    string target = path;
                    // Shared sources stay intact for unconverted references. Every
                    // editable copy gets its own output path, even the first one.
                    if (pathCounts[SourcePath(element)] > 1)
                    {
                        target = $"DATA/imported_trail_{trail.Number:0000}.trl";
                        while (entries.ContainsKey(target) || convertedPaths.Contains(target)) target = "DATA/" + Guid.NewGuid().ToString("N") + ".trl";
                    }
                    // Commit XML changes only after every conversion check succeeds.
                    element.SetAttributeValue("GUID", guid);
                    element.SetAttributeValue("MapID", trail.MapId);
                    element.SetAttributeValue("trailData", target);
                    convertedPaths.Add(target);
                    trails.Add(trail);
                }
                catch (InvalidOperationException ex)
                {
                    string label = string.IsNullOrWhiteSpace(originalPath) ? "(no trailData reference)" : originalPath;
                    skipped.Add(label + " — " + ex.Message);
                }
            }
        var code = assets.Where(e => e.Key.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)).Select(e => new ProjectCode(e.Key, Encoding.UTF8.GetString(e.Value))).ToList();
        var snapshot = new ProjectSnapshot(name, output, documents.Select((d, i) => new ProjectXml(d.Name, d.Xml.ToString(), i < 5)).ToList(), new Dictionary<string, string>(), CategoryOrder.Collect(documents.Select(d => d.Xml)), Trails: trails, NextTrailNumber: trails.Count + 1, ImportedAssets: assets.Keys.ToList(), CodeDocuments: code, AssetPaths: ProjectAssetPaths.Infer(documents.Select(d => d.Xml)));
        string project = ProjectStore.Create(snapshot, storage);
        try
        {
            string folder = Path.GetDirectoryName(project)!;
            foreach (var asset in assets)
            {
                string path = Path.Combine(folder, asset.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, asset.Value);
            }
            if (skipped.Count > 0)
            {
                string report = "TacoStuffer_Import_Report.txt";
                for (int index = 2; File.Exists(Path.Combine(folder, report)); index++) report = $"TacoStuffer_Import_Report_{index}.txt";
                File.WriteAllText(Path.Combine(folder, report), "TRAIL CONVERSION REPORT\n\n" + trails.Count + " trails converted; " + skipped.Count + " references left unconverted.\nOriginal XML references and available binary assets are retained. Missing files remain missing.\n\n" + string.Join(Environment.NewLine, skipped));
            }
            return new(project, snapshot, trails.Count, skipped);
        }
        catch
        {
            Directory.Delete(Path.GetDirectoryName(project)!, true);
            throw;
        }
    }
}
