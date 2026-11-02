using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public static class ElementIdentity
{
    private static readonly ConditionalWeakTable<XElement, SiblingOrder> Siblings = new();
    private sealed class SiblingOrder
    {
        private Dictionary<XElement, int>? ordinals;
        private Dictionary<string, int> guidCounts = [];
        public SiblingOrder(XElement parent)
        {
            parent.Changed += (_, _) => ordinals = null;
        }

        public int Ordinal(XElement parent, XElement element)
        {
            if (ordinals == null)
            {
                ordinals = parent.Elements().Select((e, index) => (e, index)).ToDictionary(p => p.e, p => p.index);
                guidCounts = parent.Elements().Where(e => e.Attribute("GUID") != null).GroupBy(e => e.Name.LocalName + ":" + (string)e.Attribute("GUID")!).ToDictionary(g => g.Key, g => g.Count());
            }
            return ordinals.GetValueOrDefault(element);
        }
        public bool Duplicate(XElement parent, XElement element, string key) { _ = Ordinal(parent, element); return guidCounts.GetValueOrDefault(key) > 1; }
    }

    public static string Key(XElement element)
    {
        if (element.Name.LocalName == "MarkerCategory") return "category:" + CategoryOperations.PathOf(element);
        string? guid = (string?)element.Attribute("GUID");
        if (guid != null)
        {
            string key = element.Name.LocalName + ":" + guid;
            if (element.Parent is { } owner)
            {
                var siblings = Siblings.GetValue(owner, p => new SiblingOrder(p));
                if (siblings.Duplicate(owner, element, key)) key += ":duplicate:" + siblings.Ordinal(owner, element);
            }
            return key;
        }
        int ordinal = element.Parent is { } parent ? Siblings.GetValue(parent, p => new SiblingOrder(p)).Ordinal(parent, element) : 0;
        return element.Name.LocalName + ":index:" + ordinal;
    }
}
