using System.Numerics;

namespace TacoStuffer.Models;

public sealed record CompassTelemetry(ushort Width, ushort Height, float Rotation, Vector2 Player, Vector2 Center, float Scale, int UiSize);

public sealed record MapViewport(float X, float Y, float Width, float Height);

public static class MapProjection
{
    public static float UiScale(int size) => size switch { 0 => 0.9f, 2 => 1.111f, 3 => 1.224f, _ => 1 };

    public static MapViewport Viewport(MumbleSnapshot frame, float physicalWidth, float physicalHeight)
    {
        if ((frame.UiState & 1) != 0) return new(0, 0, physicalWidth, physicalHeight);
        var map = frame.Compass;
        if (map == null) return new(0, 0, 0, 0);
        float small = Math.Min(1, Math.Min(physicalWidth / 1024, physicalHeight / 768));
        float scale = UiScale(map.UiSize) * small;
        float w = map.Width * scale, h = map.Height * scale;
        float bottom = (map.UiSize switch { 0 => 33, 2 => 41, 3 => 45, _ => 37 }) * small;
        return (frame.UiState & 2) != 0 ? new(physicalWidth - w, 1, w, h) : new(physicalWidth - w, physicalHeight - h - bottom, w, h);
    }

    public static bool TryProject(MumbleSnapshot frame, Vector3 position, MapViewport viewport, float physicalWidth, float physicalHeight, out Vector2 point)
    {
        point = default;
        var map = frame.Compass;
        if (map == null || !float.IsFinite(map.Rotation) || !float.IsFinite(map.Player.X) || !float.IsFinite(map.Player.Y) || !float.IsFinite(map.Center.X) || !float.IsFinite(map.Center.Y) || !float.IsFinite(map.Scale) || map.Scale <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return false;
        float small = Math.Min(1, Math.Min(physicalWidth / 1024, physicalHeight / 768));
        var delta = new Vector2(position.X - frame.AvatarPosition.X, -(position.Z - frame.AvatarPosition.Z)) / (0.0254f * 24) - (map.Center - map.Player) * small;
        float angle = (frame.UiState & 1) != 0 || (frame.UiState & 4) == 0 ? 0 : map.Rotation;
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        delta = new(delta.X * c - delta.Y * s, delta.X * s + delta.Y * c);
        point = new Vector2(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2) + delta * (UiScale(map.UiSize) / map.Scale);
        return float.IsFinite(point.X) && float.IsFinite(point.Y);
    }
}

public readonly record struct MapRibbonBasis(Vector2 Origin, Vector2 WorldOrigin, Vector2 X, Vector2 Z)
{
    public Vector2 Project(Vector3 position) => Origin + (position.X - WorldOrigin.X) * X + (position.Z - WorldOrigin.Y) * Z;

    public Vector2 ProjectSide(Vector3 position, Vector3 center, float halfWidth)
    {
        var side = (position.X - center.X) * X + (position.Z - center.Z) * Z;
        return Project(center) + (side.LengthSquared() > 1e-12f ? Vector2.Normalize(side) : Vector2.UnitX) * halfWidth;
    }
}

public static class MapRibbonProjection
{
    public static bool TryBasis(MumbleSnapshot frame, float width, float height, out MapRibbonBasis basis)
    {
        basis = default;
        var viewport = MapProjection.Viewport(frame, width, height);
        var anchor = frame.AvatarPosition;
        if (!MapProjection.TryProject(frame, anchor, viewport, width, height, out var origin)
            || !MapProjection.TryProject(frame, anchor + Vector3.UnitX, viewport, width, height, out var x)
            || !MapProjection.TryProject(frame, anchor + Vector3.UnitZ, viewport, width, height, out var z)) return false;
        basis = new(origin, new(anchor.X, anchor.Z), x - origin, z - origin);
        return basis.X.LengthSquared() > 1e-12f;
    }

    public static float Width(float size, int uiSize) => ScaleNormalization.MapTrailWidth(size, 1, true, uiSize, 1, 1);

    public static float UvScale(MapRibbonBasis basis, float width, float trailScale) => basis.X.Length() / Math.Max(1, width);
}
