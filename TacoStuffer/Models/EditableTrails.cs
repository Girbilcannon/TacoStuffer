using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed class TrailPoint
{
    public long Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector3 Position
    {
        get => new(X, Y, Z);
        set
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
        }
    }
}

public sealed class TrailSegment
{
    public long Id { get; set; }
    public long NextPoint { get; set; } = 1;
    public List<TrailPoint> Points { get; set; } = [];
    public int Smoothness { get; set; } = 9;
    public int SourceSmoothness { get; set; } = 9;
    public List<TrailPoint>? ResampleSource { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public long GeometryRevision { get; private set; }
    // An edit invalidates the retained resampling source and GPU geometry revision.
    // Keep point identities stable for selection while rebuilding derived geometry.
    public void ResetResampling()
    {
        ResampleSource = null;
        GeometryRevision++;
    }

    public void GeometryChanged() => GeometryRevision++;

    public bool Add(Vector3 position)
    {
        if (!EditableTrails.Finite(position) || position == Vector3.Zero) throw new InvalidOperationException("Trail points must be finite and cannot use TacO's zero-position segment separator.");
        if (Points.LastOrDefault()?.Position == position) return false;
        ResetResampling();
        Points.Add(new TrailPoint { Id = NextPoint++, Position = position });
        return true;
    }
}

public sealed class EditableTrail
{
    public int Version { get; set; } = 1;
    public long Number { get; set; }
    public string Guid { get; set; } = "";
    public uint MapId { get; set; }
    public long NextSegment { get; set; } = 1;
    public List<TrailSegment> Segments { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string DataPath => $"DATA/trail_{Number:0000}.trl";
    public TrailSegment AddSegment()
    {
        var s = new TrailSegment { Id = NextSegment++ };
        Segments.Add(s);
        return s;
    }
}

public sealed record TrailPointRef(long Trail, long Segment, long Point)
{
    public string Key => $"point|{Trail}|{Segment}|{Point}";
    public static TrailPointRef? Parse(string key)
    {
        var bits = key.Split('|');
        return bits.Length == 4 && bits[0] == "point" && long.TryParse(bits[1], out long t) && long.TryParse(bits[2], out long s) && long.TryParse(bits[3], out long p) ? new(t, s, p) : null;
    }
}

public sealed record TrailSegmentRef(long Trail, long Segment);

public static class EditableTrails
{
    public const string Folder = "EDITABLE_TRAILS";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    public static string Serialize(EditableTrail trail)
    {
        Validate(trail);
        return JsonSerializer.Serialize(trail, Json);
    }

    public static EditableTrail Deserialize(string source)
    {
        try
        {
            var trail = JsonSerializer.Deserialize<EditableTrail>(source, Json) ?? throw new InvalidOperationException("Empty editable trail.");
            Validate(trail);
            return trail;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid editable trail JSON.", ex);
        }
    }

    public static void Validate(EditableTrail trail)
    {
        if (trail.Version != 1 || trail.Number < 1 || trail.MapId == 0 || string.IsNullOrWhiteSpace(trail.Guid) || trail.Segments == null) throw new InvalidOperationException("Invalid editable trail header.");
        if (trail.Segments.Any(s => s.Id < 1 || s.Points == null) || trail.Segments.Select(s => s.Id).Distinct().Count() != trail.Segments.Count || trail.NextSegment <= trail.Segments.Select(s => s.Id).DefaultIfEmpty(0).Max()) throw new InvalidOperationException("Invalid segment indexes.");
        foreach (var s in trail.Segments)
        {
            if (s.Smoothness is < 1 or > 10 || s.SourceSmoothness is < 1 or > 10) throw new InvalidOperationException("Invalid segment smoothness.");
            if (s.ResampleSource is { } source && (source.Any(p => p.Id < 1 || !Finite(p.Position) || p.Position == Vector3.Zero) || source.Select(p => p.Id).Distinct().Count() != source.Count || s.NextPoint <= source.Select(p => p.Id).DefaultIfEmpty(0).Max())) throw new InvalidOperationException("Invalid segment resampling source.");
            if (s.Points.Any(p => p.Id < 1 || !Finite(p.Position) || p.Position == Vector3.Zero) || s.Points.Select(p => p.Id).Distinct().Count() != s.Points.Count || s.NextPoint <= s.Points.Select(p => p.Id).DefaultIfEmpty(0).Max()) throw new InvalidOperationException("Invalid trail point indexes/coordinates.");
        }
    }

    // Keep the original polyline for reversible density adjustments; emitted
    // TRL data always uses Points, never this source copy.
    public static void Resample(TrailSegment segment, int level)
    {
        level = Math.Clamp(level, 1, 10);
        if (segment.Points.Count < 2 || level == segment.Smoothness) return;
        var source = segment.ResampleSource ?? segment.Points;
        int sourceLevel = segment.ResampleSource == null ? segment.Smoothness : segment.SourceSmoothness;
        int factor = 1 << Math.Abs(level - sourceLevel);
        long last = source.Count - 1;
        long count = level > sourceLevel ? last * factor + 1 : last / factor + 1 + (last % factor == 0 ? 0 : 1);
        if (count > 100000) throw new InvalidOperationException("This density would exceed 100,000 editable points in one segment. Choose a lower level.");
        var changed = new List<TrailPoint>();
        long next = segment.NextPoint;
        TrailPoint Copy(TrailPoint p) => new() { Id = p.Id, Position = p.Position };
        for (int i = 0; i < source.Count; i++)
        {
            if (level <= sourceLevel)
            {
                if (i % factor == 0 || i == source.Count - 1) changed.Add(Copy(source[i]));
            }
            else
            {
                changed.Add(Copy(source[i]));
                if (i == source.Count - 1) continue;
                for (int n = 1; n < factor; n++)
                {
                    var position = Vector3.Lerp(source[i].Position, source[i + 1].Position, (float)n / factor);
                    if (!Finite(position) || position == Vector3.Zero) throw new InvalidOperationException("Interpolation crossed TacO's zero-position separator. Adjust the endpoints first.");
                    changed.Add(new TrailPoint { Id = next++, Position = position });
                }
            }
        }
        if (segment.ResampleSource == null)
        {
            segment.ResampleSource = source.Select(Copy).ToList();
            segment.SourceSmoothness = sourceLevel;
        }
        segment.Points = changed;
        segment.NextPoint = next;
        segment.Smoothness = level;
        segment.GeometryChanged();
    }

