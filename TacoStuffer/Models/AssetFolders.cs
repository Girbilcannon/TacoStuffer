using System.IO;

namespace TacoStuffer.Models;

internal sealed record AssetFolderMove(string Source, string Destination, IReadOnlyDictionary<string, string> Files);

internal static class AssetFolders
{
    // A default XML root must not expose every project folder. An explicitly
    // registered project root does request that view, including its children.
    public static IReadOnlyList<string> BrowseRoots(ProjectAssetPaths defaults, IEnumerable<string> registered) =>
        defaults.All.Where(p => p != ".").Concat(registered.Select(ProjectAssetPaths.Folder))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool Contains(string folder, string path) => folder == "." || path.Replace('\\', '/').Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.Replace('\\', '/').StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    public static string Parent(string folder)
    {
        int slash = folder.LastIndexOf('/'); return slash < 0 ? "." : folder[..slash];
    }
    public static string Relocate(string path, string source, string destination) => Contains(source, path)
        ? destination + path.Replace('\\', '/')[source.Length..] : path;
    public static string Existing(string root, string folder)
    {
        folder = ProjectAssetPaths.Folder(folder);
        while (!Directory.Exists(AssetLibrary.Resolve(root, folder)))
        {
            if (folder == ".") throw new DirectoryNotFoundException("The project folder is no longer available.");
            folder = Parent(folder);
        }
        return folder;
    }
    public static ProjectAssetPaths ExistingDefaults(string root, ProjectAssetPaths defaults) => defaults; // Keep configured defaults when folders disappear; browsing never recreates them.
    public static IReadOnlyList<string> Files(string root, string folder)
    {
        folder = ProjectAssetPaths.Folder(folder);
        if (folder == ".") throw new InvalidOperationException("The project root cannot be deleted or moved.");
        var result = new List<string>();
        void Visit(string relative)
        {
            foreach (string full in Directory.EnumerateFileSystemEntries(AssetLibrary.Resolve(root, relative)))
            {
                string path = Path.GetRelativePath(root, full).Replace('\\', '/');
                AssetLibrary.Resolve(root, path); // Every descendant is checked for links before any mutation.
                if (Directory.Exists(full)) Visit(path); else result.Add(path);
            }
        }
        Visit(folder); return result;
    }
    public static string Destination(string root, string source, string parent)
    {
        source = ProjectAssetPaths.Folder(source); parent = ProjectAssetPaths.Folder(parent);
        if (source == "." || Contains(source, parent)) throw new InvalidOperationException("A folder cannot move into itself or its descendants.");
        string from = AssetLibrary.Resolve(root, source), targetParent = AssetLibrary.Resolve(root, parent);
        if (!Directory.Exists(from) || !Directory.Exists(targetParent)) throw new DirectoryNotFoundException("Refresh Assets; a source or destination folder no longer exists.");
        string target = ProjectAssetPaths.FilePath(parent, Path.GetFileName(source));
        ProjectAssetPaths.Folder(target);
        string full = AssetLibrary.Resolve(root, target);
        if (!target.Equals(source, StringComparison.OrdinalIgnoreCase) && (File.Exists(full) || Directory.Exists(full)))
            throw new IOException("That destination already contains a folder or file with this name. Folders are never merged or overwritten.");
        return target;
    }
    public static AssetFolderMove Move(string root, string source, string parent)
    {
        string destination = Destination(root, source, parent);
        var files = Files(root, source).ToDictionary(path => path, path => Relocate(path, source, destination), StringComparer.OrdinalIgnoreCase);
        if (!source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            Directory.Move(AssetLibrary.Resolve(root, source), AssetLibrary.Resolve(root, destination));
        return new(source, destination, files);
    }
    public static AssetFolderMove Rename(string root,string source,string name)
    {
        source = ProjectAssetPaths.Folder(source);
        if (source == "." || !ProjectStore.ValidFileName(name) || name is "." or "..") throw new InvalidOperationException("Enter a valid single folder name.");
        string destination = ProjectAssetPaths.FilePath(Parent(source),name);
        if (source == destination) return new(source,destination,new Dictionary<string,string>());
        string target = AssetLibrary.Resolve(root,destination);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("That folder or file already exists. No files were changed.");
        var paths = Files(root,source).ToDictionary(p => p,p => Relocate(p,source,destination),StringComparer.OrdinalIgnoreCase);
        Directory.Move(AssetLibrary.Resolve(root,source),target);
        return new(source,destination,paths);
    }
    public static void Delete(string root, string folder, Action<string> recycle)
    {
        _ = Files(root, folder); // Validate the complete subtree before invoking the platform deletion service.
        recycle(AssetLibrary.Resolve(root, folder));
    }
}
