using System.Globalization;

namespace TacoStuffer.Models;

public static class CheckpointAuthoring
{
    // Custom checkpoint colors are RRGGBBAA, not the ordinary ARGB color format.
    public static bool TryRgba(string text, out uint rgba) => uint.TryParse(text, NumberStyles.HexNumber,
        CultureInfo.InvariantCulture, out rgba) && text.Length == 8 && text.All(Uri.IsHexDigit);
    public static bool TryCount(string text, out int count) => int.TryParse(text, NumberStyles.None,
        CultureInfo.InvariantCulture, out count) && count >= 0;

    // Reader-neutral reference traversal for the Taquito handoff. It does not
    // execute custom checkpoints in Taco Stuffer's ordinary behavior preview.
    public static IReadOnlyDictionary<string, int> LookAhead(string root, int count, IReadOnlyDictionary<string, IReadOnlyList<string>> links)
    {
        root = PathingBehavior.GuidKey(root);
        var distances = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.Length == 0 || !links.ContainsKey(root)) return distances;
        var visited = new HashSet<string>(StringComparer.Ordinal) { root };
        var queue = new Queue<(string Guid, int Depth)>(); queue.Enqueue((root, 0));
        while (queue.TryDequeue(out var current))
        {
            if (current.Depth >= count || !links.TryGetValue(current.Guid, out var next)) continue;
            foreach (string value in next)
            {
                string key = PathingBehavior.GuidKey(value);
                if (key.Length == 0 || !links.ContainsKey(key) || !visited.Add(key)) continue;
                int depth = current.Depth + 1;
                distances[key] = depth; queue.Enqueue((key, depth));
            }
        }
        return distances;
    }

    public static IReadOnlyList<string> Reachable(string root, IReadOnlyDictionary<string, IReadOnlyList<string>> links)
    {
        string key = PathingBehavior.GuidKey(root);
        return key.Length == 0 || !links.ContainsKey(key) ? [] : new[] { key }.Concat(LookAhead(root, int.MaxValue, links).Keys).ToList();
    }
}
