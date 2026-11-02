namespace TacoStuffer.Models;

// The UI label is never used as a category namespace or a packaged file path.
public sealed record PathChoice(string Value, string Label, int Depth = 0, bool IsSelectable = true)
{
    public string Text => new string(' ', Math.Max(0, Depth) * 3) + Label;
    public override string ToString() => Text;
}

public static class PathChoices
{
    public static List<PathChoice> Categories(IReadOnlyDictionary<string, string> labels,
        IEnumerable<string>? paths = null, string? current = null, bool includeNone = false)
    {
        var values = (paths ?? labels.Keys).Distinct(StringComparer.Ordinal).ToList();
        if (!string.IsNullOrEmpty(current) && !values.Contains(current, StringComparer.Ordinal)) values.Add(current);
        var choices = values.Where(p => p.Length > 0).OrderBy(p => p, NamespaceComparer.Instance)
            .Select(p => new PathChoice(p, labels.TryGetValue(p, out string? label) && !string.IsNullOrWhiteSpace(label)
                ? label : p[(p.LastIndexOf('.') + 1)..], p.Count(c => c == '.'))).ToList();
        if (includeNone) choices.Insert(0, new("", "None (no action)"));
        return choices;
    }

    public static List<PathChoice> Files(IEnumerable<string> paths)
    {
        var result = new List<PathChoice>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.OrderBy(p => p.Replace('\\', '/'), NamespaceComparer.FileInstance))
        {
            string[] parts = path.Replace('\\', '/').Split('/');
            string folder = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                folder = folder.Length == 0 ? parts[i] : folder + "/" + parts[i];
                if (folders.Add(folder)) result.Add(new(folder, parts[i], i, false));
            }
            result.Add(new(path, parts[^1], parts.Length - 1));
        }
        return result;
    }

    private sealed class NamespaceComparer(char separator) : IComparer<string>
    {
        public static readonly NamespaceComparer Instance = new('.');
        public static readonly NamespaceComparer FileInstance = new('/');
        public int Compare(string? a, string? b)
        {
            string[] left = (a ?? "").Split(separator), right = (b ?? "").Split(separator);
            for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
            {
                int comparison = StringComparer.Ordinal.Compare(left[i], right[i]);
                if (comparison != 0) return comparison;
            }
            return left.Length.CompareTo(right.Length);
        }
    }
}
