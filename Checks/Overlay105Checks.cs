using System.Numerics;
using TacoStuffer.Models;

internal static class Overlay105Checks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static EditableTrail Create(long number, params float[][] runs)
    {
        var trail = new EditableTrail { Number = number, MapId = 15, Guid = "trail-" + number };
        foreach (var run in runs)
        {
            var segment = trail.AddSegment();
            foreach (float x in run) segment.Add(new(x, 1, 1));
        }
        return trail;
    }
    private static float[] Xs(EditableTrail trail) => trail.Segments.SelectMany(s => s.Points).Select(p => p.X).ToArray();
    public static void Run()
    {
        // Click order and endpoint choice cannot override Outliner order.
        foreach (bool reverseClicks in new[] { false, true })
        foreach (long earlyEndpoint in new long[] { 1, 3 })
        foreach (long lateEndpoint in new long[] { 1, 3 })
        {
            var early = Create(90, [1, 2, 3]); var late = Create(4, [7, 8, 9]);
            var a = new TrailPointRef(90, 1, earlyEndpoint); var b = new TrailPointRef(4, 1, lateEndpoint);
            var seam = TrailTopology.Merge([early, late], reverseClicks ? b : a, reverseClicks ? a : b);
            Check(Xs(early).SequenceEqual(new float[] { 1, 2, 3, 7, 8, 9 }) && seam.Trail == 90 && seam.Point == 3,
                "Cross-trail merge uses supplied hierarchy order rather than numeric IDs, endpoint choices or click order");
            Check(late.Segments.Count == 0, "The later trail remains as an empty identity after donating its points");
        }
        var same = Create(8, [1, 2], [7, 8]);
        same.Segments.Reverse(); // IDs deliberately disagree with displayed segment order.
        TrailTopology.Merge([same], new(8, 1, 1), new(8, 2, 2));
        Check(Xs(same).SequenceEqual(new float[] { 7, 8, 1, 2 }) && same.Segments[0].Id == 2,
            "Same-trail merge follows segment list order even when IDs and clicks suggest the opposite");

        var reverse = Create(11, [1, 2, 3, 4], [10, 11]);
        var segment = reverse.Segments[0];
        var refs = segment.Points.Select(p => p.Id).ToArray(); long next = segment.NextPoint;
        segment.ResampleSource = segment.Points.Select(p => new TrailPoint { Id = p.Id, Position = p.Position }).ToList();
        string before = EditableTrails.Serialize(reverse);
        TrailTopology.Reverse(reverse, new(11, 1, 2));
        Check(segment.Points.Select(p => p.Id).SequenceEqual(refs.Reverse()) && segment.NextPoint == next && segment.ResampleSource == null,
            "Reverse changes ordering, retains point identities/counter, and invalidates stale resampling data");
        var persisted = EditableTrails.Deserialize(EditableTrails.Serialize(reverse));
        var compiled = EditableTrails.Import(EditableTrails.Compile(reverse), 12, "compiled");
        Check(Xs(persisted).SequenceEqual(new float[] { 4, 3, 2, 1, 10, 11 }) && Xs(compiled).SequenceEqual(Xs(persisted)),
            "Reverse persists in editable JSON and compiled TRL without changing another segment");
        Check(Xs(EditableTrails.Deserialize(before)).SequenceEqual(new float[] { 1, 2, 3, 4, 10, 11 }),
            "Undo snapshot restores pre-reversal order");
        TrailTopology.Reverse(reverse, new(11, 1, 2));
        Check(segment.Points.Select(p => p.Id).SequenceEqual(refs), "Reversing twice restores exact identities and ordering");
        TrailTopology.Remove(reverse, new(11, 1, 2), true);
        Check(Xs(reverse).SequenceEqual(new float[] { 1, 3, 4, 10, 11 }), "Split retains original point flow across the new gap");

        string path = Path.Combine(Path.GetTempPath(), "toolbar105-" + Guid.NewGuid() + ".json");
        try
        {
            var preferences = new EditorPreferences { OverlayToolbarPosition = new(-1400, 240) };
            preferences.Save(path);
            Check(EditorPreferences.Load(path).Copy().OverlayToolbarPosition == new ToolbarPosition(-1400, 240),
                "Toolbar position persists, copies and supports monitors left of the primary screen");
            File.WriteAllText(path, "{}");
            Check(EditorPreferences.Load(path).OverlayToolbarPosition == null, "Older preferences retain the default toolbar placement");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("1.0.5 hierarchy direction, segment reversal, export and toolbar position checks passed.");
    }
}
