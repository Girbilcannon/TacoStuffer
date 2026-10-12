using System.Numerics;

namespace TacoStuffer.Models;

public readonly record struct TrailPickTriangle(TrailSegmentRef Segment, Vector2 A, Vector2 B, Vector2 C, float DepthA, float DepthB, float DepthC);

public static class TrailPicking
{
    // Test the projected ribbon itself, rather than a centerline or its bounding box.
    // Perspective-correct depth resolves crossings in favor of the nearest visible ribbon.
    public static TrailSegmentRef? Hit(IReadOnlyList<TrailPickTriangle> triangles, Vector2 mouse)
    {
        TrailSegmentRef? result = null;
        float nearest = float.PositiveInfinity;
        foreach (var triangle in triangles)
        if (TryDepth(triangle, mouse, out float depth) && depth <= nearest)
        {
            nearest = depth;
            result = triangle.Segment;
        }
        return result;
    }

    public static bool TryDepth(TrailPickTriangle t, Vector2 mouse, out float depth)
    {
        depth = 0;
        if (mouse.X < Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) || mouse.X > Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))
            || mouse.Y < Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) || mouse.Y > Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))) return false;
        float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        float area = Cross(t.B - t.A, t.C - t.A);
        if (!float.IsFinite(area) || Math.Abs(area) < .000001f || t.DepthA <= 0 || t.DepthB <= 0 || t.DepthC <= 0) return false;
        float b = Cross(mouse - t.A, t.C - t.A) / area;
        float c = Cross(t.B - t.A, mouse - t.A) / area;
        float a = 1 - b - c;
        if (a < -.00001f || b < -.00001f || c < -.00001f) return false;
        depth = 1 / (a / t.DepthA + b / t.DepthB + c / t.DepthC);
        return float.IsFinite(depth) && depth > 0;
    }

    public static long? NearestPoint(IReadOnlyList<(long Id, Vector2 Screen)> points, Vector2 mouse)
    {
        long? result = null;
        float nearest = float.PositiveInfinity;
        foreach (var point in points)
        {
            float distance = Vector2.DistanceSquared(point.Screen, mouse);
            if (!float.IsFinite(distance) || distance >= nearest) continue;
            nearest = distance;
            result = point.Id;
        }
        return result;
    }
}
