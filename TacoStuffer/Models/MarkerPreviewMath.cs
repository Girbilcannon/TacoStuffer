using System.Numerics;

namespace TacoStuffer.Models;

public static class MarkerPreviewMath
{
    public static float Opacity(float alpha, float distanceMeters, float near, float far)
    {
        float result = Math.Clamp(alpha, 0, 1);
        if (near < 0 || far < near) return result;
        float distance = distanceMeters / 0.0254f; // TacO fade distances are game inches.
        return result * (near == far ? (distance <= near ? 1 : 0) : Math.Clamp((far - distance) / (far - near), 0, 1));
    }

    // DecoTools' frozen screen direction, adapted to raw Mumble coordinates.
    public static Vector3 AxisDelta(Vector2 mouseDelta, Vector2 direction, float unitsPerPixel, int axis) =>
    (axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ) * Vector2.Dot(mouseDelta, direction) * unitsPerPixel;

    public static Vector3 PlaneDelta(Vector2 mouseDelta, Vector3 right, Vector3 up, float unitsPerPixel) =>
    (right * mouseDelta.X - up * mouseDelta.Y) * unitsPerPixel;
}
