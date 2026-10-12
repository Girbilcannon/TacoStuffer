using System.IO;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record ProjectCode(string Name, string Content);

public sealed record ProjectXml(string Name, string Content, bool Open = true);

public sealed record ProjectSnapshot(string Name, string OutputFolder, List<ProjectXml> Documents, Dictionary<string, string> TextureLabels, List<string>? CategoryOrder = null, List<string>? CollapsedElements = null, long NextMarkerNumber = 1, List<EditableTrail>? Trails = null, long NextTrailNumber = 1, List<string>? ImportedAssets = null, List<ProjectCode>? CodeDocuments = null, ProjectAssetPaths? AssetPaths = null);

public static class ProjectStore
{
    public static bool ValidFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' ')) return false;
        string device = name.Split('.')[0].ToUpperInvariant();
        return !(device is "CON" or "PRN" or "AUX" or "NUL" || (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] is >= '1' and <= '9'));
    }

    public static string TacoName(string name)
    {
        string result = new string(name.Where(c => !char.IsWhiteSpace(c) && !Path.GetInvalidFileNameChars().Contains(c)).ToArray()) + ".taco";
        return ValidFileName(result) ? result : "Pack_" + result;
    }

    private static XDocument ReadXml(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static XDocument ReadContent(string text)
    {
        using var source = new StringReader(text);
        using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    public static bool ValidPackPath(string path)
    {
        try
        {
            return TacoImporter.PackPath(path) == path.Replace('\\', '/');
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static List<string> Names(XDocument manifest)
    {
        var root = manifest.Root;
        if (root?.Name != "CilantroProject" || (string?)root.Attribute("version") != "1") throw new InvalidOperationException("Unsupported .cilantro project format.");
        var documents = root.Element("Documents") ?? throw new InvalidOperationException("Project document list is missing.");
        var names = documents.Elements("Xml").Select(e => (string?)e.Attribute("file") ?? "").ToList();
        if (names.Any(n => !ValidPackPath(n) || !n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            throw new InvalidOperationException("Project XML filenames must be valid, unique relative paths.");
        return names;
    }

    public static ProjectSnapshot Load(string file)
    {
        var manifest = ReadXml(file);
        var names = Names(manifest);
        var root = manifest.Root!;
        string name = (string?)root.Attribute("name") ?? "";
        if (!ValidFileName(name)) throw new InvalidOperationException("Invalid project name.");
        string folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var nodes = root.Element("Documents")!.Elements("Xml").ToList();
        var documents = new List<ProjectXml>();
        for (int i = 0; i < names.Count; i++)
        {
            var xml = ReadXml(Path.Combine(folder, names[i]));
            if (xml.Root?.Name != "OverlayData") throw new InvalidOperationException("XML root must be OverlayData: " + names[i]);
            documents.Add(new(names[i], xml.ToString(), (string?)nodes[i].Attribute("open") != "false"));
        }
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in root.Element("TextureLabels")?.Elements("Texture") ?? Enumerable.Empty<XElement>())
        labels[(string?)label.Attribute("path") ?? ""] = (string?)label.Attribute("displayName") ?? "";
        var order = root.Element("CategoryOrder")?.Elements("Category").Select(c => (string?)c.Attribute("path") ?? "").ToList();
        var collapsed = root.Element("ElementsView")?.Elements("Collapsed").Select(c => (string?)c.Attribute("key") ?? "").ToList();
        string? savedCounter = (string?)root.Attribute("nextMarkerNumber");
        long nextMarker = 1;
        if (savedCounter != null && (!long.TryParse(savedCounter, NumberStyles.None, CultureInfo.InvariantCulture, out nextMarker) || nextMarker < 1)) throw new InvalidOperationException("Invalid next marker number.");
        var numbers = new List<long>();
        foreach (var item in root.Element("EditableTrails")?.Elements("Trail") ?? Enumerable.Empty<XElement>())
        {
            if (!long.TryParse((string?)item.Attribute("number"), out long number) || number < 1) throw new InvalidOperationException("Invalid editable trail reference.");
            numbers.Add(number);
        }
        var trails = EditableTrails.Load(folder, numbers);
        long nextTrail = 1;
        if (root.Attribute("nextTrailNumber") != null && (!long.TryParse((string?)root.Attribute("nextTrailNumber"), out nextTrail) || nextTrail < 1)) throw new InvalidOperationException("Invalid next trail number.");
        nextTrail = Math.Max(nextTrail, trails.Select(t => t.Number + 1).DefaultIfEmpty(1).Max());
        var assets = root.Element("ImportedAssets")?.Elements("File").Select(e => (string?)e.Attribute("path") ?? "").ToList() ?? [];
        var codeNames = root.Element("CodeDocuments")?.Elements("File").Select(e => (string?)e.Attribute("path") ?? "").ToList() ?? [];
        if (assets.Concat(codeNames).Any(p => !ValidPackPath(p))) throw new InvalidOperationException("Invalid imported asset path.");
        var pathsNode = root.Element("AssetPaths");
        var assetPaths = new ProjectAssetPaths((string?)pathsNode?.Attribute("markerIcons") ?? "TEXTURES", (string?)pathsNode?.Attribute("trailTextures") ?? "TEXTURES", (string?)pathsNode?.Attribute("trailFiles") ?? "DATA").Validated();
        var code = codeNames.Select(p => new ProjectCode(p, File.ReadAllText(Path.Combine(folder, p)))).ToList();
        return new(name, (string?)root.Attribute("tacoOutputFolder") ?? "", documents, labels, order, collapsed, nextMarker, trails, nextTrail, assets, code, pathsNode == null ? null : assetPaths);
    }

    public static string Create(ProjectSnapshot snapshot, string parentFolder)
    {
        if (!ValidFileName(snapshot.Name)) throw new InvalidOperationException("Use a valid Windows project name. Spaces are allowed.");
        if (!Path.IsPathFullyQualified(parentFolder)) throw new InvalidOperationException("Choose an absolute project save location.");
        string folder = Path.Combine(parentFolder, snapshot.Name);
        if (Directory.Exists(folder) || File.Exists(folder)) throw new InvalidOperationException("The project folder already exists. Choose another name or location.");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string child in new[] { "CUSTOM_ATTRIBUTES", EditableTrails.Folder }.Concat(new[] { (snapshot.AssetPaths ?? new()).Validated().MarkerIcons, (snapshot.AssetPaths ?? new()).Validated().TrailTextures, (snapshot.AssetPaths ?? new()).Validated().TrailFiles }).Distinct(StringComparer.OrdinalIgnoreCase)) Directory.CreateDirectory(Path.Combine(folder, child));
            string file = Path.Combine(folder, snapshot.Name + ".cilantro");
            Save(file, snapshot);
            return file;
        }
        catch
        {
            Directory.Delete(folder, true);
            throw;
        }
    }

    public static string ExportPath(string projectFile, ProjectSnapshot snapshot)
    {
        string projectFolder = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
        string outputFolder = snapshot.OutputFolder.Length == 0 ? Path.Combine(projectFolder, "EXPORT") : snapshot.OutputFolder;
        if (!Path.IsPathFullyQualified(outputFolder)) throw new InvalidOperationException("The .taco output location must be an absolute folder path.");
        string output = Path.GetFullPath(outputFolder);
        foreach (string assetFolder in new[] { (snapshot.AssetPaths ?? new()).Validated().MarkerIcons, (snapshot.AssetPaths ?? new()).Validated().TrailTextures, (snapshot.AssetPaths ?? new()).Validated().TrailFiles })
        {
            if (assetFolder == ".") continue;
            string assetRoot = Path.GetFullPath(Path.Combine(projectFolder, assetFolder));
            if (output.Equals(assetRoot, StringComparison.OrdinalIgnoreCase) || output.StartsWith(assetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose an export folder outside the configured asset folders.");
        }
        return Path.Combine(output, TacoName(snapshot.Name));
    }

    // Validate and prepare writes before replacing tracked files. The backups below
    // support rollback after a failed write; this is not a filesystem transaction
    // that can guarantee recovery from a process or machine crash mid-save.
    public static void Save(string file, ProjectSnapshot snapshot)
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("Project.Save");
        string folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        if (!Directory.Exists(folder)) throw new InvalidOperationException("Project folder is missing.");
        if (!ValidFileName(snapshot.Name)) throw new InvalidOperationException("Invalid project name.");
        if (snapshot.NextTrailNumber < 1) throw new InvalidOperationException("Invalid next trail number.");
        if (snapshot.NextMarkerNumber < 1) throw new InvalidOperationException("Invalid next marker number.");
        var assetPaths = (snapshot.AssetPaths ?? new()).Validated();
        var manifest = new XDocument(new XElement("CilantroProject", new XAttribute("version", "1"), new XAttribute("name", snapshot.Name), new XAttribute("tacoOutputFolder", snapshot.OutputFolder), new XAttribute("nextMarkerNumber", snapshot.NextMarkerNumber), new XAttribute("nextTrailNumber", snapshot.NextTrailNumber),
            new XElement("AssetPaths", new XAttribute("markerIcons", assetPaths.MarkerIcons), new XAttribute("trailTextures", assetPaths.TrailTextures), new XAttribute("trailFiles", assetPaths.TrailFiles)),
            new XElement("ImportedAssets", (snapshot.ImportedAssets ?? []).Select(p => new XElement("File", new XAttribute("path", p)))),
            new XElement("CodeDocuments", (snapshot.CodeDocuments ?? []).Select(p => new XElement("File", new XAttribute("path", p.Name)))),
            new XElement("EditableTrails", (snapshot.Trails ?? []).Select(t => new XElement("Trail", new XAttribute("number", t.Number)))),
            new XElement("Documents", snapshot.Documents.Select(d => new XElement("Xml", new XAttribute("file", d.Name), new XAttribute("open", d.Open ? "true" : "false")))),
            new XElement("TextureLabels", snapshot.TextureLabels.Select(p => new XElement("Texture", new XAttribute("path", p.Key), new XAttribute("displayName", p.Value)))),
            new XElement("CategoryOrder", (snapshot.CategoryOrder ?? []).Select(p => new XElement("Category", new XAttribute("path", p)))),
            new XElement("ElementsView", (snapshot.CollapsedElements ?? []).Select(p => new XElement("Collapsed", new XAttribute("key", p))))));
        var names = Names(manifest);
        _ = ExportPath(file, snapshot);
        if ((snapshot.ImportedAssets ?? []).Concat((snapshot.CodeDocuments ?? []).Select(c => c.Name)).Any(p => !ValidPackPath(p))) throw new InvalidOperationException("Invalid imported asset path.");
        List<string> oldNames = File.Exists(file) ? Names(ReadXml(file)) : [];
        var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in snapshot.Documents)
        {
            if (ReadContent(doc.Content).Root?.Name != "OverlayData") throw new InvalidOperationException("Invalid XML root: " + doc.Name);
            string path = Path.Combine(folder, doc.Name);
            if (File.Exists(path) && !oldNames.Contains(doc.Name, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("An untracked XML already exists: " + doc.Name);
            writes.Add(path, Encoding.UTF8.GetBytes(doc.Content));
        }
        foreach (var code in snapshot.CodeDocuments ?? []) writes.Add(Path.Combine(folder, code.Name), Encoding.UTF8.GetBytes(code.Content));
        Directory.CreateDirectory(Path.Combine(folder, EditableTrails.Folder));
        foreach (var trail in snapshot.Trails ?? []) writes.Add(Path.Combine(folder, EditableTrails.Folder, EditableTrails.FileName(trail.Number)), Encoding.UTF8.GetBytes(EditableTrails.Serialize(trail)));
        writes.Add(Path.GetFullPath(file), Encoding.UTF8.GetBytes(manifest.ToString()));
        var obsolete = oldNames.Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase)).Select(n => Path.Combine(folder, n)).ToList();
        var backup = writes.Keys.Concat(obsolete).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null, StringComparer.OrdinalIgnoreCase);
        try
        {
            // Preserve timestamps and avoid rewriting all XMLs/trails when only
            // one object changed. Backups still cover every changed target.
            foreach (var item in writes)
            if (backup[item.Key] is not { } previous || !previous.AsSpan().SequenceEqual(item.Value)) AtomicWrite(item.Key, item.Value);
            foreach (string path in obsolete) if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            foreach (var item in backup)
            {
                if (item.Value == null)
                {
                    if (File.Exists(item.Key)) File.Delete(item.Key);
                }
                else AtomicWrite(item.Key, item.Value);
            }
            throw;
        }
    }

    // Stage beside the destination, then replace it. This protects each individual
    // file from a partially written payload; multi-file rollback is handled by Save.
    private static void AtomicWrite(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
