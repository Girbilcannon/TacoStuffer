using System.IO;
using System.Xml.Linq;
using TacoStuffer.Models;

namespace TacoStuffer;

public partial class MainWindow
{
    private void ValidateAssetFolder(string folder, bool deleting)
    {
        if (projectFolder == null || !TrySyncXml()) throw new InvalidOperationException("Resolve XML errors before changing asset folders.");
        _ = AssetFolders.Files(projectFolder, folder);
        if (AssetFolders.Contains(folder,Path.GetRelativePath(projectFolder,projectFile!).Replace('\\','/')))
            throw new InvalidOperationException("The project manifest folder cannot be moved or deleted.");
        foreach (string output in alternateOutputs.Prepend(tacoOutputFolder).Where(p => !string.IsNullOrWhiteSpace(p)))
            if (AssetFolders.Contains(folder,Path.GetRelativePath(projectFolder,output).Replace('\\','/')))
                throw new InvalidOperationException("This folder contains a .taco output location. Change it in Project Settings first.");
        FlushHistoryCheckpoint();
    }
    private ProjectAssetPaths MoveAssetFolder(string source, string parent)
    {
        ValidateAssetFolder(source,false);
        return FinishAssetFolderMove(AssetFolders.Move(projectFolder!,source,parent));
    }
    private ProjectAssetPaths RenameAssetFolder(string source,string name)
    {
        ValidateAssetFolder(source,false);
        return FinishAssetFolderMove(AssetFolders.Rename(projectFolder!,source,name));
    }
    private ProjectAssetPaths ExternalAssetFolderRename(string source,string destination)
    {
        if (projectFolder == null || !TrySyncXml()) throw new InvalidOperationException("Resolve XML errors, then reactivate Assets to finish synchronizing the renamed folder.");
        var files = AssetFolders.Files(projectFolder,destination).ToDictionary(p => AssetFolders.Relocate(p,destination,source),p => p,StringComparer.OrdinalIgnoreCase);
        FlushHistoryCheckpoint();
        return FinishAssetFolderMove(new(source,destination,files));
    }
    private ProjectAssetPaths FinishAssetFolderMove(AssetFolderMove result)
    {
        string source = result.Source, destination = result.Destination;
        var paths = new Dictionary<string,string>(result.Files,StringComparer.OrdinalIgnoreCase);
        var known = importedAssets.Concat(textureLabels.Keys).Concat(pages.SelectMany(p => p.Xml.Descendants().Attributes())
            .Where(a => a.Name.LocalName.Equals("iconFile",StringComparison.OrdinalIgnoreCase) || a.Name.LocalName.Equals("texture",StringComparison.OrdinalIgnoreCase) || a.Name.LocalName.Equals("trailData",StringComparison.OrdinalIgnoreCase) || a.Name.LocalName.Equals("script",StringComparison.OrdinalIgnoreCase)).Select(a => a.Value.Replace('\\','/')));
        foreach (string path in known.Where(p => AssetFolders.Contains(source,p))) paths[path] = AssetFolders.Relocate(path,source,destination);
        ApplyImageReferences(paths);
        string Rebase(string p) => AssetFolders.Relocate(p,source,destination);
        foreach (var page in pages.Where(p => AssetFolders.Contains(source,p.Name)))
        {
            page.Name = Rebase(page.Name); SetTabHeader(page.Tab,page.Name);
            if (detachedTabs.TryGetValue(page.Tab,out var host)) host.RenameDocument(page.Name);
            if (activeHostedCodeTab == page.Tab) hostedCodeWindow?.Rename(page.Name);
            if (!importedAssets.Contains(page.Name,StringComparer.OrdinalIgnoreCase)) importedAssets.Add(page.Name);
        }
        for (int i=0;i<codePages.Count;i++)
        {
            var page = codePages[i]; if (!AssetFolders.Contains(source,page.Name)) continue;
            string name = Rebase(page.Name); codePages[i] = page with { Name = name };
            page.View.RenameDocument(name); SetTabHeader(page.Tab,name);
            if (detachedTabs.TryGetValue(page.Tab,out var host)) host.RenameDocument(name);
            if (activeHostedCodeTab == page.Tab) hostedCodeWindow?.Rename(name);
            if (!importedAssets.Contains(name,StringComparer.OrdinalIgnoreCase)) importedAssets.Add(name);
        }
        string RebaseId(string id)
        {
            int split = id.IndexOf('|'); if (split < 0) return id;
            string oldDocument = id[..split], document = Rebase(oldDocument), key = id[(split+1)..];
            return document + "|" + (key == oldDocument ? document : key);
        }
        outlinerOrder = outlinerOrder.Select(RebaseId).ToList();
        var collapsed = collapsedElements.Select(RebaseId).ToList(); collapsedElements.Clear(); foreach (string id in collapsed) collapsedElements.Add(id);
        EditorState RebaseHistory(EditorState state) => state with { Documents = state.Documents.Select(d => d with { Name = Rebase(d.Name) }).ToList(), OutlinerOrder = state.OutlinerOrder?.Select(RebaseId).ToList() };
        history.Transform(RebaseHistory); if (lastHistoryState != null) lastHistoryState = RebaseHistory(lastHistoryState);
        assetPaths = new(Rebase(assetPaths.MarkerIcons),Rebase(assetPaths.TrailTextures),Rebase(assetPaths.TrailFiles),Rebase(assetPaths.Docs),Rebase(assetPaths.Scripts),Rebase(assetPaths.CoreXml));
        assetFolders = assetFolders.Select(Rebase).ToList();
        RememberDiskDocuments(); WritePages(); return assetPaths;
    }
    private ProjectAssetPaths DeleteAssetFolder(string folder)
    {
        ValidateAssetFolder(folder,true);
        try
        {
            AssetFolders.Delete(projectFolder!,folder,path => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));
        }
        finally { ReconcileDiskAssets(); RefreshExplorer(); }
        assetFolders.RemoveAll(p => AssetFolders.Contains(folder,p));
        return assetPaths;
    }
}
