using System.Numerics;

namespace TacoStuffer.Models;

public sealed record CheckpointTestNode(string Key, string Guid, uint Map, Vector3 Position, float Radius,
    IReadOnlyList<string> Next, int LookAhead, uint Color, uint PreColor, bool Finish, bool Checkpoint,
    IReadOnlyList<(Vector3 A, Vector3 B)> Edges);

// All state is temporary. No XML, category preference or persisted behavior state
// is changed by this session. Finish trails use swept segment/capsule contact.
public sealed class CheckpointTestSession
{
    public Dictionary<string, CheckpointTestNode> Nodes { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Armed { get; } = [];
    public HashSet<string> Completed { get; } = [];
    private readonly Dictionary<string, bool> ready = [];
    private readonly List<HashSet<string>> forks = [];
    private Vector3? previous;
    public bool Finished { get; private set; }
    public void Start(IEnumerable<CheckpointTestNode> nodes, string root)
    {
        Nodes.Clear(); foreach (var node in nodes) Nodes.Add(node.Guid, node);
        Reset(root);
    }
    public void Reset(string root)
    {
        Armed.Clear(); Completed.Clear(); ready.Clear(); forks.Clear(); previous = null; Finished = false;
        if (Nodes.ContainsKey(root)) Armed.Add(root);
    }
    public void Pause() { previous = null; ready.Clear(); }
    public IReadOnlyDictionary<string, int> Visible()
    {
        var links = Nodes.ToDictionary(p => p.Key, p => p.Value.Next, StringComparer.Ordinal);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string root in Armed)
        {
            result[root] = 0;
            foreach (var next in CheckpointAuthoring.LookAhead(root, Nodes[root].LookAhead, links))
                if (!Completed.Contains(next.Key) && (!result.TryGetValue(next.Key, out int depth) || next.Value < depth)) result[next.Key] = next.Value;
        }
        return result;
    }
    public string? Update(Vector3 position, uint map, Func<CheckpointTestNode, bool> available)
    {
        if (previous is { } last && Vector3.DistanceSquared(last, position) > 200 * 200) Pause();
        var eligible = new List<CheckpointTestNode>();
        foreach (string guid in Armed)
        {
            var node = Nodes[guid];
            if (node.Map != map || !available(node)) { ready.Remove(guid); continue; }
            bool inside = Contact(node, position, position);
            if (!ready.TryGetValue(guid, out bool canEnter)) { ready[guid] = !inside; continue; }
            if (canEnter && (inside || previous is { } old && Contact(node, old, position))) eligible.Add(node);
            if (!inside) ready[guid] = true;
        }
        previous = position;
        var hit = eligible.OrderBy(n => Vector3.DistanceSquared(position, n.Position)).ThenBy(n => n.Guid, StringComparer.Ordinal).FirstOrDefault();
        if (hit == null) return null;
        foreach (var fork in forks.Where(f => f.Contains(hit.Guid)).ToList())
        {
            foreach (string sibling in fork.Where(g => g != hit.Guid)) { Armed.Remove(sibling); ready.Remove(sibling); }
            forks.Remove(fork);
        }
        Armed.Remove(hit.Guid); ready.Remove(hit.Guid); Completed.Add(hit.Guid);
        if (hit.Finish || hit.Checkpoint && hit.Next.Count == 0) { Finished = true; Armed.Clear(); return hit.Guid; }
        var next = hit.Next.Where(g => Nodes.ContainsKey(g) && !Completed.Contains(g)).Distinct().ToHashSet(StringComparer.Ordinal);
        foreach (string guid in next) { Armed.Add(guid); ready[guid] = !Contact(Nodes[guid], position, position); }
        if (next.Count > 1) forks.Add(next);
        return hit.Guid;
    }
    private static bool Contact(CheckpointTestNode node, Vector3 from, Vector3 to) => node.Edges.Count > 0
        ? node.Edges.Any(e => SegmentDistanceSquared(from, to, e.A, e.B) <= node.Radius * node.Radius)
        : SegmentDistanceSquared(from, to, node.Position, node.Position) <= node.Radius * node.Radius;

    public static float SegmentDistanceSquared(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
    {
        var d1 = q - p; var d2 = b - a; var r = p - a;
        float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
        if (aa < 1e-8f && ee < 1e-8f) return r.LengthSquared();
        if (aa < 1e-8f) { s = 0; t = Math.Clamp(f / ee, 0, 1); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (ee < 1e-8f) { t = 0; s = Math.Clamp(-c / aa, 0, 1); }
            else
            {
                float bb = Vector3.Dot(d1, d2), denom = aa * ee - bb * bb;
                s = denom > 1e-8f ? Math.Clamp((bb * f - c * ee) / denom, 0, 1) : 0;
                t = (bb * s + f) / ee;
                if (t < 0) { t = 0; s = Math.Clamp(-c / aa, 0, 1); }
                else if (t > 1) { t = 1; s = Math.Clamp((bb - c) / aa, 0, 1); }
            }
        }
        return Vector3.DistanceSquared(p + d1 * s, a + d2 * t);
    }
}
