using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record PackSelection(int Document, int Element);
public sealed record AssetDropIdentity(int OldDocument,string OldKey,int NewDocument,string NewKey);

public static class PackEditing
{
    private static bool Under(string path, string parent) => path == parent || path.StartsWith(parent + '.', StringComparison.Ordinal);

    private static string Type(XElement e) => (string?)e.Attribute("type") ?? "";

    private static List<(int Document, XElement Element)> Resolve(List<XDocument> docs, IEnumerable<PackSelection> selection) =>
    selection.Distinct().Select(s => (s.Document, docs[s.Document].Descendants().ElementAt(s.Element))).ToList();

    private static Dictionary<string, XElement> Templates(IEnumerable<XDocument> docs)
    {
        var result = new Dictionary<string, XElement>();
        foreach (var c in docs.SelectMany(d => d.Descendants("MarkerCategory")))
        {
            string path = CategoryOperations.PathOf(c);
            if (!result.TryGetValue(path, out var known)) result[path] = new XElement(c.Name, c.Attributes());
            else MergeAttributes(known, c);
        }
        return result;
    }

    private static void MergeAttributes(XElement destination, XElement source)
    {
        foreach (var a in source.Attributes())
        {
            var known = destination.Attribute(a.Name);
            if (known != null && known.Value != a.Value) throw new InvalidOperationException("Conflicting category attributes at " + CategoryOperations.PathOf(source) + ": " + a.Name);
            if (known == null) destination.Add(new XAttribute(a));
        }
    }

    private static XElement Ensure(XDocument doc, string path, Dictionary<string, XElement> templates)
    {
        var matches = doc.Descendants("MarkerCategory").Where(c => CategoryOperations.PathOf(c) == path).ToList();
        if (matches.Count > 1) throw new InvalidOperationException("Duplicate category in one XML: " + path);
        if (matches.Count == 1) return matches[0];
        if (!templates.TryGetValue(path, out var template)) throw new InvalidOperationException("Missing category: " + path);
        var added = new XElement(template.Name, template.Attributes());
        int dot = path.LastIndexOf('.');
        if (dot < 0) doc.Root!.AddFirst(added);
        else Ensure(doc, path[..dot], templates).Add(added);
        return added;
    }

    private static void MergeCategory(XElement parent, XElement source)
    {
        var matches = parent.Elements("MarkerCategory").Where(c => (string?)c.Attribute("name") == (string?)source.Attribute("name")).ToList();
        if (matches.Count > 1) throw new InvalidOperationException("Duplicate destination category.");
        if (matches.Count == 0)
        {
            parent.Add(source);
            return;
        }
        var existing = matches[0];
        MergeAttributes(existing, source);
        foreach (var node in source.Nodes().ToList())
        {
            node.Remove();
            if (node is XElement child && child.Name == "MarkerCategory") MergeCategory(existing, child);
            else existing.Add(node);
        }
    }

    public static List<XDocument> DropAssets(IReadOnlyList<XDocument> originals,IEnumerable<PackSelection> selection,PackSelection target,CategoryDropPosition position)
        => DropAssets(originals,selection,target,position,out _);

    public static List<XDocument> DropAssets(IReadOnlyList<XDocument> originals,IEnumerable<PackSelection> selection,PackSelection target,CategoryDropPosition position,out IReadOnlyList<AssetDropIdentity> identities)
    {
        var docs = originals.Select(d => new XDocument(d)).ToList();
        var previous = docs.SelectMany((d,index) => d.Descendants().Where(e => e.Name.LocalName is "MarkerCategory" or "POI" or "Trail").Select(e => (Document:index,Key:ElementIdentity.Key(e),Element:e))).ToList();
        var moving = Resolve(docs,selection).Select(r => r.Element).ToList();
        var anchor = docs[target.Document].Descendants().ElementAt(target.Element);
        if (moving.Count == 0 || moving.Any(e => e.Name.LocalName is not ("POI" or "Trail"))) throw new InvalidOperationException("Only markers and trails can be dropped here.");
        if (moving.Contains(anchor)) throw new InvalidOperationException("Drop outside the dragged selection.");
        string category;
        if (anchor.Name == "MarkerCategory")
        {
            category = CategoryOperations.PathOf(anchor);
            if (position != CategoryDropPosition.Child) category = category.Contains('.') ? category[..category.LastIndexOf('.')] : "";
            if (category.Length == 0) throw new InvalidOperationException("Drop inside the pack heading; assets must belong to a category.");
        }
        else if (anchor.Name.LocalName is "POI" or "Trail" && position != CategoryDropPosition.Child) category = Type(anchor);
        else throw new InvalidOperationException("Markers and trails cannot contain children.");
        var templates = Templates(docs);
        if (category.Length > 0) Ensure(docs[target.Document],category,templates);
        var pois = docs[target.Document].Root!.Element("POIs");
        if (pois == null) { pois = new XElement("POIs"); docs[target.Document].Root!.Add(pois); }
        foreach (var item in moving) { item.Remove(); item.SetAttributeValue("type",category); }
        if (anchor.Name.LocalName is "POI" or "Trail")
        {
            if (position == CategoryDropPosition.Before) anchor.AddBeforeSelf(moving);
            else anchor.AddAfterSelf(moving);
        }
        else pois.Add(moving);
        var documentIndexes = docs.Select((d,index) => (d,index)).ToDictionary(p => p.d,p => p.index);
        identities = previous.Select(p => new AssetDropIdentity(p.Document,p.Key,documentIndexes[p.Element.Document!],ElementIdentity.Key(p.Element))).ToList();
        return docs;
    }

