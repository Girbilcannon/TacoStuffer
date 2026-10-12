using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record RecordedMarker(XElement Poi, long NextNumber);

public static class MarkerRecording
{
    private static readonly Regex CounterName = new(@"^marker[ _](\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static string Value(XElement e, string key) => e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

    public static long NextNumber(IEnumerable<XDocument> documents, long saved)
    {
        long next = Math.Max(1, saved);
        foreach (var attr in documents.SelectMany(d => d.Descendants()).SelectMany(e => e.Attributes()).Where(a => a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) || a.Name.LocalName.Equals("DisplayName", StringComparison.OrdinalIgnoreCase)))
        {
            var match = CounterName.Match(attr.Value);
            if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
            {
                if (number == long.MaxValue) throw new InvalidOperationException("The marker number range is exhausted.");
                next = Math.Max(next, number + 1);
            }
        }
        return next;
    }

    public static RecordedMarker Create(XDocument target, IReadOnlyList<XDocument> documents, string category, uint mapId, Vector3 position, long saved)
    {
        if (!documents.Contains(target) || target.Root?.Name != "OverlayData" || !target.Descendants("MarkerCategory").Any(c => CategoryOperations.PathOf(c) == category))
            throw new InvalidOperationException("Select a category belonging to the chosen XML file.");
        if (mapId == 0 || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new InvalidOperationException("A valid map and character position are required.");
        long number = NextNumber(documents, saved);
        if (number == long.MaxValue) throw new InvalidOperationException("The marker number range is exhausted.");
        var guids = documents.SelectMany(d => d.Descendants()).Select(e => Value(e, "GUID")).Where(s => s.Length != 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string guid;
        do
        {
            guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }
        while (guids.Contains(guid));
        string suffix = number.ToString("D5", CultureInfo.InvariantCulture);
        var poi = new XElement("POI", new XAttribute("name", "marker_" + suffix), new XAttribute("DisplayName", "Marker " + suffix),
            new XAttribute("MapID", mapId), new XAttribute("xpos", position.X.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("ypos", position.Y.ToString("R", CultureInfo.InvariantCulture)), new XAttribute("zpos", position.Z.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("type", category), new XAttribute("GUID", guid));
        return new(poi, number + 1);
    }
}
