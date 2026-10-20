using System.IO;

namespace TacoStuffer.Models;

public enum ProjectDocumentKind { Lua, Markdown, PlainText }

public static class ProjectDocuments
{
    public static ProjectDocumentKind? Kind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".lua" => ProjectDocumentKind.Lua,
        ".md" or ".markdown" => ProjectDocumentKind.Markdown,
        ".txt" => ProjectDocumentKind.PlainText,
        _ => null
    };

    public static bool IsDocumentation(string path) => Kind(path) is ProjectDocumentKind.Markdown or ProjectDocumentKind.PlainText;

    public static string NewPath(string name, ProjectDocumentKind kind)
    {
        string extension = kind switch { ProjectDocumentKind.Lua => ".lua", ProjectDocumentKind.Markdown => ".md", _ => ".txt" };
        name = name.Trim();
        if (name.Length == 0 || name.Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Enter a document name.");
        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name += extension;
        if (!ProjectStore.ValidFileName(name)) throw new InvalidOperationException("Use a filename without folders or reserved Windows characters.");
        return (kind == ProjectDocumentKind.Lua ? "scripts/" : "DOCS/") + name;
    }

    public static string RenamePath(string current, string filename)
    {
        if (Kind(current) == null || !ProjectStore.ValidPackPath(current)) throw new InvalidOperationException("Invalid document path.");
        filename = filename.Trim();
        string extension = Path.GetExtension(current);
        if (!filename.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) filename += extension;
        if (!ProjectStore.ValidFileName(filename) || filename.Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Enter a valid filename without folders. The document extension is retained.");
        string folder = Path.GetDirectoryName(current)?.Replace('\\', '/') ?? "";
        return folder.Length == 0 ? filename : folder + "/" + filename;
    }

    public static string InitialContent(ProjectDocumentKind kind) => kind switch
    {
        ProjectDocumentKind.Lua => "-- Pack script\n-- Lua source is edited and packaged here; Taco Stuffer does not execute scripts.\n\n",
        ProjectDocumentKind.Markdown => "# Pack documentation\n\nDescribe your pack, installation, and usage here.\n",
        _ => ""
    };
}
