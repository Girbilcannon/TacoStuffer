using System.Numerics;
using System.Xml.Linq;
using TacoStuffer.Models;

internal static class Outliner020Checks
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Reject(Action action, string reason)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception(reason);
    }
    public static void Run()
    {
        var assets = new[] { new XElement("POI", new XAttribute("name", "a")), new XElement("Trail", new XAttribute("name", "b")), new XElement("POI", new XAttribute("name", "c")) };
        Check(BatchAssetNaming.Plan(assets, "qd_check", false, assets).SequenceEqual(new[] { "qd_check01", "qd_check02", "qd_check03" }), "Unnumbered names start with two-digit numbering");
        Check(BatchAssetNaming.Plan(assets, "qd_check01", false, assets).SequenceEqual(new[] { "qd_check01", "qd_check02", "qd_check03" }), "Existing trailing number is reused, not appended");
        Check(BatchAssetNaming.Plan(assets, "qd_check_001", false, assets).SequenceEqual(new[] { "qd_check_001", "qd_check_002", "qd_check_003" }), "Underscore and three-digit padding are preserved");
        Check(BatchAssetNaming.Plan(assets, "qd_check_099", false, assets).SequenceEqual(new[] { "qd_check_099", "qd_check_100", "qd_check_101" }), "Numbering crosses padding boundaries naturally");
        Check(BatchAssetNaming.Plan(assets, "Checkpoint", true, assets).SequenceEqual(new[] { "Checkpoint 01", "Checkpoint 02", "Checkpoint 03" }), "Unnumbered display name adds a space");
        Check(BatchAssetNaming.Plan(assets, "Checkpoint 01", true, assets).SequenceEqual(new[] { "Checkpoint 01", "Checkpoint 02", "Checkpoint 03" }), "Display name suffix retains its space and padding");
        var occupied = assets.Append(new XElement("POI", new XAttribute("name", "QD_CHECK02"))).ToArray();
        Check(BatchAssetNaming.Plan(assets, "qd_check", false, occupied).SequenceEqual(new[] { "qd_check01", "qd_check03", "qd_check04" }), "Internal name collisions are skipped across marker/trail types without changing hierarchy order");
        Check(BatchAssetNaming.Plan(assets, "qd_check", false, occupied).Count == 3 && (string?)assets[0].Attribute("name") == "a", "Planning never mutates XML");
        Check(BatchAssetNaming.Plan([assets[1], assets[0]], "x", false, assets).SequenceEqual(new[] { "x01", "x02" }), "Caller-provided hierarchy order is retained");
        Check(BatchAssetNaming.Plan([assets[0]], "plain", false, assets).Single() == "plain", "Single rename does not invent a suffix");
        Check(BatchAssetNaming.Plan(assets, "x999999999999999999999", false, assets)[1] == "x1000000000000000000000", "Suffix increment cannot overflow");
        Reject(() => BatchAssetNaming.Plan(assets, "two words", false, assets), "Spaces in internal names accepted");
        Reject(() => BatchAssetNaming.Plan(assets, "two\twords", false, assets), "Tabs in internal names accepted");
        Reject(() => BatchAssetNaming.Plan(assets, "two\nwords", true, assets), "Multiline display name accepted");
        Reject(() => BatchAssetNaming.Plan([assets[0]], "b", false, assets), "Single asset internal name collision accepted");
        Reject(() => BatchAssetNaming.Plan([assets[0], new XElement("MarkerCategory")], "x", false, assets), "Mixed categories accepted for recorded asset batch rename");
        Reject(() => BatchAssetNaming.Plan(assets, "", false, assets), "Empty base name accepted");

        var branches = new[] { new OutlinerBranch("root",0,true), new OutlinerBranch("cat",1,true), new OutlinerBranch("trail",2,true), new OutlinerBranch("seg",3,true,false), new OutlinerBranch("other",0,true) };
        var collapsed = new HashSet<string> { "root", "other", "seg" };
        OutlinerExpansion.Toggle(branches,"root",collapsed,false);
        Check(!collapsed.Contains("root") && collapsed.Contains("cat") && collapsed.Contains("other"), "Normal open reveals one level and leaves other branches alone");
        OutlinerExpansion.Toggle(branches,"root",collapsed,true);
        Check(branches.All(b => collapsed.Contains(b.Id)), "Shift close collapses every descendant");
        OutlinerExpansion.Toggle(branches,"root",collapsed,true);
        Check(branches.Take(3).All(b => !collapsed.Contains(b.Id)) && collapsed.Contains("seg") && collapsed.Contains("other"), "Shift open exposes segments without opening their point lists or sibling branches");
        OutlinerExpansion.Toggle(branches,"seg",collapsed,true);
        Check(collapsed.Contains("seg"), "Shift open on a segment does not reveal points");
        OutlinerExpansion.Toggle(branches,"seg",collapsed,false);
        Check(!collapsed.Contains("seg"), "Normal segment click can still reveal points");
        OutlinerExpansion.Toggle(branches,"root",collapsed,true);
        Check(branches.All(b => collapsed.Contains(b.Id)), "Shift close still collapses manually opened segments");
        collapsed.Remove("seg");
        OutlinerExpansion.Toggle(branches,"root",collapsed,true);
        Check(!collapsed.Contains("trail") && collapsed.Contains("seg"), "Shift reopen closes previously expanded point lists");
        OutlinerExpansion.Toggle(branches,"cat",collapsed,false);
        Check(collapsed.Contains("cat") && !collapsed.Contains("trail"), "Normal close only changes the clicked branch");
        OutlinerExpansion.Toggle(branches,"cat",collapsed,false);
        Check(!collapsed.Contains("cat") && collapsed.Contains("trail"), "Normal reopen returns to a single visible level");

        EditableTrail Trail()
        {
            var trail = new EditableTrail { Number=1, Guid="test", MapId=23 };
            var segment = trail.AddSegment(); segment.Smoothness=6; segment.SourceSmoothness=4;
            for (int i=1;i<=9;i++) segment.Add(new Vector3(i,2,3));
            return trail;
        }
        var split = Trail(); var original = split.Segments[0];
        original.ResampleSource = original.Points.Select(p => new TrailPoint { Id=p.Id, Position=p.Position }).ToList();
        EditableTrails.DeletePoints(split,original.Id,new HashSet<long> { 3,4,7 },true);
        Check(split.Segments.Count == 3 && split.Segments.Select(s => string.Join(",",s.Points.Select(p=>p.Id))).SequenceEqual(new[] { "1,2", "5,6", "8,9" }), "Disjoint deletions create separate ordered survivor runs");
        Check(split.Segments[0] == original && split.Segments.Select(s => s.Id).Distinct().Count()==3 && split.NextSegment > split.Segments.Max(s=>s.Id), "Segment identities stay unique and first survivor reuses original");
        Check(split.Segments.All(s=>s.Smoothness==6 && s.ResampleSource==null && s.NextPoint==10), "All split pieces preserve density and valid point counters while discarding stale resampling data");
        var roundTrip = EditableTrails.Deserialize(EditableTrails.Serialize(split));
        var imported = EditableTrails.Import(EditableTrails.Compile(roundTrip),1,"test");
        Check(imported.Segments.Select(s=>s.Points.Count).SequenceEqual(new[]{2,2,2}), "Saved editable data and compiled TRL retain each gap");
        var normal = Trail(); EditableTrails.DeletePoints(normal,1,new HashSet<long>{3,4,7},false);
        Check(normal.Segments.Count==1 && normal.Segments[0].Points.Select(p=>p.Id).SequenceEqual(new long[]{1,2,5,6,8,9}), "Standard Delete still reconnects the surviving points");
        var edges = Trail(); EditableTrails.DeletePoints(edges,1,new HashSet<long>{1,2,8,9},true);
        Check(edges.Segments.Count==1 && edges.Segments[0].Points.Count==5, "Deleting either end does not create empty segments");
        var all = Trail(); EditableTrails.DeletePoints(all,1,all.Segments[0].Points.Select(p=>p.Id).ToHashSet(),true);
        Check(all.Segments.Count==0, "Deleting all points in split mode removes the empty segment");
        EditableTrails.Validate(all);
        var single = Trail(); EditableTrails.DeletePoints(single,1,new HashSet<long>{2,4,6,8},true);
        Check(single.Segments.Count==5 && single.Segments.All(s=>s.Points.Count==1), "Isolated survivor points are preserved without fabricated connections");
        var untouched = single.AddSegment(); untouched.Add(new(20,2,3));
        EditableTrails.DeletePoints(single,single.Segments[1].Id,new HashSet<long>{3},true);
        Check(single.Segments.Last()==untouched, "Existing neighboring segments retain their position and identity");
        var ids = Trail(); ids.Segments[0].Points.Reverse();
        EditableTrails.DeletePoints(ids,1,new HashSet<long>{3,4,7},true);
        Check(ids.Segments.Select(s=>string.Join(",",s.Points.Select(p=>p.Id))).SequenceEqual(new[]{"9,8","6,5","2,1"}), "Splitting follows geometric order rather than numeric point ID");
    }
}