    public static List<XDocument> Move(IReadOnlyList<XDocument> originals, IEnumerable<PackSelection> selection, string operation, string destination, int destinationDocument = -1)
    {
        var docs = originals.Select(d => new XDocument(d)).ToList();
        var rows = Resolve(docs, selection);
        var categories = rows.Where(r => r.Element.Name == "MarkerCategory").ToList();
        if (categories.Any(r => !CategoryOperations.PathOf(r.Element).Contains('.'))) throw new InvalidOperationException("The pack heading cannot be moved. Select its children instead.");
        var items = rows.Where(r => r.Element.Name.LocalName is "POI" or "Trail").ToList();
        if (operation == "category")
        {
            var paths = categories.Select(r => CategoryOperations.PathOf(r.Element)).Distinct().ToList();
            paths = paths.Where(p => !paths.Any(a => a != p && Under(p, a))).ToList();
            if (paths.Any(p => Under(destination, p))) throw new InvalidOperationException("A category cannot move into itself or its descendants.");
            // Items inside a selected category follow its path; do not move them twice.
            var looseItems = items.Where(r => !paths.Any(p => Under(Type(r.Element), p)))
            .Select(r => (r.Document, Index: docs[r.Document].Root!.Element("POIs")!.Elements().ToList().IndexOf(r.Element))).ToList();
            foreach (string path in paths) docs = CategoryOperations.Move(docs, path, destination, CategoryDropPosition.Child, out _);
            var templates = Templates(docs);
            if (!templates.ContainsKey(destination)) throw new InvalidOperationException("The destination category no longer exists.");
            foreach (var item in looseItems)
            {
                Ensure(docs[item.Document], destination, templates);
                docs[item.Document].Root!.Element("POIs")!.Elements().ElementAt(item.Index).SetAttributeValue("type", destination);
            }
        }
        else if (operation == "xml")
        {
            if (destinationDocument < 0 || destinationDocument >= docs.Count) throw new InvalidOperationException("Choose a destination XML.");
            var templates = Templates(docs);
            var roots = categories.Where(r => !categories.Any(a => a.Document == r.Document && a.Element != r.Element && Under(CategoryOperations.PathOf(r.Element), CategoryOperations.PathOf(a.Element)))).ToList();
            var movingItems = new HashSet<XElement>(items.Where(r => r.Document != destinationDocument).Select(r => r.Element));
            foreach (var row in roots.Where(r => r.Document != destinationDocument))
            {
                string path = CategoryOperations.PathOf(row.Element);
                foreach (var item in docs[row.Document].Root!.Element("POIs")?.Elements() ?? Enumerable.Empty<XElement>()) if (Under(Type(item), path)) movingItems.Add(item);
                string parentPath = path[..path.LastIndexOf('.')];
                var parent = Ensure(docs[destinationDocument], parentPath, templates);
                row.Element.Remove();
                MergeCategory(parent, row.Element);
            }
            var destinationDoc = docs[destinationDocument];
            foreach (var item in movingItems)
            {
                string path = Type(item);
                if (path.Length > 0) Ensure(destinationDoc, path, templates);
                var pois = destinationDoc.Root!.Element("POIs");
                if (pois == null)
                {
                    pois = new XElement("POIs");
                    destinationDoc.Root.Add(pois);
                }
                string? guid = (string?)item.Attribute("GUID");
                if (guid != null && pois.Elements().Any(e => (string?)e.Attribute("GUID") == guid)) throw new InvalidOperationException("The destination already contains GUID " + guid);
                item.Remove();
                pois.Add(item);
            }
        }
        else throw new InvalidOperationException("Unknown move operation.");
        return docs;
    }

    // Categories are a shared logical hierarchy: deletion removes their declarations
    // and assigned elements throughout the pack, leaving physical assets alone.
    public static List<XDocument> Delete(IReadOnlyList<XDocument> originals, IEnumerable<PackSelection> selection)
    {
        var docs = originals.Select(d => new XDocument(d)).ToList();
        var rows = Resolve(docs, selection);
        var paths = rows.Where(r => r.Element.Name == "MarkerCategory").Select(r => CategoryOperations.PathOf(r.Element)).Distinct().ToList();
        if (paths.Any(p => !p.Contains('.'))) throw new InvalidOperationException("The pack heading cannot be deleted. Select its children instead.");
        foreach (var doc in docs)
        {
            foreach (var e in doc.Descendants().Where(e => e.Name == "MarkerCategory" ? paths.Any(p => Under(CategoryOperations.PathOf(e), p)) : (e.Name.LocalName is "POI" or "Trail") && paths.Any(p => Under(Type(e), p))).ToList())
            if (e.Parent != null) e.Remove();
        }
        foreach (var row in rows.Where(r => r.Element.Name.LocalName is "POI" or "Trail")) if (row.Element.Parent != null) row.Element.Remove();
        return docs;
    }
}
