using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using TacoStuffer.Models;

namespace TacoStuffer;

public partial class MainWindow
{
    private void RememberDiskDocuments()
    {
        diskDocuments.Clear();
        if (projectFolder == null) return;
        foreach (string path in pages.Select(p => p.Name).Concat(codePages.Select(p => p.Name)))
            if (File.Exists(AssetLibrary.Resolve(projectFolder,path))) diskDocuments.Add(path);
    }

    // Only documents previously known to exist on disk are reconciled. New, unsaved
    // editor buffers are deliberately retained; browsing disk never imports documents.
    private void ReconcileDiskAssets()
    {
        if (projectFolder == null) return;
        var missing = diskDocuments.Where(p => !File.Exists(AssetLibrary.Resolve(projectFolder,p))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages.Where(p => missing.Contains(p.Name)).ToList())
        {
            CloseTab(page.Tab); pages.Remove(page); documentUse.Remove(page.Tab);
        }
        foreach (var page in codePages.Where(p => missing.Contains(p.Name)).ToList())
        {
            page.View.ClosePreview(); CloseTab(page.Tab); codePages.Remove(page); documentUse.Remove(page.Tab);
        }
        diskDocuments.ExceptWith(missing);
        importedAssets.RemoveAll(p => !File.Exists(AssetLibrary.Resolve(projectFolder,p)) && !codePages.Any(c => c.Name.Equals(p,StringComparison.OrdinalIgnoreCase)));
        foreach (string p in textureLabels.Keys.Where(p => !File.Exists(AssetLibrary.Resolve(projectFolder,p))).ToList()) textureLabels.Remove(p);
        assetPickerCache.Clear(); rawTrailPreview.Clear();
        if (missing.Count == 0) return;
        selected = null; PruneEditableTrails();
        // File deletion is handled by the Recycle Bin, not XML undo. Prevent history
        // from restoring deleted documents and causing the next save to recreate them.
        EditorState Prune(EditorState state)
        {
            var documents = state.Documents.Where(d => !missing.Contains(d.Name)).ToList();
            var guids = documents.SelectMany(d => System.Xml.Linq.XDocument.Parse(d.Xml).Descendants("Trail")).Select(e => (string?)e.Attribute("GUID")).ToHashSet();
            return state with { Documents = documents, Trails = state.Trails.Where(t => guids.Contains(EditableTrails.Deserialize(t).Guid)).ToList() };
        }
        history.Transform(Prune);
        if (lastHistoryState != null) lastHistoryState = Prune(lastHistoryState);
        RefreshExplorer();
        AttributesHost.Children.Clear(); SelectionTitle.Text = "Select an element"; CategoryPath.Text = "";
        StatusText.Text = "Removed missing documents from the workspace. Restore files from the Recycle Bin and import them to reopen.";
    }

    private void ImportManagedAsset(string path)
    {
        if (projectFolder == null) return;
        string full = AssetLibrary.Resolve(projectFolder,path);
        if (Path.GetExtension(path).Equals(".xml",StringComparison.OrdinalIgnoreCase))
        {
            if (pages.Any(p => p.Name.Equals(path,StringComparison.OrdinalIgnoreCase))) return;
            var xml = ParseXml(File.ReadAllText(full));
            if (xml.Root?.Name != "OverlayData") throw new InvalidOperationException("Pack XML must have an OverlayData root.");
            var editor = new RichTextBox { FontFamily = new FontFamily("Consolas"), FontSize = 13, AcceptsTab = true };
            Register(path,editor,new TabItem { Content = editor },FormatXml(xml),loadEditor:false);
            collapsedElements.Add(path + "|" + path);
        }
        else if (ProjectDocuments.Kind(path) != null && !codePages.Any(p => p.Name.Equals(path,StringComparison.OrdinalIgnoreCase)))
            RegisterCode(new ProjectCode(path,File.ReadAllText(full),false));
        if (!importedAssets.Contains(path,StringComparer.OrdinalIgnoreCase)) importedAssets.Add(path);
        if (ProjectDocuments.Kind(path) != null || Path.GetExtension(path).Equals(".xml",StringComparison.OrdinalIgnoreCase)) diskDocuments.Add(path);
        RefreshExplorer(); QueueHistoryCheckpoint("Import project documents/assets");
    }

    private void SetAssetDefault(string role, string path)
    {
        path = ProjectAssetPaths.Folder(path);
        assetPaths = role switch {
            "Core XML" => assetPaths with { CoreXml = path }, "Docs" => assetPaths with { Docs = path },
            "Scripts" => assetPaths with { Scripts = path }, "Icons (markers)" => assetPaths with { MarkerIcons = path },
            "Textures (trails)" => assetPaths with { TrailTextures = path }, "Trail Data" => assetPaths with { TrailFiles = path },
            _ => throw new InvalidOperationException("Unknown folder type.") };
        assetPickerCache.Clear();
    }

    private void DeleteManagedAssets(IReadOnlyList<string> paths)
    {
        if (projectFolder == null || workspaceBusy || !TrySyncXml()) throw new InvalidOperationException("Finish editing before deleting assets.");
        foreach (string path in paths) _ = AssetLibrary.Resolve(projectFolder,path);
        FlushHistoryCheckpoint();
        try
        {
            foreach (string path in paths)
            {
                string full = AssetLibrary.Resolve(projectFolder,path);
                if (File.Exists(full)) Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
        }
        finally { ReconcileDiskAssets(); RefreshExplorer(); }
    }
}
