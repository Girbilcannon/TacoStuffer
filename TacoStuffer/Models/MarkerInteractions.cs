using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public enum MarkerClipboardMode { Always, OnlyWhenInteractedWith, Never }

public sealed record MarkerInteraction(string Key, uint MapId, Vector3 Position, string Category,
    string Info, float InfoRadius, string Copy, string Message, float CopyRadius, bool AutoTrigger)
{
    public const string DefaultMessage = "'{0}' copied to clipboard.";
    public static bool TryRadius(string text, out float value)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !float.IsFinite(value)) return false;
        value = Math.Max(0, value);
        return true;
    }

    public static MarkerInteraction? Read(string key, XElement element, IReadOnlyDictionary<string, XElement> categories)
    {
        if (element.Name.LocalName != "POI") return null;
        string E(string name, string fallback = "") => AttributeRules.Effective(element, name, fallback, categories);
        if (!uint.TryParse(E("MapID"), out uint map) || !float.TryParse(E("xpos"), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(E("ypos"), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(E("zpos"), NumberStyles.Float, CultureInfo.InvariantCulture, out float z) || !EditableTrails.Finite(new(x, y, z))) return null;
        string info = E("info"), copy = E("copy");
        if (info.Length == 0 && copy.Length == 0) return null;
        float copyRadius = TryRadius(E("triggerRange"), out float trigger) ? trigger : 2;
        float infoRadius = TryRadius(E("infoRange"), out float range) ? range : copyRadius;
        string auto = E("autoTrigger").Trim();
        return new(key, map, new(x, y, z), E("type"), info, infoRadius, copy, E("copy-message"), copyRadius,
            auto == "1" || auto.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    // Expand only known placeholders in the template, never braces in the payload.
    public static string Confirmation(string template, string payload)
    {
        if (template.Length == 0) template = DefaultMessage;
        var result = new StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            if (i + 1 < template.Length && template[i] == '{' && template[i + 1] == '{') { result.Append('{'); i++; }
            else if (i + 1 < template.Length && template[i] == '}' && template[i + 1] == '}') { result.Append('}'); i++; }
            else if (i + 2 < template.Length && template.AsSpan(i, 3).SequenceEqual("{0}")) { result.Append(payload); i += 2; }
            else result.Append(template[i]);
        }
        return result.ToString();
    }
}

public sealed record InteractionPreview(MarkerInteraction? Info, MarkerInteraction? Copy, MarkerInteraction? Automatic);

public sealed class MarkerInteractionSession
{
    private HashSet<string> insideCopy = [];
    private readonly HashSet<string> dismissed = [];
    private bool baseline = true;
    private uint map;
    private long? lastAutomatic;
    public void Reset() { insideCopy.Clear(); dismissed.Clear(); baseline = true; map = 0; lastAutomatic = null; }
    public void Dismiss(string key) => dismissed.Add(key);
    public void AutomaticAttempted(long now) => lastAutomatic = now;

    public InteractionPreview Update(IReadOnlyList<MarkerInteraction> markers, uint currentMap, Vector3 player, long now, bool allowAutomatic)
    {
        if (map != currentMap) { Reset(); map = currentMap; }
        var eligible = markers.Where(m => m.MapId == currentMap).Select(m => (Marker: m, Distance: Vector3.DistanceSquared(player, m.Position)))
            .Where(p => float.IsFinite(p.Distance)).OrderBy(p => p.Distance).ToList();
        var info = eligible.Where(p => p.Marker.Info.Length > 0 && p.Distance <= (double)p.Marker.InfoRadius * p.Marker.InfoRadius).ToList();
        var copy = eligible.Where(p => p.Marker.Copy.Length > 0 && p.Distance <= (double)p.Marker.CopyRadius * p.Marker.CopyRadius).ToList();
        var newInfo = info.Select(p => p.Marker.Key).ToHashSet();
        dismissed.RemoveWhere(key => !newInfo.Contains(key));
        var auto = !baseline && allowAutomatic && (!lastAutomatic.HasValue || now - lastAutomatic.Value >= 2000)
            ? copy.FirstOrDefault(p => p.Marker.AutoTrigger && !insideCopy.Contains(p.Marker.Key)).Marker : null;
        insideCopy = copy.Select(p => p.Marker.Key).ToHashSet();
        baseline = false;
        return new(info.FirstOrDefault(p => !dismissed.Contains(p.Marker.Key)).Marker, copy.FirstOrDefault().Marker, auto);
    }
}
