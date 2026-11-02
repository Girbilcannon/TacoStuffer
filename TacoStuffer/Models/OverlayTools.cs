using System.Numerics;

namespace TacoStuffer.Models;

public enum OverlayEditTool { Select, Marquee, InsertPoint, RemovePoint, SplitSegment, MergeSegments, ReverseSegment }
public sealed record OverlayClick(int X, int Y, int EndX, int EndY, bool Shift, bool Control, nint Root);

// Classify the complete physical gesture, including motion that a camera later
// warps back to its starting position. Holding either mouse chord is never a click.
public sealed class OverlayClickGesture
{
    private bool active, moved, chord;
    private Vector2 origin;
    private long started;
    public void Begin(Vector2 point, long now, bool otherDown) { active = true; moved = false; chord = otherDown; origin = point; started = now; }
    public void Move(Vector2 point) { if (active && Vector2.DistanceSquared(origin, point) > 36) moved = true; }
    public void OtherButton() { if (active) chord = true; }
    public bool End(Vector2 point, long now, bool otherDown)
    {
        if (!active) return false; Move(point); active = false;
        return !moved && !chord && !otherDown && now - started is >= 0 and <= 500;
    }
    public void Cancel() => active = false;
}

public static class OverlaySelection
{
    public static List<string> Combine(IEnumerable<string> current, IEnumerable<string> incoming, bool shift, bool control)
    {
        var result = (shift || control ? current : []).Distinct().ToList();
        foreach (string key in incoming.Distinct())
            if (control && result.Contains(key)) result.Remove(key); else if (!result.Contains(key)) result.Add(key);
        return result;
    }
}

public static class TrailTopology
{
    public static void Remove(EditableTrail trail, TrailPointRef point, bool split)
    {
        var segment = trail.Segments.SingleOrDefault(s => s.Id == point.Segment) ?? throw new InvalidOperationException("The segment no longer exists.");
        int index = segment.Points.FindIndex(p => p.Id == point.Point);
        if (point.Trail != trail.Number || index < 0) throw new InvalidOperationException("The point no longer exists.");
        if (split && (index == 0 || index == segment.Points.Count - 1)) throw new InvalidOperationException("Choose an interior point to split the segment.");
        EditableTrails.DeletePoints(trail, segment.Id, new HashSet<long> { point.Point }, split);
        trail.Segments.RemoveAll(s => s.Points.Count == 0);
    }
    public static bool Endpoint(EditableTrail trail, TrailPointRef point)
    {
        var s = trail.Segments.FirstOrDefault(s => s.Id == point.Segment);
        return trail.Number == point.Trail && s?.Points.Count > 0 && (s.Points[0].Id == point.Point || s.Points[^1].Id == point.Point);
    }
    // Callers supply trails in Outliner order. Segment and point list order is
    // authoritative; endpoint click order must never reverse recorded geometry.
    public static TrailPointRef Merge(IReadOnlyList<EditableTrail> hierarchy, TrailPointRef a, TrailPointRef b)
    {
        var first = hierarchy.SingleOrDefault(t => t.Number == a.Trail)
            ?? throw new InvalidOperationException("The first trail no longer exists.");
        var second = hierarchy.SingleOrDefault(t => t.Number == b.Trail)
            ?? throw new InvalidOperationException("The second trail no longer exists.");
        if (first.MapId != second.MapId || (first == second && a.Segment == b.Segment))
            throw new InvalidOperationException("Choose endpoints from two different segments on the same map.");
        if (!Endpoint(first, a) || !Endpoint(second, b))
            throw new InvalidOperationException("Choose segment endpoints, not interior points.");
        bool swap = first == second
            ? first.Segments.FindIndex(s => s.Id == a.Segment) > first.Segments.FindIndex(s => s.Id == b.Segment)
            : hierarchy.ToList().IndexOf(first) > hierarchy.ToList().IndexOf(second);
        if (swap) { (first, second) = (second, first); (a, b) = (b, a); }
        var target = first.Segments.Single(s => s.Id == a.Segment);
        var donor = second.Segments.Single(s => s.Id == b.Segment);
        var left = target.Points.ToList();
        var right = donor.Points.ToList();
        if (left[^1].Position == right[0].Position) right.RemoveAt(0);
        long next = Math.Max(target.NextPoint, left.Max(p => p.Id) + 1);
        if (next <= 0 || right.Count > long.MaxValue - next)
            throw new InvalidOperationException("The segment point counter is exhausted.");
        var added = right.Select(p => new TrailPoint { Id = next++, Position = p.Position }).ToList();
        target.ResetResampling();
        target.Points = left.Concat(added).ToList();
        target.NextPoint = next;
        // Keep an empty donor trail's XML/GUID for existing script references.
        second.Segments.Remove(donor);
        return new(first.Number, target.Id, left[^1].Id);
    }

    public static void Reverse(EditableTrail trail, TrailPointRef point)
    {
        var segment = trail.Segments.SingleOrDefault(s => s.Id == point.Segment);
        if (trail.Number != point.Trail || segment == null || !segment.Points.Any(p => p.Id == point.Point))
            throw new InvalidOperationException("The point no longer exists.");
        if (segment.Points.Count < 2)
            throw new InvalidOperationException("A segment needs at least two points to have a direction.");
        segment.ResetResampling();
        segment.Points.Reverse(); // IDs remain identities; list order defines flow/export.
    }
}
