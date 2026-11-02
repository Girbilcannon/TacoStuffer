using System.Xml.Linq;
using TacoStuffer.Models;

internal static class Rename021Checks
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run()
    {
        var targets = new[] {
            new XElement("POI", new XAttribute("name","old1"),new XAttribute("DisplayName","Original A")),
            new XElement("Trail", new XAttribute("name","old2"),new XAttribute("DisplayName","Original B")) };
        var all = targets.Append(new XElement("POI",new XAttribute("name","race_002"))).ToArray();
        var both = BatchAssetNaming.PlanChanges(targets,new Dictionary<string,string> { ["name"]="race_001",["DisplayName"]="Checkpoint 01" },all);
        Check(both["name"].SequenceEqual(new[]{"race_001","race_003"}) && both["DisplayName"].SequenceEqual(new[]{"Checkpoint 01","Checkpoint 02"}),"Dialog plans both fields with padding, order and internal-name collision handling");
        var nameOnly = BatchAssetNaming.PlanChanges(targets,new Dictionary<string,string> { ["name"]="race" },all);
        Check(nameOnly.Count==1 && !nameOnly.ContainsKey("DisplayName"),"Editing only internal name leaves display names out of the operation");
        var displayOnly = BatchAssetNaming.PlanChanges(targets,new Dictionary<string,string> { ["DisplayName"]="Race Gate 009" },all);
        Check(displayOnly.Count==1 && displayOnly["DisplayName"].SequenceEqual(new[]{"Race Gate 009","Race Gate 010"}),"Editing only display name leaves internal names alone and preserves padding");
        Check(BatchAssetNaming.PlanChanges(targets,new Dictionary<string,string>(),all).Count==0,"Unedited batch dialog is a no-op");
        var before = targets.Select(x=>x.ToString()).ToArray();
        bool rejected=false;
        try { BatchAssetNaming.PlanChanges(targets,new Dictionary<string,string>{["DisplayName"]="Valid Label",["name"]="invalid name"},all); }
        catch(InvalidOperationException) { rejected=true; }
        Check(rejected && targets.Select(x=>x.ToString()).SequenceEqual(before),"Invalid internal name rejects the operation without partially changing display names");
        var single = BatchAssetNaming.PlanChanges([targets[0]],new Dictionary<string,string>{["name"]="unique",["DisplayName"]="Single Label"},all);
        Check(single["name"].Single()=="unique" && single["DisplayName"].Single()=="Single Label","Single-item dialog retains unnumbered names");
    }
}
