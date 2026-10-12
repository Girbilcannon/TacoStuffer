using System.Numerics;
using System.Runtime.InteropServices;

namespace TacoStuffer.Models;

[StructLayout(LayoutKind.Sequential)]
public readonly struct TrailGpuVertex(Vector3 position, Vector2 uv, Vector3 center)
{
    public readonly Vector3 Position = position;
    public readonly Vector2 Uv = uv;
    public readonly Vector3 Center = center;
}

public static class GpuTrailGeometry
{
    // Four vertices per edge keep the same flat ribbon shape as the existing
    // preview. UV distance never depends on point IDs or camera position.
    public static TrailGpuVertex[] Build(EditableTrail trail, TrailSegment segment,
        IReadOnlyDictionary<string, Vector3>? preview = null, int firstEdge = 1, float initialLength = 0)
    {
        if (segment.Points.Count < 2) return [];
        firstEdge = Math.Clamp(firstEdge, 1, segment.Points.Count);
        var vertices = new List<TrailGpuVertex>(checked((segment.Points.Count - firstEdge) * 4));
        float length = initialLength;
        Vector3 Position(TrailPoint p) => preview != null && preview.TryGetValue($"point|{trail.Number}|{segment.Id}|{p.Id}", out var moved) ? moved : p.Position;
        for (int i = firstEdge; i < segment.Points.Count; i++)
        {
            var a = Position(segment.Points[i - 1]);
            var b = Position(segment.Points[i]);
            if (!EditableTrails.Finite(a) || !EditableTrails.Finite(b)) continue;
            var direction = b - a;
            float distance = direction.Length();
            if (!float.IsFinite(distance) || distance < .000001f) continue;
            var side = Vector3.Cross(direction, Vector3.UnitY);
            side = side.LengthSquared() < .000001f ? Vector3.UnitX * .508f : Vector3.Normalize(side) * .508f;
            vertices.Add(new(a - side, new(0, -length), a));
            vertices.Add(new(a + side, new(1, -length), a));
            vertices.Add(new(b + side, new(1, -(length + distance)), b));
            vertices.Add(new(b - side, new(0, -(length + distance)), b));
            length += distance;
        }
        return vertices.ToArray();
    }

    public static uint[] Indices(int edgeCount)
    {
        uint[] indices = new uint[checked(edgeCount * 6)];
        for (int i = 0; i < edgeCount; i++)
        {
            uint v = checked((uint)i * 4);
            int j = i * 6;
            indices[j] = v;
            indices[j + 1] = v + 1;
            indices[j + 2] = v + 2;
            indices[j + 3] = v;
            indices[j + 4] = v + 2;
            indices[j + 5] = v + 3;
        }
        return indices;
    }
}
