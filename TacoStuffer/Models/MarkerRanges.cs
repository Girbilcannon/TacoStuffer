using System.Globalization;

namespace TacoStuffer.Models;

public sealed record MarkerRange(string Attribute, float Radius, string Reference = "player", float? GameUnits = null);

public static class MarkerRanges
{
    // The caller supplies effective XML values, so local overrides and category
    // inheritance follow exactly the same lookup as the marker preview.
    public static IReadOnlyList<MarkerRange> Effective(Func<string,string> value)
    {
        var result = new List<MarkerRange>();
        float Read(string name,float fallback = -1) => float.TryParse(value(name),NumberStyles.Float,CultureInfo.InvariantCulture,out float n) && float.IsFinite(n) && n >= 0 ? n : fallback;
        bool Flag(string name) => VisualAttributeMath.Enabled(value(name));
        void Add(string name,float radius,string reference = "player") { if (float.IsFinite(radius) && radius > 0) result.Add(new(name,radius,reference)); }
        float trigger = MarkerInteraction.TryRadius(value("triggerRange"),out float t) ? t : -1;
        if (trigger < 0 && (Flag("autoTrigger") || (value("behavior").Length > 0 && value("behavior") != "0") || new[] { "copy","show","hide","toggle","resetguid","cpReset","bounce" }.Any(n => value(n).Length > 0))) trigger = 2;
        Add("triggerRange",trigger);
        float info = MarkerInteraction.TryRadius(value("infoRange"),out float ir) ? ir : -1;
        if (info < 0 && value("info").Length > 0) info = trigger >= 0 ? trigger : 2;
        Add("infoRange",info);
        float cp = Read("cpRadius",Flag("isCheckpoint") || Flag("cpFinish") ? 2 : -1);
        if (float.TryParse(value("cpRadius"),NumberStyles.Float,CultureInfo.InvariantCulture,out float rawCp) && float.IsFinite(rawCp)) cp = Math.Max(.01f,rawCp);
        Add("cpRadius",cp);
        Add("stickyDistance",Read("stickyDistance",Flag("screenSticky") ? 300 : -1));
        // Fade XML uses game inches; all ring geometry uses Mumble meters.
        // Share the conversion with Opacity so diagnostic rings cannot drift from fading.
        foreach (string name in new[] { "fadeNear", "fadeFar" })
        {
            float units = Read(name);
            if (units > 0) result.Add(new(name,units*MarkerPreviewMath.MetersPerGameInch,"camera",units));
        }
        return result;
    }
}