    public static int RemoveLegacyCategories(IReadOnlyList<XDocument> documents, IReadOnlyList<EditableTrail> trails)
    {
        int removed = 0;
        foreach (var trail in trails)
        {
            var objects = documents.SelectMany(d => d.Descendants().Where(e => e.Name.LocalName is "POI" or "Trail")).ToList();
            var owner = objects.FirstOrDefault(e => e.Name == "Trail" && (string?)e.Attribute("GUID") == trail.Guid);
            if (owner == null) continue;
            string path = (string?)owner.Attribute("type") ?? "";
            var candidates = documents.SelectMany(d => d.Descendants("MarkerCategory")).Where(c => CategoryOperations.PathOf(c) == path).ToList();
            if (candidates.Count != 1) continue;
            var category = candidates[0];
            if (category.Parent?.Name != "MarkerCategory" || category.Elements().Any() || category.Attributes().Count() != 2 || (string?)category.Attribute("name") != $"trail_{trail.Number:0000}" || (string?)category.Attribute("DisplayName") != $"Trail {trail.Number:0000}") continue;
            if (objects.Any(e => e != owner && ((string?)e.Attribute("type") == path || ((string?)e.Attribute("type") ?? "").StartsWith(path + ".", StringComparison.Ordinal)))) continue;
            owner.SetAttributeValue("type", CategoryOperations.PathOf(category.Parent));
            category.Remove();
            removed++;
        }
        return removed;
    }

    public static string FileName(long number) => $"trail_{number:0000}.trail.json";

    public static List<EditableTrail> Load(string projectFolder, IEnumerable<long> numbers)
    {
        var requested = numbers.ToList();
        var trails = requested.Select(n => Deserialize(File.ReadAllText(Path.Combine(projectFolder, Folder, FileName(n))))).ToList();
        if (!requested.SequenceEqual(trails.Select(t => t.Number))) throw new InvalidOperationException("Editable trail filename and header number differ.");
        if (trails.Select(t => t.Number).Distinct().Count() != trails.Count || trails.Select(t => t.Guid).Distinct().Count() != trails.Count) throw new InvalidOperationException("Duplicate editable trail identity.");
        return trails;
    }

    public static byte[] Compile(EditableTrail trail)
    {
        Validate(trail);
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write(0);
        writer.Write(trail.MapId);
        bool previous = false;
        foreach (var segment in trail.Segments.Where(s => s.Points.Count > 0))
        {
            if (previous)
            {
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(0f);
            }
            foreach (var p in segment.Points)
            {
                writer.Write(p.X);
                writer.Write(p.Y);
                writer.Write(p.Z);
            }
            previous = true;
        }
        writer.Flush();
        return bytes.ToArray();
    }

    public static EditableTrail Import(byte[] bytes, long number, string guid)
    {
        if (bytes.Length < 8 || (bytes.Length - 8) % 12 != 0) throw new InvalidOperationException("Invalid TRL byte length.");
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != 0) throw new InvalidOperationException("Unsupported TRL version.");
        var result = new EditableTrail { Number = number, Guid = guid, MapId = reader.ReadUInt32() };
        TrailSegment? segment = null;
        while (stream.Position < stream.Length)
        {
            var point = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (point == Vector3.Zero)
            {
                segment = null;
                continue;
            }
            segment ??= result.AddSegment();
            segment.Add(point);
        }
        Validate(result);
        return result;
    }

    public static Vector3 Pivot(IEnumerable<Vector3> points)
    {
        var list = points.ToList();
        return list.Count == 0 ? Vector3.Zero : list.Aggregate(Vector3.Zero, (a, b) => a + b) / list.Count;
    }

    // Rigid rotation around the average pivot, as in Deco Tools. Here axes use
    // raw Mumble Y-up meters; no decoration scale, axis swap or Euler export.
    public static Vector3 Rotate(Vector3 point, Vector3 pivot, int axis, float radians) => pivot + Vector3.Transform(point - pivot, Quaternion.CreateFromAxisAngle(axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ, radians));
}

// Downsample changed position samples, rather than render frames or idle ticks.
public sealed class TrailSampler
{
    public int Smoothness { get; set; } = 8;
    public int Interval => 1 << (10 - Math.Clamp(Smoothness, 1, 10));
    public Vector3? Latest { get; private set; }
    private int changes;
    public void Reset()
    {
        Latest = null;
        changes = 0;
    }

    public bool ShouldRecord(Vector3 position)
    {
        if (!EditableTrails.Finite(position) || position == Vector3.Zero || Latest == position) return false;
        bool first = Latest == null;
        Latest = position;
        if (first)
        {
            changes = 0;
            return true;
        }
        if (++changes < Interval) return false;
        changes = 0;
        return true;
    }
}
