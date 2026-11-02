using System.Numerics;
using System.IO.Compression;
using System.Xml.Linq;
using TacoStuffer.Models;

internal static class Checkpoint019Checks
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run()
    {
        string A = Guid.NewGuid().ToString("D"), B = Guid.NewGuid().ToString("D"), C = Guid.NewGuid().ToString("D"), D = Guid.NewGuid().ToString("D");
        CheckpointTestNode Node(string id, Vector3 pos, string[] next, bool finish = false, bool checkpoint = true, (Vector3, Vector3)[]? edges = null)
            => new(id, id, 23, pos, 1, next, 2, 0x00FF0080, 0x80808040, finish, checkpoint, edges ?? []);
        var nodes = new[] { Node(A, Vector3.Zero, [B, C]), Node(B, new(10, 0, 0), [D]), Node(C, new(10, 0, 10), [D]),
            Node(D, new(20, 0, -5), [], true, false, [(new(20,0,-5),new(20,0,5))]) };
        var session = new CheckpointTestSession(); session.Start(nodes, A);
        Check(session.Visible().Count == 4 && session.Visible()[A] == 0 && session.Visible()[D] == 2, "Armed node plus both fork paths/rejoin appear within depth horizon");
        Check(session.Update(new(-5,0,0),23,_=>true) == null && session.Update(Vector3.Zero,23,_=>true) == A, "Fresh entry consumes first checkpoint");
        Check(session.Armed.SetEquals([B,C]) && !session.Visible().ContainsKey(A), "Completed ring disappears and both fork alternatives arm");
        Check(session.Update(new(10,0,0),23,_=>true) == B && !session.Armed.Contains(C) && session.Armed.Contains(D), "Entering one fork retires its alternative and retains shared finish");
        Check(session.Update(new(25,0,0),23,_=>true) == D && session.Finished && session.Armed.Count == 0, "Swept contact crosses two-point finish trail between telemetry samples");
        session.Reset(A);
        Check(!session.Finished && session.Completed.Count == 0 && session.Armed.SetEquals([A]), "Reset restores original frontier without persistent state");
        Check(session.Update(Vector3.Zero,23,_=>true) == null && session.Update(Vector3.Zero,23,_=>true) == null, "Starting/resetting while inside does not auto-consume checkpoint");
        session.Update(new(-4,0,0),23,_=>true);
        Check(session.Update(Vector3.Zero,23,_=>true) == A, "Leaving and re-entering activates after baseline");
        session.Pause();
        Check(session.Update(new(10,0,0),23,_=>true) == null, "Resume establishes a fresh baseline");
        var overlap = new[] { Node(A,Vector3.Zero,[B]), Node(B,Vector3.Zero,[],true) };
        session.Start(overlap,A); session.Update(new(-4,0,0),23,_=>true); session.Update(Vector3.Zero,23,_=>true);
        Check(session.Update(Vector3.Zero,23,_=>true) == null, "Arming an overlapping successor does not finish in place");
        session.Update(new(4,0,0),23,_=>true); Check(session.Update(Vector3.Zero,23,_=>true) == B, "Overlapping finish requires a fresh entry");
        session.Start([Node(A,Vector3.Zero,[])], A); session.Update(new(-4,0,0),23,_=>true); session.Update(Vector3.Zero,23,_=>true);
        Check(session.Finished, "Existing terminal checkpoints remain valid implicit finishes");
        Check(CheckpointTestSession.SegmentDistanceSquared(new(-1,0,0),new(1,0,0),new(0,0,-1),new(0,0,1)) < .0001f
            && CheckpointTestSession.SegmentDistanceSquared(new(0,5,0),new(0,5,0),new(-1,0,0),new(1,0,0)) == 25, "Finish geometry uses three-dimensional segment contact");
        Check(AttributeColor.Parse("FF000080",true,out uint rgba) && rgba == 0x80FF0000 && AttributeColor.Format(rgba,true) == "FF000080", "Checkpoint colors preserve alpha-last roundtrip");
        Check(AttributeColor.Parse("80FF0000",false,out uint argb) && argb == rgba && AttributeColor.Format(argb,false) == "80FF0000", "Ordinary alpha-first color is preserved");
        Check(AttributeColor.Parse("#F00",false,out uint shortColor) && shortColor == 0xFFFF0000, "Existing ordinary RGB shorthand remains supported by the picker");
        var catalog = new AttributeCatalog(); catalog.Reload(null);
        var finishDefinition = catalog.Groups.Single(g=>g.Key=="cpFinish");
        Check(finishDefinition.Applies(new XElement("POI")) && finishDefinition.Applies(new XElement("Trail")), "Separate finish attribute applies to markers and trails");
        var context = new AttributeRequirementContext(true,true,true,false,true,true);
        Check(AttributeRequirements.Evaluate(new XElement("POI",new XAttribute("GUID",A),new XAttribute("isCheckpoint","true")),catalog.Groups.Single(g=>g.Key=="checkpoint"),new Dictionary<string,XElement>(),context).Count == 0,
            "Active checkpoint test satisfies the preview-support requirement");
        Check(AttributeRequirements.Evaluate(XElement.Parse("<POI cpFinish='false'/>"),finishDefinition,new Dictionary<string,XElement>(),context).Count == 0,"Disabled finish flag has no active GUID requirement");
        var reset = catalog.Groups.Single(g=>g.Key=="resetguid");
        Check(!AttributePaneRules.HasLocalGroup(XElement.Parse($"<POI cpReset='{A}' resetMessage='Restarted!'/ >".Replace("/ >","/>")),reset), "Shared resetMessage does not create a phantom Reset GUID section");
        string output = Path.Combine(Path.GetTempPath(),"Checkpoint019_"+Guid.NewGuid().ToString("N")+".taco");
        try
        {
            var xml=XDocument.Parse($"<OverlayData><POIs><POI GUID='{A}' cpReset='{B}' resetMessage='Race restarted!'/><Trail GUID='{C}' MapID='23' cpFinish='true'/></POIs></OverlayData>");
            TestPackBuilder.Build(output,[("test.xml",xml.ToString())],null);
            using var zip=ZipFile.OpenRead(output);using var stream=zip.GetEntry("test.xml")!.Open();
            Check(XNode.DeepEquals(xml,XDocument.Load(stream)),"Finish and reset message attributes export without changes");
        }
        finally {File.Delete(output);}
    }
}
