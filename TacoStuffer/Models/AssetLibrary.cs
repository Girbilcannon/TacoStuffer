using System.IO;

namespace TacoStuffer.Models;

internal static class AssetLibrary
{
    public static string RewriteReferences(string source, IReadOnlyDictionary<string, string> moves)
    {
        var edits = new List<(int Start, int Length, string Text)>();
        var lines = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++) if (source[i] == '\n') lines.Add(i + 1);
        using var text = new StringReader(source);
        using var reader = System.Xml.XmlReader.Create(text, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        try
        {
            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element || !reader.HasAttributes) continue;
                while (reader.MoveToNextAttribute())
                {
                    if (!reader.LocalName.Equals("iconFile", StringComparison.OrdinalIgnoreCase) && !reader.LocalName.Equals("texture", StringComparison.OrdinalIgnoreCase) && !reader.LocalName.Equals("trailData", StringComparison.OrdinalIgnoreCase) && !reader.LocalName.Equals("script",StringComparison.OrdinalIgnoreCase)) continue;
                    if (!moves.TryGetValue(reader.Value.Replace('\\', '/'), out string? path)) continue;
                    var info = (System.Xml.IXmlLineInfo)reader;
                    int offset = lines[info.LineNumber - 1] + info.LinePosition - 1;
                    while (offset < source.Length && source[offset] != '=' ) offset++;
                    offset++;
                    while (offset < source.Length && char.IsWhiteSpace(source[offset])) offset++;
                    if (offset >= source.Length || source[offset] is not ('\'' or '"')) continue;
                    char quote = source[offset++]; int end = source.IndexOf(quote, offset);
                    if (end < 0) continue;
                    string escaped = path.Replace("&", "&amp;").Replace("<", "&lt;").Replace(quote.ToString(), quote == '"' ? "&quot;" : "&apos;");
                    edits.Add((offset, end - offset, escaped));
                }
                reader.MoveToElement();
            }
        }
        catch (System.Xml.XmlException) { /* Earlier undo entries can retain incomplete typing. */ }
        foreach (var edit in edits.OrderByDescending(e => e.Start)) source = source.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        return source;
    }
    public static bool IsTrail(string path) => Path.GetExtension(path).Equals(".trl", StringComparison.OrdinalIgnoreCase);
    public static bool IsDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".xml" or ".lua" or ".md" or ".markdown" or ".txt";
    public static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".dds" or ".tga";
    public static string Resolve(string root, string relative)
    {
        root = Path.GetFullPath(root);
        string normalized = relative == "." ? "." : TacoImporter.PackPath(relative);
        string path = Path.GetFullPath(Path.Combine(root, normalized));
        if (path != root && !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Assets must stay inside the project.");
        // Reject links at every ancestor, not just at the selected file.
        for (string? part = path; part != null; part = Path.GetDirectoryName(part))
        {
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Linked project folders and assets are not supported.");
            if (part.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }
    public static IReadOnlyDictionary<string, string> Move(string root, IEnumerable<string> paths, string folder)
    {
        folder = ProjectAssetPaths.Folder(folder);
        string destination = Resolve(root, folder);
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException(destination);
        var moves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string source in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string from = Resolve(root, source);
            if ((!IsImage(from) && !IsTrail(from)) || !File.Exists(from)) throw new InvalidOperationException("Select existing image or TRL assets to move.");
            string target = ProjectAssetPaths.FilePath(folder, Path.GetFileName(from));
            if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
            string to = Resolve(root, target);
            if (!targets.Add(to) || File.Exists(to) || Directory.Exists(to))
                throw new IOException("Move cancelled: an asset already exists at " + target + ". No files were moved.");
            moves.Add(source, target);
        }
        var completed = new List<KeyValuePair<string, string>>();
        try
        {
            foreach (var move in moves)
            {
                File.Move(Resolve(root, move.Key), Resolve(root, move.Value));
                completed.Add(move);
            }
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var move in completed.AsEnumerable().Reverse())
                try { File.Move(Resolve(root, move.Value), Resolve(root, move.Key)); }
                catch (Exception ex) { rollbackErrors.Add(ex); }
            if (rollbackErrors.Count > 0) throw new AggregateException("Some assets could not be restored after a failed move. Check the source and destination folders.", new[] { failure }.Concat(rollbackErrors));
            throw;
        }
        return moves;
    }
}
