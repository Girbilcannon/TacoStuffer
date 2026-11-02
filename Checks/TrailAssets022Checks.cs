using System.IO.Compression;
using System.Numerics;
using System.Xml.Linq;
using TacoStuffer.Models;

internal static class TrailAssets022Checks
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(),"trail-assets022-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"DATA"));
        Directory.CreateDirectory(Path.Combine(root,"Other"));
        try
        {
            var original = new EditableTrail { Number=1,Guid="original",MapId=24 };
            var a=original.AddSegment(); a.Add(new(1,2,3)); a.Points.Add(new TrailPoint { Id=a.NextPoint++,Position=new(1,2,3) }); a.Add(new(4,5,6));
            original.AddSegment().Add(new(10,11,12));
            var bytes = EditableTrails.Compile(original);
            File.WriteAllBytes(Path.Combine(root,"DATA","Foreign Trail.TRL"),bytes);
            File.WriteAllBytes(Path.Combine(root,"Other","second.trl"),bytes);
            var doc = XDocument.Parse("<OverlayData><MarkerCategory name='pack' DisplayName='My Pack'><MarkerCategory name='route' DisplayName='Display Route'/></MarkerCategory><POIs><POI name='Foreign_Trail'/></POIs></OverlayData>");
            var plan=TrailAssetInsertion.Plan(root,new[]{"DATA/Foreign Trail.TRL","Other/second.trl"},[doc],"pack.route","DATA",7,[],true);
            Check(plan.Count==2 && plan[0].Editable?.Number==7 && plan[1].Editable?.Number==8,"Editable insertion assigns unique monotonic identities");
            Check(plan[0].Editable!.MapId==24 && plan[0].Editable!.Segments.Select(s=>s.Points.Count).SequenceEqual(new[]{3,1}),"Map ID, segments and duplicate points are retained from the binary");
            Check(plan[0].Element.Attribute("name")!.Value=="Foreign_Trail_01" && plan[0].Element.Attribute("DisplayName")!.Value=="Foreign Trail","File-derived names avoid spaces and project collisions while keeping readable display names");
            Check(plan.All(p=>(string?)p.Element.Attribute("type")=="pack.route" && (string?)p.Element.Attribute("MapID")=="24") && plan.Select(p=>(string?)p.Element.Attribute("GUID")).Distinct().Count()==2,"XML references exact category namespace, binary map ID and unique GUIDs");
            Check(!doc.Descendants("Trail").Any() && !plan[0].Copy,"Planning does not mutate XML or copy an unreferenced asset");
            foreach(var item in plan) doc.Root!.Element("POIs")!.Add(new XElement(item.Element));
            var repeated=TrailAssetInsertion.Plan(root,["DATA/Foreign Trail.TRL"],[doc],"pack.route","DATA",9,plan.Select(p=>p.Editable!).ToList(),true).Single();
            Check(repeated.Copy && repeated.Path!="DATA/Foreign Trail.TRL" && !File.Exists(Path.Combine(root,repeated.Path)),"Reinsertion plans an independent output filename without overwriting source data");
            var rawDoc = XDocument.Parse("<OverlayData><MarkerCategory name='pack'/><POIs/></OverlayData>");
            var raw=TrailAssetInsertion.Plan(root,["Other/second.trl"],[rawDoc],"pack","DATA",1,[],false).Single();
            Check(raw.Editable==null && raw.Bytes.SequenceEqual(bytes) && !raw.Copy,"Make editable off preserves the source binary without editable state");
            rawDoc.Root!.Element("POIs")!.Add(raw.Element);
            string output=Path.Combine(root,"raw.taco");
            TestPackBuilder.Build(output,[("pack.xml",rawDoc.ToString())],root,[],[raw.Path]);
            using(var archive=ZipFile.OpenRead(output))
            {
                using var stream=archive.GetEntry(raw.Path)!.Open(); using var copy=new MemoryStream(); stream.CopyTo(copy);
                Check(copy.ToArray().SequenceEqual(bytes),"Referenced TRL export preserves every original byte");
                using var reader=new StreamReader(archive.GetEntry("pack.xml")!.Open()); var exported=XDocument.Parse(reader.ReadToEnd());
                string texture=(string)exported.Descendants("Trail").Single().Attribute("texture")!;
                Check(archive.GetEntry(texture)!=null,"Reference-only trails receive a visible fallback texture at export");
            }
            string editableOutput=Path.Combine(root,"editable.taco");
            TestPackBuilder.Build(editableOutput,[("pack.xml",doc.ToString())],root,plan.Select(p=>p.Editable!).ToList(),plan.Select(p=>p.Path).ToList());
            using(var archive=ZipFile.OpenRead(editableOutput))
            using(var reader=new BinaryReader(archive.GetEntry(plan[0].Path)!.Open()))
                Check(reader.ReadInt32()==0 && reader.ReadUInt32()==24,"Editable insertion compiles for the source map rather than current game map");
            string rawProject = Path.Combine(root,"Raw.cilantro");
            ProjectStore.Save(rawProject,new ProjectSnapshot("Raw",Path.Combine(root,"EXPORT"),[new ProjectXml("raw.xml",rawDoc.ToString())],[],
                Trails:[],ImportedAssets:[raw.Path],AssetPaths:new()));
            var reloadedRaw=ProjectStore.Load(rawProject);
            Check(reloadedRaw.Trails!.Count==0 && XDocument.Parse(reloadedRaw.Documents.Single().Content).Descendants("Trail").Single().Attribute("trailData")!.Value==raw.Path,"Reference-only insertion reopens without silently becoming editable");
            string editableProject = Path.Combine(root,"Editable.cilantro");
            ProjectStore.Save(editableProject,new ProjectSnapshot("Editable",Path.Combine(root,"EXPORT"),[new ProjectXml("editable.xml",doc.ToString())],[],
                Trails:plan.Select(p=>p.Editable!).ToList(),NextTrailNumber:9,ImportedAssets:plan.Select(p=>p.Path).ToList(),AssetPaths:new()));
            var reopened=ProjectStore.Load(editableProject);
            Check(reopened.Trails!.Count==2 && reopened.Trails[0].Segments.Select(s=>s.Points.Count).SequenceEqual(new[]{3,1}),"Editable insertion retains original segment/point structure on save and reopen");
            var moved=AssetLibrary.Move(root,["Other/second.trl"],"DATA");
            Check(moved["Other/second.trl"]=="DATA/second.trl" && File.Exists(Path.Combine(root,"DATA","second.trl")),"TRL file moves are supported");
            string source="<OverlayData><!-- trailData='Other/second.trl' --><POIs><Trail trailData='Other/second.trl'/></POIs></OverlayData>";
            string rewritten=AssetLibrary.RewriteReferences(source,moved);
            Check(XDocument.Parse(rewritten).Descendants("Trail").Single().Attribute("trailData")!.Value=="DATA/second.trl" && rewritten.Contains("<!-- trailData='Other/second.trl' -->"),"Moved TRL references are rebased in XML/history without editing comments");
            void Reject(string category,string path)
            {
                bool rejected=false;
                try { TrailAssetInsertion.Plan(root,[path],[doc],category,"DATA",1,[],true); } catch(Exception ex) when(ex is InvalidOperationException or IOException) { rejected=true; }
                Check(rejected,"Invalid trail/category accepted: "+path);
            }
            Reject("pack.missing","DATA/Foreign Trail.TRL");
            Reject("pack.route","../outside.trl");
            File.WriteAllBytes(Path.Combine(root,"DATA","bad.trl"),[0,0,0]); Reject("pack.route","DATA/bad.trl");
            var bad=(byte[])bytes.Clone(); bad[4]=0;bad[5]=0;bad[6]=0;bad[7]=0;
            File.WriteAllBytes(Path.Combine(root,"DATA","zero-map.trl"),bad); Reject("pack.route","DATA/zero-map.trl");
            bad=(byte[])bytes.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(bad,8);
            File.WriteAllBytes(Path.Combine(root,"DATA","nan.trl"),bad); Reject("pack.route","DATA/nan.trl");
            string project=Path.Combine(root,"Test.cilantro");
            ProjectStore.Save(project,new ProjectSnapshot("Test",Path.Combine(root,"EXPORT"),[new ProjectXml("pack.xml",rawDoc.ToString())],[],AssetPaths:new("TEXTURES","TEXTURES","DATA")));
            Check(ProjectStore.Load(project).AssetPaths!.TrailFiles=="DATA","Default trail folder survives project persistence");
            Check(AssetLibrary.IsTrail("TRAIL.TRL") && AssetLibrary.IsDocument("notes.md") && !AssetLibrary.IsImage("TRAIL.TRL"),"Asset kind classification is extension-aware and case-insensitive");
        }
        finally { Directory.Delete(root,true); }
    }
}
