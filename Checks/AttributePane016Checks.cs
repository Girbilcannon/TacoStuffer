using System.Xml.Linq;
using TacoStuffer.Models;

internal static class AttributePane016Checks
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run()
    {
        var categories = new Dictionary<string, XElement>
        {
            ["pack"] = XElement.Parse("<MarkerCategory name='pack' DisplayName='Readable' alpha='0.5' color='FFFFFFFF' defaultToggle='0' isHidden='1'/>"),
            ["pack.child"] = XElement.Parse("<MarkerCategory name='child' alpha='0.7' info='Hello&#xA;World' infoRange='6' triggerRange='8' autoTrigger='true'/>"),
            ["pack.other"] = XElement.Parse("<MarkerCategory name='other' alpha='0.9' triggerRange='12'/>")
        };
        var marker = XElement.Parse("<POI type='pack.child' alpha='0.7' COLOR='' />");
        var inherited = AttributeRules.Inherited(marker, categories);
        Check(inherited.Single(a => a.Name == "alpha") is { Value: "0.7", Category: "pack.child", Overridden: true }, "Nearest parent is retained in inherited list even for equal local override");
        Check(inherited.Single(a => a.Name == "color").Overridden && AttributeRules.Effective(marker, "color", "fallback", categories) == "", "Explicit empty and case-insensitive overrides are retained");
        Check(!inherited.Any(a => a.Name is "DisplayName" or "name" or "defaultToggle" or "isHidden"), "Category identity/menu flags are not inherited marker attributes");
        Check(AttributeRules.Inherited(categories["pack.child"], categories).Count == 0, "Category declarations retain existing non-inheritance semantics");
        AttributeRules.Apply([marker], new Dictionary<string, string?> { ["alpha"] = null }, (_, _) => true);
        Check(!AttributeRules.Inherited(marker, categories).Single(a => a.Name == "alpha").Overridden
            && AttributeRules.Effective(marker, "alpha", "1", categories) == "0.7", "Removing local value restores inheritance");

        var catalog = new AttributeCatalog(); catalog.Reload(null);
        var copy = catalog.Groups.Single(g => g.Key == "copy");
        var trigger = catalog.Groups.Single(g => g.Key == "trigger");
        var show = catalog.Groups.Single(g => g.Key == "showHide");
        var action = XElement.Parse("<POI type='pack.child' show='pack.other' autoTrigger='true'/>");
        Check(AttributePaneRules.HasLocalGroup(action, show) && AttributePaneRules.HasLocalGroup(action, trigger)
            && !AttributePaneRules.HasLocalGroup(action, copy), "Show/Hide plus Auto Trigger must never activate Copy controls");
        Check(!AttributePaneRules.HasLocalGroup(marker, catalog.Groups.Single(g => g.Key == "info")), "Inherited-only Info is absent from local controls");
        Check(copy.Members.All(m => m.Name != "triggerRange" && m.Name != "autoTrigger")
            && trigger.Members.Select(m => m.Name).SequenceEqual(new[] { "triggerRange", "autoTrigger" }), "Trigger attributes have their own definition instead of Copy ownership");

        var empty = XElement.Parse("<POI/>");
        var showValues = AttributePaneRules.AddGroup(empty, show, categories);
        Check(showValues.ContainsKey("show") && !showValues.ContainsKey("triggerRange") && !showValues.ContainsKey("autoTrigger")
            && !showValues.ContainsKey("copy") && !showValues.ContainsKey("hide"), "New Show/Hide leaves dependencies to the author without creating Copy or omitted options");
        var inheritedTriggerValues = AttributePaneRules.AddGroup(XElement.Parse("<POI type='pack.child'/>"), show, categories);
        Check(!inheritedTriggerValues.ContainsKey("triggerRange") && !inheritedTriggerValues.ContainsKey("autoTrigger"), "Adding an action preserves inherited trigger settings instead of overriding them");

        var info = catalog.Groups.Single(g => g.Key == "info");
        var infoValues = AttributePaneRules.AddGroup(marker, info, categories);
        Check(infoValues["info"] == "Hello\nWorld" && infoValues["infoRange"] == "6", "Add Attributes turns inherited group values into exact local overrides");
        var alpha = catalog.Groups.Single(g => g.Key == "alpha");
        var other = XElement.Parse("<POI type='pack.other'/>");
        var first = AttributePaneRules.AddGroup(marker, alpha, categories);
        var second = AttributePaneRules.AddGroup(other, alpha, categories);
        Check(first["alpha"] == "0.7" && second["alpha"] == "0.9", "Multi-target additions preserve each target's own inherited value");
        AttributeRules.Apply([marker], first, (_, _) => true);
        Check(AttributeRules.Inherited(marker, categories).Single(a => a.Name == "alpha").Overridden, "Adding inherited attribute marks the matching inherited row overridden");
        marker.SetAttributeValue("alpha", "0.2");
        Check(!AttributePaneRules.AddGroup(marker, alpha, categories).ContainsKey("alpha"), "Adding an existing local group does not replace edited values");
    }
}
