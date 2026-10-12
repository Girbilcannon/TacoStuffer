using System.Numerics;

namespace TacoStuffer.Models;

public static class CameraProjection
{
    public const float DefaultFov = 0.872664626f;
    public const float NearClip = 0.05f;
    // Matches DecoTools MoveToolTab.cpp CameraFromMumble / Camera::Project:
    // raw meters, Y-up, Cross(worldUp, forward), vertical FOV in radians.
    public static bool TryProject(MumbleSnapshot camera, Vector3 world, float width, float height, out Vector2 screen)
    {
        return new Frame(camera, width, height).TryProject(world, out screen);
    }

    // Build one basis/focal length for all vertices in a rendered frame.
    public readonly struct Frame
    {
        private readonly Vector3 origin, forward, right, up;
        private readonly float halfWidth, halfHeight, focal;
        private readonly bool valid;
        public Frame(MumbleSnapshot camera, float width, float height)
        {
            origin = camera.CameraPosition;
            forward = Normalize(camera.CameraFront, Vector3.UnitZ);
            right = Normalize(Vector3.Cross(Vector3.UnitY, forward), Vector3.UnitX);
            up = Normalize(Vector3.Cross(forward, right), Vector3.UnitY);
            float fov = float.IsFinite(camera.Fov) && camera.Fov > 0.1f && camera.Fov < 3 ? camera.Fov : DefaultFov;
            halfWidth = width * .5f;
            halfHeight = height * .5f;
            focal = halfHeight / MathF.Tan(fov * .5f);
            valid = width > 1 && height > 1 && Finite(origin) && Finite(camera.CameraFront);
        }

        public bool Contains(Vector2 screen) => screen.X >= 0 && screen.Y >= 0 && screen.X <= halfWidth * 2 && screen.Y <= halfHeight * 2;

        public float Depth(Vector3 world) => Vector3.Dot(world - origin, forward);

        public float PlaneDistance(Vector3 world, int plane)
        {
            var relative = world - origin;
            float z = Vector3.Dot(relative, forward);
            float x = Vector3.Dot(relative, right), y = Vector3.Dot(relative, up);
            return plane switch { 0 => z - (NearClip + .001f), 1 => z * halfWidth + x * focal, 2 => z * halfWidth - x * focal, 3 => z * halfHeight - y * focal, _ => z * halfHeight + y * focal };
        }

        public bool TryProject(Vector3 world, out Vector2 screen)
        {
            screen = default;
            if (!valid || !Finite(world)) return false;
            var relative = world - origin;
            float depth = Vector3.Dot(relative, forward);
            if (depth <= NearClip) return false;
            screen = new(halfWidth + Vector3.Dot(relative, right) * focal / depth, halfHeight - Vector3.Dot(relative, up) * focal / depth);
            return float.IsFinite(screen.X) && float.IsFinite(screen.Y);
        }
    }

    private static Vector3 Normalize(Vector3 value, Vector3 fallback) => value.Length() <= 0.000001f ? fallback : Vector3.Normalize(value);

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public readonly record struct RibbonVertex(Vector3 Position, Vector2 Uv)
{
    public static RibbonVertex Midpoint(RibbonVertex a, RibbonVertex b) => new((a.Position + b.Position) * .5f, (a.Uv + b.Uv) * .5f);
}

public static class RibbonProjection
{
    // Clip in camera space before projection; carry the original world-distance
    // UVs through each intersection, including edges crossing behind the camera.
    public static int Clip(CameraProjection.Frame camera, RibbonVertex a, RibbonVertex b, RibbonVertex c, Span<RibbonVertex> output)
    {
        Span<RibbonVertex> input = stackalloc RibbonVertex[12];
        Span<RibbonVertex> scratch = stackalloc RibbonVertex[12];
        input[0] = a;
        input[1] = b;
        input[2] = c;
        int count = 3;
        for (int plane = 0; plane < 5 && count > 0; plane++)
        {
            int next = 0;
            var previous = input[count - 1];
            float previousDistance = camera.PlaneDistance(previous.Position, plane);
            for (int i = 0; i < count; i++)
            {
                var current = input[i];
                float distance = camera.PlaneDistance(current.Position, plane);
                if ((distance >= 0) != (previousDistance >= 0))
                {
                    float t = previousDistance / (previousDistance - distance);
                    var intersection = new RibbonVertex(Vector3.Lerp(previous.Position, current.Position, t), Vector2.Lerp(previous.Uv, current.Uv, t));
                    if (next == 0 || Vector3.DistanceSquared(scratch[next - 1].Position, intersection.Position) > 1e-12f) scratch[next++] = intersection;
                }
                if (distance >= 0 && (next == 0 || Vector3.DistanceSquared(scratch[next - 1].Position, current.Position) > 1e-12f)) scratch[next++] = current;
                previous = current;
                previousDistance = distance;
            }
            if (next > 1 && Vector3.DistanceSquared(scratch[0].Position, scratch[next - 1].Position) <= 1e-12f) next--;
            var swap = input;
            input = scratch;
            scratch = swap;
            count = next;
        }
        input[..count].CopyTo(output);
        return count;
    }
}
