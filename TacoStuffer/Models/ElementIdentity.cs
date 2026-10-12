using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public static class ElementIdentity
{
    private static readonly ConditionalWeakTable<XElement, SiblingOrder> Siblings = new();
    private sealed class SiblingOrder
    {
        private Dictionary<XElement, int>? ordinals;
        public SiblingOrder(XElement parent)
        {
            parent.Changed += (_, _) => ordinals = null;
        }

        public int Ordinal(XElement parent, XElement element)
        {
            ordinals ??= parent.Elements().Select((e, index) => (e, index)).ToDictionary(p => p.e, p => p.index);
            return ordinals.GetValueOrDefault(element);
        }
    }

    public static string Key(XElement element)
    {
        if (element.Name.LocalName == "MarkerCategory") return "category:" + CategoryOperations.PathOf(element);
        string? guid = (string?)element.Attribute("GUID");
        if (guid != null) return element.Name.LocalName + ":" + guid;
        int ordinal = element.Parent is { } parent ? Siblings.GetValue(parent, p => new SiblingOrder(p)).Ordinal(parent, element) : 0;
        return element.Name.LocalName + ":index:" + ordinal;
    }
}
