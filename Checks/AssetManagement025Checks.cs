using System.Xml.Linq;
using TacoStuffer.Models;

internal static class AssetManagement025Checks
{
    private static void Check(bool value,string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(),"TacoStuffer025-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new ProjectAssetPaths("art/icons","art/textures","routes","notes","lua",".");
            var snapshot = new ProjectSnapshot("Assets025","",[new("pack.xml","<OverlayData><MarkerCategory name='pack'/><POIs/></OverlayData>")],new(),
                AssetPaths:paths, AssetFolders:["references","art"],AlternateOutputs:[Path.Combine(root,"reader1"),Path.Combine(root,"reader2")],
                OutlinerOrder:["pack.xml|category:pack","pack.xml|POI:guid"]);
            string file = ProjectStore.Create(snapshot,root);
            string project = Path.GetDirectoryName(file)!;
            var loaded = ProjectStore.Load(file);
            Check(loaded.AssetPaths == paths && loaded.AssetFolders!.SequenceEqual(snapshot.AssetFolders!) && loaded.AlternateOutputs!.SequenceEqual(snapshot.AlternateOutputs!) && loaded.OutlinerOrder!.SequenceEqual(snapshot.OutlinerOrder!),"All six defaults, registered folders, outputs and Outliner ordering survive save/reopen");
            Check(Directory.Exists(Path.Combine(project,"notes")) && Directory.Exists(Path.Combine(project,"lua")),"New projects create document defaults");
            Directory.CreateDirectory(Path.Combine(project,"references/sub"));
            File.WriteAllText(Path.Combine(project,"references/sub/test.txt"),"keep me");
            var moved = AssetFolders.Rename(project,"references","resources");
            Check(moved.Files["references/sub/test.txt"] == "resources/sub/test.txt" && File.ReadAllText(Path.Combine(project,"resources/sub/test.txt")) == "keep me","Folder rename returns a complete reference map and preserves file bytes");
            bool collision = false;
            try { AssetFolders.Rename(project,"resources","art"); } catch (IOException) { collision = true; }
            Check(collision && Directory.Exists(Path.Combine(project,"resources")),"Folder rename collision does not change the source");
            // Explicit adoption is required before existing documents can be saved.
            File.WriteAllText(Path.Combine(project,"extra.xml"),"<OverlayData><MarkerCategory name='pack'/><POIs/></OverlayData>");
            File.WriteAllText(Path.Combine(project,"lua/external.lua"),"return 25");
            Check(ProjectStore.Load(file).Documents.Count == 1 && ProjectStore.Load(file).CodeDocuments!.Count == 0,"Files placed on disk do not silently become project documents");
            var adopted = loaded with { Documents = [..loaded.Documents,new("extra.xml",File.ReadAllText(Path.Combine(project,"extra.xml")))],
                CodeDocuments = [new("lua/external.lua","return 25")],ImportedAssets = ["extra.xml","lua/external.lua"] };
            ProjectStore.Save(file,adopted);
            var reopened = ProjectStore.Load(file);
            Check(reopened.Documents.Count == 2 && reopened.CodeDocuments!.Single().Name == "lua/external.lua","Imported XML and Lua register without overwriting untracked files accidentally");
            string pack = Path.Combine(root,"compiled.taco");
            TestPackBuilder.Build(pack,reopened.Documents.Select(d => (d.Name,d.Content)).ToList(),project,importedAssets:reopened.ImportedAssets,assetPaths:paths,codeDocuments:reopened.CodeDocuments);
            File.Delete(Path.Combine(project,"extra.xml")); File.Delete(Path.Combine(project,"lua/external.lua"));
            var afterDeletion = ProjectStore.Load(file);
            Check(afterDeletion.Documents.Count == 1 && afterDeletion.CodeDocuments!.Count == 0 && !File.Exists(Path.Combine(project,"extra.xml")),"Opening a project tolerates externally deleted documents without recreating them");

            string first = Path.Combine(root,"target1"), second = Path.Combine(root,"target2"), blocked = Path.Combine(root,"blocked");
            File.WriteAllText(blocked,"not a folder");
            var delivery = PackDelivery.Copy(pack,"Pack.taco",[blocked,first,first + Path.DirectorySeparatorChar,second]);
            Check(delivery.Written.Count == 2 && delivery.Errors.Count == 1,"Failed destinations do not prevent other copies and normalized duplicates are skipped");
            Check(delivery.Written.All(p => File.ReadAllBytes(p).SequenceEqual(File.ReadAllBytes(pack))),"All destinations receive identical compiled bytes");
            var again = PackDelivery.Copy(pack,"Pack.taco",[first,second]);
            Check(again.Written.Count == 2 && again.Errors.Count == 0,"Existing compiled packs can be replaced");

            var docs = new[] {
                XDocument.Parse("<OverlayData><MarkerCategory name='pack'><MarkerCategory name='one'/><MarkerCategory name='two'/></MarkerCategory><POIs><POI GUID='a' type='pack.one'/><Trail GUID='b' type='pack.one'/><POI GUID='c' type='pack.two'/></POIs></OverlayData>"),
                XDocument.Parse("<OverlayData><MarkerCategory name='pack'><MarkerCategory name='other'/></MarkerCategory><POIs><POI GUID='d' type='pack.other'/></POIs></OverlayData>") };
            PackSelection Item(int document,string guid) => new(document,docs[document].Descendants().ToList().FindIndex(e => (string?)e.Attribute("GUID") == guid));
            PackSelection Category(int document,string name) => new(document,docs[document].Descendants().ToList().FindIndex(e => e.Name == "MarkerCategory" && (string?)e.Attribute("name") == name));
            var into = PackEditing.DropAssets(docs,[Item(0,"a"),Item(0,"b")],Category(0,"two"),CategoryDropPosition.Child);
            Check(into[0].Descendants().Where(e => (string?)e.Attribute("GUID") is "a" or "b").All(e => (string?)e.Attribute("type") == "pack.two"),"Batch drops onto a category update both markers and trails");
            var across = PackEditing.DropAssets(docs,[Item(0,"a"),Item(0,"b")],Item(1,"d"),CategoryDropPosition.After);
            Check(across[1].Root!.Element("POIs")!.Elements().Select(e => (string?)e.Attribute("GUID")).SequenceEqual(new[]{"d","a","b"}) && across[1].Descendants().Where(e => e.Name.LocalName is "POI" or "Trail").All(e => (string?)e.Attribute("type") == "pack.other"),"Cross-XML drops retain hierarchy order and destination category");
            var before = PackEditing.DropAssets(docs,[Item(0,"b"),Item(0,"a")],Item(0,"c"),CategoryDropPosition.Before);
            Check(before[0].Root!.Element("POIs")!.Elements().Select(e => (string?)e.Attribute("GUID")).SequenceEqual(new[]{"b","a","c"}),"Before-drop preserves caller hierarchy order for multiple assets");
            var sibling = PackEditing.DropAssets(docs,[Item(0,"a")],Category(0,"two"),CategoryDropPosition.Before);
            Check((string?)sibling[0].Descendants("POI").First(e => (string?)e.Attribute("GUID") == "a").Attribute("type") == "pack","Dropping beside a subcategory makes the asset a sibling in its parent category");
            var withoutGuid = new[] { XDocument.Parse("<OverlayData><MarkerCategory name='p'/><POIs><POI name='first' type='p'/><Trail name='second' type='p'/><POI name='third' type='p'/></POIs></OverlayData>") };
            var nodes = withoutGuid[0].Descendants().ToList();
            var noGuidMove = PackEditing.DropAssets(withoutGuid,[new(0,nodes.FindIndex(e => (string?)e.Attribute("name") == "first"))],new(0,nodes.FindIndex(e => (string?)e.Attribute("name") == "third")),CategoryDropPosition.After,out var identities);
            Check(noGuidMove[0].Descendants("POI").Last().Attribute("name")!.Value == "first" && identities.Single(i => i.OldKey == "POI:index:0").NewKey == "POI:index:2","Ordering metadata follows moved assets even without GUIDs");
            bool noChildren = false;
            try { PackEditing.DropAssets(docs,[Item(0,"a")],Item(0,"b"),CategoryDropPosition.Child); } catch (InvalidOperationException) { noChildren = true; }
            Check(noChildren && (string?)docs[0].Descendants("POI").First().Attribute("type") == "pack.one","Markers/trails cannot become parents; planning never mutates original XML");
            Console.WriteLine("0.1.14b asset management, folder rename, document adoption, multi-output delivery and Outliner drop checks passed.");
        }
        finally { Directory.Delete(root,true); }
    }
}
