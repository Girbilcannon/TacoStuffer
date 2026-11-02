using System.Xml.Linq;

namespace TacoStuffer.Models;

public static class AttributePaneRules
{
    public static bool HasLocal(XElement element, string name) => element.Attributes().Any(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
    public static bool HasLocalGroup(XElement element, AttributeDefinition group) => group.Applies(element) && (group.Key == "resetguid" ? HasLocal(element, "resetguid") : group.Members.Any(m => HasLocal(element, m.Name)));
    public static bool UsesTrigger(string group) => group is "copy" or "showHide" or "toggle" or "behavior" or "bounce" or "resetguid";

    // Adding a group makes inherited values local without replacing them with
    // generic defaults. Absent optional fields remain omitted until requested.
    public static Dictionary<string, string?> AddGroup(XElement element, AttributeDefinition group, IReadOnlyDictionary<string, XElement> categories)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in group.Members)
        {
            if (HasLocal(element, member.Name)) continue;
            string effective = AttributeRules.Effective(element, member.Name, "\0", categories);
            if (effective != "\0" || !member.Optional) values[member.Name] = effective != "\0" ? effective : member.DefaultValue;
        }
        // Dependencies are shown as advisory alerts. Adding an action must not
        // silently add Trigger or clear an alert before the author chooses it.
        return values;
    }
}
