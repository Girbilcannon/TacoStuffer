using System.IO;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record ProjectAssetPaths(string MarkerIcons = "TEXTURES", string TrailTextures = "TEXTURES", string TrailFiles = "DATA")
{
    public static string Folder(string value)
    {
        string path = value.Trim().Replace('\\', '/').TrimEnd('/');
        if (path == ".") return path;
        path = TacoImporter.PackPath(path);
        if (Storage(path)) throw new InvalidOperationException("Choose an asset folder outside project storage folders.");
        return path;
    }

    public ProjectAssetPaths Validated() => new(Folder(MarkerIcons), Folder(TrailTextures), Folder(TrailFiles));

    public static string FilePath(string folder, string name) => folder == "." ? name : folder + "/" + name;

    private static bool Storage(string path) => path.Split('/')[0].ToUpperInvariant() is "EXPORT" or "CUSTOM_ATTRIBUTES" or "EDITABLE_TRAILS";

    public static IEnumerable<string> Files(string root, string folder)
    {
        root = Path.GetFullPath(root);
        string start = Path.Combine(root, Folder(folder));
        if (!Directory.Exists(start)) yield break;
        IEnumerable<string> Visit(string directory)
        {
            if ((System.IO.File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked asset folders are not supported.");
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Storage(relative)) continue;
                var attributes = System.IO.File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked assets are not supported: " + relative);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (string child in Visit(path)) yield return child;
                }
                else if (!relative.EndsWith(".cilantro", StringComparison.OrdinalIgnoreCase) && !relative.EndsWith(".taco", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(relative).StartsWith("TacoStuffer_Import_Report", StringComparison.OrdinalIgnoreCase)) yield return path;
            }
        }
        foreach (string path in Visit(start)) yield return path;
    }

    public static ProjectAssetPaths Infer(IEnumerable<XDocument> documents)
    {
        var attributes = documents.SelectMany(d => d.Descendants().Attributes()).ToList();
        string Common(string key, string fallback)
        {
            var directories = new List<string[]>();
            foreach (var attr in attributes.Where(a => a.Name.LocalName.Equals(key, StringComparison.OrdinalIgnoreCase) && a.Value.Length > 0))
            {
                try
                {
                    var bits = TacoImporter.PackPath(attr.Value).Split('/');
                    directories.Add(bits[..^1]);
                }
                catch (InvalidOperationException)
                {
                }
            }
            if (directories.Count == 0) return fallback;
            int count = directories.Min(d => d.Length);
            for (int i = 0; i < count; i++) if (directories.Any(d => !d[i].Equals(directories[0][i], StringComparison.OrdinalIgnoreCase)))
                {
                    count = i;
                    break;
                }
            string folder = count == 0 ? "." : string.Join('/', directories[0].Take(count));
            try
            {
                return Folder(folder);
            }
            catch (InvalidOperationException)
            {
                return fallback;
            }
        }
        return new(Common("iconFile", "TEXTURES"), Common("texture", "TEXTURES"), Common("trailData", "DATA"));
    }
}
