using System.Xml.Linq;

namespace TacoStuffer.Models;

public enum CategoryDropPosition
{
    Child, Before, After
}

public static class CategoryOperations
{
    public static string PathOf(XElement e) => string.Join('.', e.AncestorsAndSelf("MarkerCategory").Reverse().Select(c => (string?)c.Attribute("name") ?? "unnamed"));

    private static string ParentOf(string path) => path.Contains('.') ? path[..path.LastIndexOf('.')] : "";

    // Every operation edits clones. Callers replace their live documents only after all checks pass.
    public static List<XDocument> Move(IReadOnlyList<XDocument> documents, string source, string target, CategoryDropPosition position, out string newPath, IReadOnlyList<string>? preferredOrder = null)
    {
        if (!source.Contains('.')) throw new InvalidOperationException("The pack heading cannot be moved beneath another category.");
        if (source == target || target.StartsWith(source + '.', StringComparison.Ordinal)) throw new InvalidOperationException("A category cannot be moved into itself or one of its descendants.");
        var graph = new Dictionary<string, XElement>();
        foreach (var category in documents.SelectMany(d => d.Descendants("MarkerCategory")))
        {
            string path = PathOf(category);
            if (!graph.TryGetValue(path, out var known)) graph[path] = new XElement(category.Name, category.Attributes());
            else foreach (var attribute in category.Attributes())
                {
                    var existing = known.Attribute(attribute.Name);
                    if (existing != null && existing.Value != attribute.Value) throw new InvalidOperationException("Conflicting shared category definition: " + path + ". Resolve its attributes before moving categories.");
                    if (existing == null) known.Add(new XAttribute(attribute));
                }
        }
        if (!graph.ContainsKey(source) || !graph.ContainsKey(target)) throw new InvalidOperationException("The source or destination no longer exists.");
        string parent = position == CategoryDropPosition.Child ? target : ParentOf(target);
        if (parent.Length == 0) throw new InvalidOperationException("Drop inside the pack heading to make a top-level category.");
        newPath = parent + "." + source[(source.LastIndexOf('.') + 1)..];
        string destination = newPath;
        if (destination != source && graph.Keys.Any(p => p == destination || p.StartsWith(destination + '.', StringComparison.Ordinal))) throw new InvalidOperationException("A category with this name already exists at the destination.");
        var clones = documents.Select(d => new XDocument(d)).ToList();
        XElement Ensure(XDocument doc, string path)
        {
            var found = doc.Descendants("MarkerCategory").FirstOrDefault(c => PathOf(c) == path);
            if (found != null) return found;
            if (!graph.TryGetValue(path, out var template)) throw new InvalidOperationException("Missing parent category: " + path);
            var created = new XElement(template.Name, template.Attributes());
            string p = ParentOf(path);
            if (p.Length == 0) doc.Root!.AddFirst(created);
            else Ensure(doc, p).Add(created);
            return created;
        }
        foreach (var doc in clones)
        {
            var sources = doc.Descendants("MarkerCategory").Where(c => PathOf(c) == source).ToList();
            if (sources.Count > 1) throw new InvalidOperationException("Duplicate category declaration within one XML: " + source);
            if (sources.Count == 1)
            {
                var moved = sources[0];
                moved.Remove();
                var parentNode = Ensure(doc, parent);
                if (position == CategoryDropPosition.Child) parentNode.Add(moved);
                else
                {
                    var sibling = parentNode.Elements("MarkerCategory").FirstOrDefault(c => PathOf(c) == target);
                    if (sibling == null) parentNode.Add(moved);
                    else if (position == CategoryDropPosition.Before) sibling.AddBeforeSelf(moved);
                    else sibling.AddAfterSelf(moved);
                }
            }
            foreach (var item in doc.Descendants().Where(e => e.Attribute("type") != null))
            {
                string path = (string)item.Attribute("type")!;
                if (path == source || path.StartsWith(source + '.', StringComparison.Ordinal)) item.SetAttributeValue("type", destination + path[source.Length..]);
            }
        }
        var order = CategoryOrder.Move(CategoryOrder.Collect(documents, preferredOrder), source, destination, target, position);
        return CategoryOrder.Normalize(clones, order);
    }
}
