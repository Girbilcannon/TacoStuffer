using System.Xml.Linq;
using TacoStuffer.Models;

internal static class Attribute107Checks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid standalone choice accepted"); }
    public static void Run()
    {
        var catalog = new AttributeCatalog(); catalog.Reload(null);
        AttributeDefinition Group(string key) => catalog.Groups.Single(g => g.Key == key);
        var icon = Group("iconFile"); var texture = Group("texture");
        Check(AttributePickerOptions.CanSplit(icon) && icon.Members.Any(m => m.Name == "iconSize"), "The combined compiled Icon group exposes its size member");
        var root = XElement.Parse("<MarkerCategory name='pack'/>");
        var categories = new Dictionary<string, XElement> { ["pack"] = root };
        var values = AttributePickerOptions.AddMember(root, new(icon, "iconSize"), categories);
        AttributeRules.Apply([root], values, (_, _) => true);
        Check(root.Attribute("iconSize")?.Value == "1.0" && root.Attributes().Count() == 2 && root.Attribute("iconFile") == null,
            "Adding only Icon Size to a parent category creates no image path or sibling attribute");
        root.SetAttributeValue("iconSize", "1.6");
        var child = XElement.Parse("<MarkerCategory name='child' iconFile='ICONS/beetle.png'/>"); categories["pack.child"] = child;
        var poi = XElement.Parse("<POI type='pack.child'/>");
        Check(AttributeRules.Effective(poi, "iconSize", "", categories) == "1.6" && AttributeRules.Effective(poi, "iconFile", "", categories) == "ICONS/beetle.png",
            "A child-specific icon and parent-only size combine through existing POI inheritance");
        AttributeRules.Apply([poi], AttributePickerOptions.AddMember(poi, new(icon, "iconSize"), categories), (_, _) => true);
        Check(poi.Attribute("iconSize")?.Value == "1.6" && poi.Attribute("iconFile") == null && AttributeRules.Inherited(poi, categories).Single(a => a.Name == "iconSize").Overridden,
            "A standalone override keeps the inherited value and marks that field overridden without copying the image");
        poi.SetAttributeValue("iconSize", "2.2");
        Check(AttributePickerOptions.AddMember(poi, new(icon, "iconSize"), categories).Count == 0, "Selecting an existing member preserves the edited local value");
        var other = XElement.Parse("<MarkerCategory name='other' iconSize='0.7'/>"); categories["pack.other"] = other;
        var a = XElement.Parse("<POI type='pack.child'/>"); var b = XElement.Parse("<POI type='pack.other'/>");
        foreach(var target in new[]{a,b}) AttributeRules.Apply([target], AttributePickerOptions.AddMember(target,new(icon,"iconSize"),categories),(_,_)=>true);
        Check(a.Attribute("iconSize")?.Value == "1.6" && b.Attribute("iconSize")?.Value == "0.7", "Multi-selection preserves each target's effective inherited value independently");

        foreach (var group in catalog.Groups.Where(AttributePickerOptions.CanSplit))
        foreach (var member in group.Members)
        {
            string kind = group.AppliesTo.Split(',')[0].Trim(); var target = new XElement(kind);
            var addition = AttributePickerOptions.AddMember(target, new(group, member.Name), categories);
            Check(addition.Count == 1 && addition.ContainsKey(member.Name), "An explicit individual choice adds exactly one attribute, including optional members: " + member.Name);
            AttributeRules.Apply([target], addition, (_, _) => true);
            Check(XElement.Parse(target.ToString()).Attributes().Single().Name.LocalName == member.Name, "Only the requested member survives XML round trip: " + member.Name);
        }
        var trail = XElement.Parse("<Trail texture='TEXTURES/route.png'/>");
        AttributeRules.Apply([trail], AttributePickerOptions.AddMember(trail,new(texture,"trailScale"),categories),(_,_)=>true);
        Check(trail.Attribute("texture")?.Value == "TEXTURES/route.png" && trail.Attribute("trailScale") != null, "Selecting trail scale retains an existing texture");
        AttributeRules.Apply([trail],new Dictionary<string,string?>{["trailScale"]=null},(_,_)=>true);
        Check(trail.Attributes().Single().Name == "texture", "Removing one independent field leaves its sibling untouched");
        var ranged = XElement.Parse("<POI triggerRange='8'/>");
        var infoRange = AttributePickerOptions.AddMember(ranged, new(Group("info"), "infoRange"), categories);
        Check(infoRange.Count == 1 && infoRange["infoRange"] == "8", "Info Range alone retains the existing trigger-range fallback without adding Info Text");
        var trigger = Group("trigger"); var auto = XElement.Parse("<POI/>");
        AttributeRules.Apply([auto],AttributePickerOptions.AddMember(auto,new(trigger,"autoTrigger"),categories),(_,_)=>true);
        Check(auto.Attributes().Single().Name == "autoTrigger" && !AttributePaneRules.HasLocalGroup(auto,Group("copy")), "Auto Trigger does not add range, copy or other dependencies");
        foreach (string key in new[]{"checkpoint","behavior","bounce","sticky","resetguid"})
        {
            var group=Group(key); Check(!AttributePickerOptions.CanSplit(group),"Dependent group remains bundled: "+key);
            Reject(()=>AttributePickerOptions.AddMember(new XElement("POI"),new(group,group.Members[0].Name),categories));
        }
        var custom = new AttributeDefinition("custom", "Custom", "POI", [new("one","1","number",false), new("two","2","number",true)], true);
        Check(!AttributePickerOptions.CanSplit(custom), "Unknown custom semantics are not inferred from optional flags");
        Reject(()=>AttributePickerOptions.AddMember(new XElement("Trail"),new(icon,"iconSize"),categories));
        Reject(()=>AttributePickerOptions.AddMember(new XElement("POI"),new(icon,"not-a-member"),categories));
        var full = AttributePickerOptions.AddBundle(new XElement("POI"),icon,categories);
        Check(full.Count == 2 && full.ContainsKey("iconFile") && full.ContainsKey("iconSize"), "Parent Icon selection adds the complete image-and-size bundle");
        Console.WriteLine("1.0.7 individual attribute, parent sizing/child icon, inheritance, multi-target, removal and exact XML checks passed.");
    }
}
