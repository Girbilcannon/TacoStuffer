using System.Xml.Linq;

namespace TacoStuffer.Models;

public static class AttributeRules
{
    public sealed record InheritedAttribute(string Name, string Value, string Category, bool Overridden);
    private static string Local(XElement element, string name, string fallback) => element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;

    public static IReadOnlyList<InheritedAttribute> Inherited(XElement element, IReadOnlyDictionary<string, XElement> categories)
    {
        var result = new Dictionary<string, InheritedAttribute>(StringComparer.OrdinalIgnoreCase);
        if (element.Name.LocalName is not ("POI" or "Trail")) return [];
        string path = Local(element, "type", "");
        while (path.Length > 0)
        {
            if (categories.TryGetValue(path, out var category))
                foreach (var attribute in category.Attributes())
                {
                    string name = attribute.Name.LocalName;
                    if (name.ToLowerInvariant() is "name" or "displayname" or "guid" or "type" or "isseparator" or "ishidden" or "defaulttoggle") continue;
                    result.TryAdd(name, new(name, attribute.Value, path, Local(element, name, "\0") != "\0"));
                }
            int dot = path.LastIndexOf('.'); path = dot < 0 ? "" : path[..dot];
        }
        return result.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Effective(XElement element, string name, string fallback, IReadOnlyDictionary<string, XElement> categories)
    {
        string local = Local(element, name, "\0");
        if (local != "\0") return local;
        if (element.Name == "MarkerCategory" || element.Name.LocalName is not ("POI" or "Trail")) return fallback;
        if (name.ToLowerInvariant() is "name" or "displayname" or "guid" or "type" or "isseparator" or "ishidden" or "defaulttoggle") return fallback;
        string path = Local(element, "type", "");
        while (path.Length > 0)
        {
            if (categories.TryGetValue(path, out var category))
            {
                string inherited = Local(category, name, "\0");
                if (inherited != "\0") return inherited;
            }
            int dot = path.LastIndexOf('.');
            path = dot < 0 ? "" : path[..dot];
        }
        return fallback;
    }

    public static void Apply(IReadOnlyList<XElement> targets, IReadOnlyDictionary<string, string?> values, Func<XElement, string, bool> applies)
    {
        foreach (var target in targets) foreach (var value in values)
        if (value.Value != null && !applies(target, value.Key)) throw new InvalidOperationException(value.Key + " does not apply to every selected object.");
        foreach (var target in targets) foreach (var value in values)
        {
            var attributes = target.Attributes().Where(a => a.Name.LocalName.Equals(value.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (value.Value == null)
            {
                foreach (var a in attributes) a.Remove();
            }
            else if (attributes.Count == 0) target.SetAttributeValue(value.Key, value.Value);
            else
            {
                attributes[0].Value = value.Value;
                foreach (var a in attributes.Skip(1)) a.Remove();
            }
        }
    }
}
