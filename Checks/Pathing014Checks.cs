using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using TacoStuffer.Models;

internal static class Pathing014Checks
{
    private static void Check(bool value,string reason) {if(!value)throw new Exception(reason);}
    public static void Run()
    {
        string guid=Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var state=new PathingPreviewState();var utc=new DateTimeOffset(2026,10,23,20,0,0,TimeSpan.Zero);
        state.Trigger(guid,1,0,"A",15,1,utc);
        Check(state.Hidden(guid,1,false,"A",15,1,utc)&&!state.Hidden(guid,1,false,"A",16,1,utc),"Mode 1 resets on map change");
        state.Trigger(guid,6,0,"A",15,1,utc);
        Check(state.Hidden(guid,6,false,"A",15,1,utc)&&!state.Hidden(guid,6,false,"A",15,2,utc)&&state.Hidden(guid,6,false,"A",15,1,utc),"Mode 6 retains map/shard-specific state");
        state.Trigger(guid,4,1.25,"A",15,1,utc);
        Check(state.Hidden(guid,4,false,"A",15,1,utc.AddSeconds(1))&&!state.Hidden(guid,4,false,"A",15,1,utc.AddSeconds(1.25)),"Fractional timer boundary");
        state.Trigger(guid,2,0,"A",15,1,utc);
        Check(state.Hidden(guid,2,false,"A",15,1,utc.AddHours(3))&&!state.Hidden(guid,2,false,"A",15,1,utc.AddHours(4)),"Daily reset is UTC midnight");
        state.Trigger(guid,7,0,"Alice",15,1,utc);
        Check(state.Hidden(guid,7,false,"Alice",15,1,utc)&&!state.Hidden(guid,7,false,"Bob",15,1,utc),"Daily-per-character isolation");
        Check(PathingBehavior.NextWeekly(new(2026,10,26,7,29,59,TimeSpan.Zero))==new DateTimeOffset(2026,10,26,7,30,0,TimeSpan.Zero)
            &&PathingBehavior.NextWeekly(new(2026,10,26,7,30,0,TimeSpan.Zero))==new DateTimeOffset(2026,11,2,7,30,0,TimeSpan.Zero),"Weekly reset boundary");
        state.Trigger(guid,3,0,"A",15,1,utc);
        Check(state.Hidden(guid,3,false,"A",15,1,utc.AddYears(20))&&!state.Hidden(guid,3,true,"A",15,1,utc)
            &&!state.Hidden(guid,5,false,"A",15,1,utc)&&state.Hidden(guid,5,true,"A",15,1,utc)&&!state.Hidden(guid,0,true,"A",15,1,utc),"Permanent/inverted/unimplemented behavior separation");
        Check(PathingBehavior.GuidKey(guid)==PathingBehavior.GuidKey(new Guid(Convert.FromBase64String(guid)).ToString()),"GUID canonicalization");
        Check(PathingBehavior.MountVisible(null,0)&&!PathingBehavior.MountVisible("",0)&&PathingBehavior.MountVisible("raptor, jackal",5)
            &&PathingBehavior.MountVisible("none",0)&&!PathingBehavior.MountVisible("raptor",10),"Mount omission/empty/CSV/unmounted semantics");
        var bounce=new BouncePreview();
        Check(bounce.Sample("m",true,"bounce",2,1,.5,0)==0&&bounce.Sample("m",true,"bounce",2,1,.5,.5)==0
            &&Math.Abs(bounce.Sample("m",true,"bounce",2,1,.5,1.5)-2)<.0001&&Math.Abs(bounce.Sample("m",true,"bounce",2,1,.5,2.5))<.0001,"Bounce delay and reflected legs");
        Check(bounce.Sample("r",true,"rise",3,1,0,0)==0&&bounce.Sample("r",true,"rise",3,1,0,5)==3
            &&bounce.Sample("r",false,"rise",3,1,0,5)==3&&Math.Abs(bounce.Sample("r",false,"rise",3,1,0,6))<.0001,"Rise and unfocus return");
        var duplicates=XDocument.Parse("<OverlayData><POIs><POI GUID='same'/><POI GUID='same' invertBehavior='1'/></POIs></OverlayData>");
        Check(duplicates.Descendants("POI").Select(ElementIdentity.Key).Distinct().Count()==2,"Shared-GUID inverse markers keep independent editor keys");
        var direction=new Vector3(3,1,4);var wall=GpuTrailGeometry.WallSide(direction);
        Check(Math.Abs(Vector3.Dot(wall,direction))<.00001&&Math.Abs(wall.Length()-1)<.00001&&Marshal.OffsetOf<TrailGpuVertex>(nameof(TrailGpuVertex.WallOffset)).ToInt32()==36,"Wall geometry and GPU layout");
        string folder=Path.Combine(Path.GetTempPath(),"Behavior014_"+Guid.NewGuid().ToString("N"));string file=Path.Combine(folder,"state.json");
        try
        {
            state.Categories["pack.hidden"]=false;state.Save(file);var loaded=PathingPreviewState.Load(file);
            Check(loaded.Hidden(guid,3,false,"A",15,1,utc)&&!loaded.Categories["pack.hidden"]&&!loaded.Hidden(guid,6,false,"A",15,1,utc),"Persist timers/preferences, not session state");
        }
        finally {Directory.Delete(folder,true);}
        // Building a pack must preserve behavior pairs, coordinates and hooks;
        // source libraries are files, never a replacement for hook expressions.
        string pack = Path.Combine(Path.GetTempPath(), "Attributes014_" + Guid.NewGuid().ToString("N") + ".taco");
        try
        {
            var authored = XDocument.Parse($"<OverlayData><MarkerCategory name='pack' DisplayName='Readable title' defaultToggle='0' isHidden='1' tip-description='Category notes'/><POIs><POI GUID='{guid}' type='pack' MapID='24' xpos='1' ypos='2' zpos='3' behavior='4' resetLength='1.25' show='pack.next' hide='pack.old' toggle='pack.other' mount='raptor, jackal' bounce='rise' bounce-height='2' bounce-duration='1' bounce-delay='0.5' canFade='0' tip-name='Checkpoint' tip-description='Details' script-trigger='OnCheckpoint()'/><POI GUID='{guid}' type='pack' MapID='24' xpos='1' ypos='2' zpos='3' behavior='4' invertBehavior='1'/></POIs></OverlayData>");
            TestPackBuilder.Build(pack, new[] { ("pack.xml", authored.ToString()) }, null,
                codeDocuments: new[] { new ProjectCode("pack.lua", "Pack:Require(\"scripts/checkpoints.lua\")", true), new ProjectCode("scripts/checkpoints.lua", "function OnCheckpoint(marker, automatic) end", true) });
            using var archive = System.IO.Compression.ZipFile.OpenRead(pack);
            using var content = archive.GetEntry("pack.xml")!.Open();
            var exported = XDocument.Load(content);
            Check(XNode.DeepEquals(authored, exported), "Compile preserves attributes, GUID pairs, category namespaces and XYZ");
            Check(archive.GetEntry("pack.lua") != null && archive.GetEntry("scripts/checkpoints.lua") != null
                && !archive.Entries.Any(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)), "Lua libraries are packaged; preview state is excluded");
        }
        finally {File.Delete(pack);}
        var catalog=new AttributeCatalog();catalog.Reload(null);
        var flat=catalog.Groups.SelectMany(g=>g.Members).ToDictionary(m=>m.Name,StringComparer.OrdinalIgnoreCase);
        string definitions=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../TacoStuffer/Attributes"));
        foreach(string definition in Directory.EnumerateFiles(definitions,"*.xml"))
        foreach(var a in XDocument.Load(definition).Root!.Elements("Attribute"))
        {
            string name=(string)a.Attribute("name")!;
            Check(flat.TryGetValue(name,out var m)&&m.DefaultValue==(string?)a.Attribute("default")&&m.Type==(string?)a.Attribute("type"),"Compiled/XML parity: "+name);
        }
        Check(catalog.Groups.Single(g=>g.Key=="defaultToggle").Applies(new XElement("MarkerCategory"))&&!catalog.Groups.Single(g=>g.Key=="defaultToggle").Applies(new XElement("POI"))
            &&catalog.Groups.Single(g=>g.Key=="isWall").Applies(new XElement("Trail"))&&!catalog.Groups.Single(g=>g.Key=="isWall").Applies(new XElement("POI")),"Attribute applicability");
    }
}
