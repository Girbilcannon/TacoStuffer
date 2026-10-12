using System.Xml.Linq;

namespace TacoStuffer.Models;

public static class CategoryOrder
{
    private static string Parent(string path) => path.Contains('.') ? path[..path.LastIndexOf('.')] : "";

    private static bool Under(string path, string root) => path == root || path.StartsWith(root + '.', StringComparison.Ordinal);

    public static List<string> Collect(IEnumerable<XDocument> documents, IEnumerable<string>? preferred = null)
    {
        var paths = documents.SelectMany(d => d.Descendants("MarkerCategory")).Select(CategoryOperations.PathOf).Distinct().ToList();
        var existing = paths.ToHashSet();
        return (preferred ?? []).Where(existing.Contains).Concat(paths).Distinct().ToList();
    }

    public static List<XDocument> Normalize(IReadOnlyList<XDocument> documents, IEnumerable<string>? preferred = null, bool clone = true)
    {
        var order = Collect(documents, preferred).Select((path, index) => (path, index)).ToDictionary(p => p.path, p => p.index);
        var result = clone ? documents.Select(d => new XDocument(d)).ToList() : documents.ToList();
        RemoveEmptyDuplicates(result);
        foreach (var parent in result.SelectMany(d => d.Descendants()).Where(p => p.Elements("MarkerCategory").Any()).ToList())
        {
            var children = parent.Elements("MarkerCategory").ToList();
            var sorted = new Queue<XElement>(children.OrderBy(c => order[CategoryOperations.PathOf(c)]));
            if (children.SequenceEqual(sorted)) continue;
            // Only replace category slots; comments, POIs and other nodes stay in place.
            var nodes = parent.Nodes().Select(n => n is XElement e && e.Name == "MarkerCategory" ? sorted.Dequeue() : n).ToList();
            foreach (var node in parent.Nodes().ToList()) node.Remove();
            foreach (var node in nodes) parent.Add(node);
        }
        return result;
    }

    private static void RemoveEmptyDuplicates(List<XDocument> documents)
    {
        // Index references once per document, rather than scanning every POI
        // for each repeated empty category declaration.
        var referenced = documents.ToDictionary(d => d, d =>
        {
            var paths = new HashSet<string>();
            foreach (var e in d.Root?.Element("POIs")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                string path = (string?)e.Attribute("type") ?? "";
                while (path.Length > 0)
                {
                    paths.Add(path);
                    int dot = path.LastIndexOf('.');
                    if (dot < 0) break;
                    path = path[..dot];
                }
            }
            return paths;
        });
        bool Needed(XDocument doc, XElement category) => category.HasElements
            || category.Nodes().Any(n => n is XComment || n is XText text && !string.IsNullOrWhiteSpace(text.Value))
            || referenced[doc].Contains(CategoryOperations.PathOf(category));
        bool SameAttributes(XElement a, XElement b) => a.Attributes().Count() == b.Attributes().Count() && a.Attributes().All(v => (string?)b.Attribute(v.Name) == v.Value);
        var groups = documents.SelectMany(d => d.Descendants("MarkerCategory").Select(c => (Document: d, Category: c)))
        .Where(r => CategoryOperations.PathOf(r.Category).Contains('.')).GroupBy(r => CategoryOperations.PathOf(r.Category)).ToList();
        foreach (var group in groups)
        {
            var rows = group.ToList();
            foreach (var row in rows)
            {
                if (row.Category.Parent == null || Needed(row.Document, row.Category)) continue;
                // Keep content-bearing declarations, or the first identical empty
                // menu entry. Never collapse differing attributes or pack headings.
                var keeper = rows.FirstOrDefault(r => r.Category.Parent != null && SameAttributes(r.Category, row.Category) && Needed(r.Document, r.Category));
                if (keeper.Category == null) keeper = rows.First(r => r.Category.Parent != null && SameAttributes(r.Category, row.Category));
                if (keeper.Category != row.Category) row.Category.Remove();
            }
        }
    }

    public static List<string> Move(IReadOnlyList<string> order, string source, string destination, string target, CategoryDropPosition position)
    {
        var branch = order.Where(p => Under(p, source)).Select(p => destination + p[source.Length..]).ToList();
        var remaining = order.Where(p => !Under(p, source)).ToList();
        int targetIndex = remaining.IndexOf(target);
        int insert = position == CategoryDropPosition.Before ? targetIndex : targetIndex + 1;
        if (position == CategoryDropPosition.Child)
        {
            int lastChild = remaining.FindLastIndex(p => Parent(p) == target);
            if (lastChild >= 0) insert = lastChild + 1;
        }
        remaining.InsertRange(insert < 0 ? remaining.Count : insert, branch);
        return remaining;
    }

    // XML edits may deliberately reorder a subset of shared children. Update those
    // slots in the canonical sequence without changing unrelated sibling order.
    public static List<string> AdoptDocument(List<string> order, XDocument before, XDocument after)
    {
        var old = before.Descendants("MarkerCategory").Select(CategoryOperations.PathOf).GroupBy(Parent).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var group in after.Descendants("MarkerCategory").Select(CategoryOperations.PathOf).GroupBy(Parent))
        {
            var desired = group.ToList();
            if (old.TryGetValue(group.Key, out var previous) && previous.SequenceEqual(desired)) continue;
            var desiredSet = desired.ToHashSet();
            var slots = Enumerable.Range(0, order.Count).Where(i => desiredSet.Contains(order[i])).ToList();
            if (slots.Count != desired.Count) continue;
            for (int i = 0; i < slots.Count; i++) order[slots[i]] = desired[i];
        }
        return order;
    }
}
