using System.Xml.Linq;
using TacoStuffer.Models;

internal static class AttributeRequirement017Checks
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run()
    {
        var catalog = new AttributeCatalog(); catalog.Reload(null);
        var categories = new Dictionary<string, XElement>();
        var context = new AttributeRequirementContext(true, true);
        IReadOnlyList<AttributeAlert> Alerts(XElement e, string group) => AttributeRequirements.Evaluate(e, catalog.Groups.Single(g => g.Key == group), categories, context);
        bool Requires(XElement e, string group, string target) => Alerts(e, group).Any(a => a.Message == "This attribute requires: " + target);
        var copy = XElement.Parse("<POI type='pack.child' copy='Text'/>");
        Check(Requires(copy, "copy", "Trigger"), "Copy without explicit or inherited range shows Trigger dependency");
        var original = copy.ToString(); _ = Alerts(copy, "copy");
        Check(copy.ToString() == original, "Evaluating requirements must never modify XML");
        categories["pack"] = XElement.Parse("<MarkerCategory name='pack' triggerRange='2'/>");
        Check(!Requires(copy, "copy", "Trigger"), "Inherited Trigger clears Copy requirement");
        copy.SetAttributeValue("triggerRange", "");
        Check(Requires(copy, "copy", "Trigger"), "Explicit empty local range blocks inherited range");
        copy.SetAttributeValue("triggerRange", "4");
        Check(Alerts(copy, "copy").Count == 0, "Adding valid Trigger clears Copy alert");
        copy.SetAttributeValue("triggerRange", "NaN");
        Check(Requires(copy, "copy", "Trigger"), "Invalid ranges do not satisfy requirements");
        Check(!Requires(XElement.Parse("<MarkerCategory copy='Text'/>"), "copy", "Trigger"), "Category menu copying has no proximity requirement");

        var trigger = XElement.Parse("<POI triggerRange='2'/>");
        Check(Alerts(trigger, "trigger").Any(a => a.Message.StartsWith("No effect")), "Orphan Trigger warns");
        trigger.SetAttributeValue("show", "pack");
        Check(Alerts(trigger, "trigger").Count == 0, "Show uses Trigger without needing Copy");
        trigger.Attribute("show")!.Remove();
        trigger.SetAttributeValue("type", "pack.child");
        categories["pack"].SetAttributeValue("copy", "Inherited text");
        Check(Alerts(trigger, "trigger").Count == 0, "Inherited actions prevent false orphan alerts");
        trigger.Attribute("type")!.Remove();
        trigger.SetAttributeValue("bounce", "rise");
        Check(Alerts(trigger, "trigger").Count == 0, "Bounce uses proximity range");
        trigger.SetAttributeValue("autoTrigger", "1");
        Check(Alerts(trigger, "trigger").Any(a => a.Message.StartsWith("Auto Trigger has no effect")), "Bounce does not consume automatic activation");
        trigger.Attribute("bounce")!.Remove(); trigger.Attribute("autoTrigger")!.Remove();
        trigger.SetAttributeValue("info", "Instructions");
        Check(Alerts(trigger, "trigger").Count == 0, "Info uses fallback Trigger Range");
        trigger.SetAttributeValue("infoRange", "5");
        Check(Alerts(trigger, "trigger").Any(a => a.Message.StartsWith("No effect")), "Explicit Info Range means Trigger Range is unused by Info");

        var behavior = XElement.Parse("<POI behavior='4' triggerRange='2' GUID='c59f209c-78df-402e-956b-dc7afdcf2698'/>");
        Check(Requires(behavior, "behavior", "Reset Length greater than 0"), "Timed behavior warns for absent duration");
        behavior.SetAttributeValue("resetLength", "30");
        Check(Alerts(behavior, "behavior").Count == 0, "Valid duration clears timed behavior alert");
        behavior.SetAttributeValue("GUID", "bad");
        Check(Requires(behavior, "behavior", "a valid GUID"), "Persistent behavior needs GUID identity");
        Check(Requires(XElement.Parse("<POI toggle='pack' toggleCategory='' triggerRange='2'/>"), "toggle", "a target category"), "Toggle alias precedence is respected");
        Check(Requires(XElement.Parse("<POI mount=''/>"), "mount", "at least one mount")
            && Alerts(XElement.Parse("<POI mount='none'/>"), "mount").Count == 0, "Empty Mount list differs from unmounted selection");
        context = new(false, false, true, false);
        Check(Requires(XElement.Parse("<POI info='Text'/>"), "info", "Info popups enabled"), "Disabled Info preference is a condition requirement");
        Check(Requires(XElement.Parse("<POI copy='Text' triggerRange='2'/>"), "copy", "clipboard permission"), "Clipboard-disabled condition is reported");
        Check(Requires(XElement.Parse("<POI show='pack' triggerRange='2'/>"), "showHide", "Test marker interactions"), "Active preview reports interaction-test condition");
        context = new(true, true, true, true);
        Check(Alerts(XElement.Parse("<POI show='pack' triggerRange='2'/>"), "showHide").Count == 0, "Meeting preview condition clears alert");
        context = new(true, true, false, false, false);
        Check(Requires(XElement.Parse("<POI copy='Text' triggerRange='2' autoTrigger='1'/>"), "copy", "automatic clipboard permission"), "Manual-only clipboard mode reports automatic-copy requirement");
        var values = AttributePaneRules.AddGroup(XElement.Parse("<POI/>"), catalog.Groups.Single(g => g.Key == "copy"), categories);
        Check(!values.ContainsKey("triggerRange") && !values.ContainsKey("autoTrigger"), "Adding Copy must not silently install the dependency");
    }
}
