namespace TacoStuffer.Models;

public sealed record SearchNode(string Id, string? Parent, string Text);

public static class ExplorerSearch
{
    public static bool Matches(string text, string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
    .All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

    public static HashSet<string> Find(IEnumerable<SearchNode> source, string query)
    {
        var nodes = source.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        var result = new HashSet<string>();
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var node in nodes.Values.Where(n => words.All(word => n.Text.Contains(word, StringComparison.OrdinalIgnoreCase))))
        {
            var visiting = new HashSet<string>();
            var current = node;
            while (visiting.Add(current.Id))
            {
                if (!result.Add(current.Id)) break;
                if (current.Parent == null || !nodes.TryGetValue(current.Parent, out var parent)) break;
                current = parent;
            }
        }
        return result;
    }
}
