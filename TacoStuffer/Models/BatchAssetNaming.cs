using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public static class BatchAssetNaming
{
    private static string Value(XElement e, string name) => e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PlanChanges(IReadOnlyList<XElement> targets,
        IReadOnlyDictionary<string, string> values, IEnumerable<XElement> allAssets)
    {
        var assets = allAssets.ToList();
        var plans = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var value in values)
        {
            if (value.Key is not ("name" or "DisplayName")) throw new InvalidOperationException("Only name and DisplayName can be renamed here.");
            plans.Add(value.Key, Plan(targets, value.Value, value.Key == "DisplayName", assets));
        }
        return plans;
    }

    // Plan the whole operation before writing XML. Targets must already be in
    // Outliner order; selection click order must never determine the sequence.
    public static IReadOnlyList<string> Plan(IReadOnlyList<XElement> targets, string input, bool displayName, IEnumerable<XElement> allAssets)
    {
        if (targets.Count == 0 || targets.Any(e => e.Name.LocalName is not ("POI" or "Trail")))
            throw new InvalidOperationException("Select markers and trails only to rename recorded assets together.");
        if (input.Any(char.IsControl) || (!displayName && input.Any(char.IsWhiteSpace)))
            throw new InvalidOperationException(displayName ? "Display names must use a single line." : "Internal names cannot contain spaces or other whitespace.");
        input = input.Trim();
        if (input.Length == 0) throw new InvalidOperationException("Enter a name before applying it.");
        var selected = targets.ToHashSet();
        var occupied = displayName ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : allAssets.Where(e => !selected.Contains(e)).Select(e => Value(e, "name")).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 1)
        {
            if (occupied.Contains(input)) throw new InvalidOperationException("Another asset already uses this internal name. Choose a unique name.");
            return [input];
        }
        int suffix = input.Length;
        while (suffix > 0 && input[suffix - 1] is >= '0' and <= '9') suffix--;
        bool numbered = suffix != input.Length;
        string prefix = numbered ? input[..suffix] : input + (displayName ? " " : "");
        int padding = numbered ? input.Length - suffix : 2;
        BigInteger number = numbered ? BigInteger.Parse(input[suffix..], CultureInfo.InvariantCulture) : BigInteger.One;
        var names = new List<string>();
        while (names.Count < targets.Count)
        {
            string candidate = prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0');
            number++;
            if (!occupied.Add(candidate)) continue;
            names.Add(candidate);
        }
        return names;
    }
}
