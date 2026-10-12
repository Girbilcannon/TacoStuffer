using System.Globalization;
using System.Numerics;

namespace TacoStuffer.Models;

public readonly record struct PreviewRect(float X, float Y, float Width, float Height);

public static class VisualAttributeMath
{
    public static uint Color(string text)
    {
        string hex = text.Trim().TrimStart('#');
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        return hex.Length is 6 or 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)
            ? (hex.Length == 6 ? value | 0xff000000 : value) : 0xffffffff;
    }

    public static bool Enabled(string value) => value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);

    public static Vector3? Rotation(string combined, string x, string y, string z)
    {
        if (combined.Length == 0 && x.Length == 0 && y.Length == 0 && z.Length == 0) return null;
        var parts = combined.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        float Read(string local, int index) => float.TryParse(local.Length > 0 ? local : index < parts.Length ? parts[index] : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && float.IsFinite(v) ? v : 0;
        return new(Read(x, 0), Read(y, 1), Read(z, 2));
    }

    // XML rotation axes use the marker plane's X/Y with Z as its normal.
    // Map that frame into Mumble's Y-up world; apply X, then Y, then Z.
    public static Vector3 RotateMarker(Vector3 local, Vector3 degrees)
    {
        var matrix = Matrix4x4.CreateRotationX(degrees.X * MathF.PI / 180)
            * Matrix4x4.CreateRotationY(degrees.Y * MathF.PI / 180)
            * Matrix4x4.CreateRotationZ(degrees.Z * MathF.PI / 180);
        var p = Vector3.Transform(local, matrix);
        return new(p.X, p.Z, -p.Y);
    }

    public static float AnimationOffset(double seconds, float speed) => (float)((seconds * speed) % 1);

    public static bool StickyInRange(Vector3 player, Vector3 marker, float inches) => Vector3.Distance(player, marker) <= Math.Max(0, inches) * .0254f;

    public static PreviewRect StickyRect(float screenWidth, float screenHeight, float imageWidth, float imageHeight, float x, float y, float scale)
    {
        // Use double while scaling so extreme but finite XML values cannot
        // overflow into NaN before the image is fitted to the screen.
        double w = Math.Max(1, (double)imageWidth * Math.Max(.001f, scale)), h = Math.Max(1, (double)imageHeight * Math.Max(.001f, scale));
        double fit = Math.Min(1, Math.Min(screenWidth / w, screenHeight / h));
        w *= fit;
        h *= fit;
        return new((float)Math.Clamp(screenWidth / 2d + x - w / 2, 0, Math.Max(0, screenWidth - w)),
            (float)Math.Clamp(screenHeight / 2d - y - h / 2, 0, Math.Max(0, screenHeight - h)), (float)w, (float)h);
    }
}
