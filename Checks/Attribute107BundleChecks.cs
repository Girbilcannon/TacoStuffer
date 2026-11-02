using System.Xml.Linq;
using TacoStuffer.Models;

internal static class Attribute107BundleChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        var catalog = new AttributeCatalog(); catalog.Reload(null);
        var categories = new Dictionary<string,XElement> {
            ["pack"] = XElement.Parse("<MarkerCategory name='pack' iconFile='ICONS/parent.png' iconSize='1.7' trailScale='2.5' triggerRange='8'/>")
        };
        var icon = catalog.Groups.Single(g=>g.Key=="iconFile");
        var marker = XElement.Parse("<POI type='pack' iconFile='ICONS/edited.png'/>");
        Check(!AttributePickerOptions.HasCompleteBundle(marker,icon),"A child-only icon retains individual presentation");
        var complete = AttributePickerOptions.AddBundle(marker,icon,categories);
        Check(complete.Count==1 && complete["iconSize"]=="1.7", "Parent Icon completes a partial bundle using inherited size without overwriting the edited image");
        AttributeRules.Apply([marker],complete,(_,_)=>true);
        Check(marker.Attribute("iconFile")!.Value=="ICONS/edited.png" && marker.Attribute("iconSize")!.Value=="1.7" && AttributePickerOptions.HasCompleteBundle(marker,icon),
            "Both fields exist and the pane selects its original full Icon layout");
        Check(AttributePickerOptions.HasCompleteBundle(XElement.Parse(marker.ToString()),icon),"Full-bundle presentation survives XML save/reload without authoring-only metadata");
        Check(AttributePickerOptions.AddBundle(marker,icon,categories).Count==0,"Re-adding a parent bundle preserves all edited local values");
        foreach(var group in catalog.Groups.Where(AttributePickerOptions.CanSplit))
        {
            var target=new XElement(group.AppliesTo.Split(',')[0].Trim());
            var additions=AttributePickerOptions.AddBundle(target,group,categories);
            var expected=group.Members.Where(m=>!(group.Key=="toggle" && m.Name=="toggleCategory")).Select(m=>m.Name).ToHashSet();
            Check(additions.Keys.ToHashSet().SetEquals(expected),"Parent selection populates every independent control, including optional fields: "+group.Key);
            AttributeRules.Apply([target],additions,(_,_)=>true);
            Check(AttributePickerOptions.HasCompleteBundle(target,group),"Parent bundle selects full pane layout: "+group.Key);
            foreach(var member in group.Members)
            {
                var childTarget=new XElement(target.Name);
                var child=AttributePickerOptions.AddMember(childTarget,new(group,member.Name),categories);
                Check(child.Count==1 && child.ContainsKey(member.Name),"Child selection remains exactly one attribute: "+member.Name);
            }
        }
        var texture=catalog.Groups.Single(g=>g.Key=="texture");
        var trail=XElement.Parse("<Trail type='pack'/>");
        var textures=AttributePickerOptions.AddBundle(trail,texture,categories);
        Check(textures.Count==2 && textures.ContainsKey("texture") && textures["trailScale"]=="2.5","Parent Texture restores texture plus inherited trail scale");
        var copy=catalog.Groups.Single(g=>g.Key=="copy");
        var copyFields=AttributePickerOptions.AddBundle(new XElement("POI"),copy,categories);
        Check(copyFields.Count==2 && copyFields.ContainsKey("copy-message") && !copyFields.ContainsKey("triggerRange"),"Copy includes its confirmation option without silently adding Trigger");
        var trigger=catalog.Groups.Single(g=>g.Key=="trigger");
        var triggerFields=AttributePickerOptions.AddBundle(new XElement("POI"),trigger,categories);
        Check(triggerFields.Count==2 && triggerFields["autoTrigger"]=="false" && !triggerFields.ContainsKey("copy"),"Trigger restores range and automatic toggle without unrelated Copy fields");
        var toggle=catalog.Groups.Single(g=>g.Key=="toggle");
        Check(!AttributePickerOptions.AddBundle(new XElement("POI"),toggle,categories).ContainsKey("toggleCategory"),"Parent Toggle does not introduce a blank higher-priority alias that masks its target");
        foreach(var group in catalog.Groups.Where(g=>!AttributePickerOptions.CanSplit(g)))
        {
            var target=new XElement(group.AppliesTo.Split(',')[0].Trim());
            Check(AttributePickerOptions.AddBundle(target,group,categories).OrderBy(p=>p.Key).SequenceEqual(AttributePaneRules.AddGroup(target,group,categories).OrderBy(p=>p.Key)),
                "Dependent/custom groups retain their original opt-in behavior: "+group.Key);
        }
        Console.WriteLine("1.0.7 parent bundle restoration, pane mode, exact child fields, inheritance and dependency checks passed.");
    }
}
