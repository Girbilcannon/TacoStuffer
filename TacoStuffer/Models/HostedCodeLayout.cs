namespace TacoStuffer.Models;

public static class HostedCodeLayout
{
    public static double ResizeFraction(double fraction, double horizontalDip, double dpiScale, int viewportPixels)
        => Math.Clamp(fraction - horizontalDip * dpiScale / Math.Max(1,viewportPixels), .15, .95);
    public static int DockedWidth(int viewportPixels, double fraction)
        => Math.Clamp((int)Math.Round(viewportPixels * fraction),Math.Min(240,Math.Max(1,viewportPixels)),Math.Max(1,viewportPixels));
}
