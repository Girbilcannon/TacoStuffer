namespace TacoStuffer.Models;

// Dimensions are physical pixels unless explicitly named meters. Source image
// dimensions never participate in ordinary icon or trail geometry.
public static class ScaleNormalization
{
    public const float WorldRepeatMeters = 1.016f;
    public static float WorldHalf(float focalPixels, float depthMeters, float iconSize, float user, float min, float max)
    {
        if (!float.IsFinite(depthMeters) || depthMeters <= 0) return 0;
        min = Math.Max(0, min);
        return Math.Clamp(focalPixels * Math.Max(0, iconSize) * user / depthMeters, min, Math.Max(min, max));
    }
    public static float WorldWidth(float trailScale, float user) => WorldRepeatMeters * Math.Max(0, trailScale) * user;
    private static float MapSize(float size, float compassScale, bool withZoom, int uiSize, float user)
        => !float.IsFinite(compassScale) || compassScale <= 0 ? 0 : size * (withZoom ? 1 / compassScale : 1) * MapProjection.UiScale(uiSize) * user;
    public static float MapIconHalf(float size, float compassScale, bool withZoom, int uiSize, float user)
        => compassScale > 0 && float.IsFinite(compassScale) ? Math.Clamp(MapSize(size, compassScale, withZoom, uiSize, user) / 2, 1, 256) : 0;
    public static float MapTrailWidth(float size, float compassScale, bool withZoom, int uiSize, float user, float trailScale)
        => compassScale > 0 && float.IsFinite(compassScale) ? Math.Clamp(MapSize(size, compassScale, withZoom, uiSize, user) * Math.Max(0, trailScale), 1, 100) : 0;
    public static float SolidWidth(int uiSize, float user) => Math.Max(.5f, 4 * MapProjection.UiScale(uiSize) * user);

    // Straight BGRA input; alpha weights RGB, but coverage is applied by the
    // line's alpha separately. Fully transparent artwork stays invisible.
    public static uint AverageColor(ReadOnlySpan<byte> bgra)
    {
        double a = 0, r = 0, g = 0, b = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            double weight = bgra[i + 3]; a += weight;
            b += bgra[i] * weight; g += bgra[i + 1] * weight; r += bgra[i + 2] * weight;
        }
        return a == 0 ? 0u : 0xff000000u | ((uint)Math.Round(r / a, MidpointRounding.AwayFromZero) << 16) | ((uint)Math.Round(g / a, MidpointRounding.AwayFromZero) << 8) | (uint)Math.Round(b / a, MidpointRounding.AwayFromZero);
    }
    public static uint SolidColor(uint xml, uint sample) => xml == 0xffffffffu ? sample : xml;
}
