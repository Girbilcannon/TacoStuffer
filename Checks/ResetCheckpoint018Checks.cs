using System.IO.Compression;
using System.Xml.Linq;
using TacoStuffer.Models;

internal static class ResetCheckpoint018Checks
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run()
    {
        string A = Convert.ToBase64String(Guid.NewGuid().ToByteArray()), B = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        string C = Convert.ToBase64String(Guid.NewGuid().ToByteArray()), D = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        string E = Convert.ToBase64String(Guid.NewGuid().ToByteArray()), F = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var parsed = GuidResetTargets.Parse(" " + A + ",\n" + B + "," + new Guid(Convert.FromBase64String(A)).ToString() + ",bad-guid,,");
        Check(parsed.Values.SequenceEqual(new[] { A, B }) && parsed.Invalid.SequenceEqual(new[] { "bad-guid" }), "CSV/newline input validates and deduplicates equivalent GUIDs without changing spelling");
        Check(GuidResetTargets.Parse("").Values.Count == 0, "Empty reset list is a no-op, never reset-all");

        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var state = new PathingPreviewState();
        foreach (int mode in new[] { 1, 3, 6, 7 }) state.Trigger(A, mode, 0, "Alice", 23, 10, now);
        state.Trigger(A, 6, 0, "Alice", 23, 11, now);
        state.Trigger(A, 7, 0, "Bob", 23, 10, now);
        state.Trigger(B, 2, 0, "Alice", 23, 10, now);
        state.Trigger(C, 4, 60, "Alice", 23, 10, now);
        state.Categories["race.later"] = true;
        int changed = state.Activate(D, 0, 0, "Alice", 23, 10, now, new[] { A, B, A });
        Check(changed == 2, "Standalone reset activation clears multiple distinct GUIDs");
        foreach (int mode in new[] { 1, 3, 6, 7 })
            Check(!state.Hidden(A, mode, false, "Alice", 23, 10, now), "Reset clears matching state bucket " + mode);
        Check(!state.Hidden(A, 6, false, "Alice", 23, 11, now), "Reset clears all visited instance buckets for the target");
        Check(state.Hidden(A, 7, false, "Bob", 23, 10, now) && state.Hidden(C, 4, false, "Alice", 23, 10, now)
            && state.Categories["race.later"], "Other character/unlisted GUID/category state is preserved");
        Check(state.Hidden(A, 1, true, "Alice", 23, 10, now), "Reset returns inverted markers to their initial hidden state");
        Check(state.Reset(["invalid", D], "Alice") == 0, "Unknown/invalid IDs safely do nothing");
        state.Activate(D, 3, 0, "Alice", 23, 10, now, [D]);
        Check(!state.Hidden(D, 3, false, "Alice", 23, 10, now), "Reset effects run after the activator's own behavior");
        string folder = Path.Combine(Path.GetTempPath(), "Reset018_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "state.json"); state.Save(file); var loaded = PathingPreviewState.Load(file);
            Check(!loaded.Hidden(A, 3, false, "Alice", 23, 10, now) && !loaded.Hidden(B, 2, false, "Alice", 23, 10, now)
                && loaded.Hidden(A, 7, false, "Bob", 23, 10, now), "Reset deletions persist across state reloads without erasing other characters");
            var source = XDocument.Parse($"<OverlayData><MarkerCategory name='race'/><POIs><POI GUID='{A}' type='race' MapID='23' xpos='1' ypos='2' zpos='3' isCheckpoint='true' cpColor='FF000080' cpPreColor='80808040' nextCheckCount='2' nextCheck='{B},{C}' cpRadius='3'/><POI GUID='{D}' type='race' resetguid='{A},{B},{C}' cpReset='{A}' isCheckpoint='false' triggerRange='3' autoTrigger='false'/></POIs></OverlayData>");
            string output = Path.Combine(folder, "test.taco"); TestPackBuilder.Build(output, [("race.xml", source.ToString())], null);
            using var zip = ZipFile.OpenRead(output); using var stream = zip.GetEntry("race.xml")!.Open();
            Check(XNode.DeepEquals(source, XDocument.Load(stream)), "Export preserves reset list, custom checkpoint fields, RGBA bytes and original positions");
        }
        finally { Directory.Delete(folder, true); }

        string Key(string value) => PathingBehavior.GuidKey(value);
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            [Key(A)] = new[] { B, C }, [Key(B)] = new[] { D }, [Key(C)] = new[] { E },
            [Key(D)] = new[] { F }, [Key(E)] = new[] { F }, [Key(F)] = new[] { A }
        };
        var visible = CheckpointAuthoring.LookAhead(A, 2, graph);
        Check(visible.Count == 4 && visible[Key(B)] == 1 && visible[Key(C)] == 1 && visible[Key(D)] == 2 && visible[Key(E)] == 2,
            "Lookahead budget applies to both fork paths, not a shared total");
        Check(CheckpointAuthoring.Reachable(A, graph).Count == 6 && CheckpointAuthoring.LookAhead(A, 0, graph).Count == 0,
            "Graph traversal terminates at cycles and supports zero upcoming nodes");
        Check(CheckpointAuthoring.LookAhead(A, 20, graph).Count == 5, "Rejoins and cycles do not duplicate root or repeated nodes");
        Check(CheckpointAuthoring.TryRgba("FF000080", out uint rgba) && (rgba & 255) == 128
            && !CheckpointAuthoring.TryRgba("#FF000080", out _) && !CheckpointAuthoring.TryCount("-1", out _), "Custom colors use trailing alpha; count is nonnegative");
        var catalog = new AttributeCatalog(); catalog.Reload(null);
        var checkpoint = catalog.Groups.Single(g => g.Key == "checkpoint");
        Check(checkpoint.Members.Select(m => m.Name).SequenceEqual(new[] { "isCheckpoint", "cpColor", "cpPreColor", "nextCheckCount", "nextCheck", "cpRadius", "cpReset" })
            && !checkpoint.Applies(new XElement("Trail")), "Checkpoint group includes the distinct one-root cpReset action");
        var resetGroup = catalog.Groups.Single(g => g.Key == "resetguid");
        Check(resetGroup.Applies(new XElement("POI")) && !resetGroup.Applies(new XElement("Trail")), "Reset GUID applies to marker actions, not trails");
        var marker = new XElement("POI", new XAttribute("resetguid", A), new XAttribute("triggerRange", "3"), new XAttribute("autoTrigger", "true"));
        var context = new AttributeRequirementContext(true, true, true, true);
        Check(AttributeRequirements.Evaluate(marker, resetGroup, new Dictionary<string, XElement>(), context).Count == 0
            && AttributeRequirements.Evaluate(marker, catalog.Groups.Single(g => g.Key == "trigger"), new Dictionary<string, XElement>(), context).Count == 0,
            "Reset GUID is an activation consumer and satisfies Trigger orphan checks");
        var resetRace = new XElement("POI", new XAttribute("isCheckpoint", "false"), new XAttribute("cpReset", A));
        Check(AttributeRequirements.Evaluate(resetRace, checkpoint, new Dictionary<string, XElement>(), context).Any(a => a.Message.Contains("Trigger or Auto Trigger")), "cpReset requires an activation route");
        resetRace.SetAttributeValue("autoTrigger", "true");
        Check(!AttributeRequirements.Evaluate(resetRace, checkpoint, new Dictionary<string, XElement>(), context).Any(a => a.Message.Contains("Trigger or Auto Trigger")), "Automatic activation satisfies cpReset condition");
        resetRace.Attribute("isCheckpoint")!.Remove();
        Check(!AttributeRequirements.Evaluate(resetRace, checkpoint, new Dictionary<string, XElement>(), context).Any(a => a.Message.Contains("Trigger or Auto Trigger") || a.Message.Contains("checkpoint GUID")), "A dedicated cpReset marker needs no isCheckpoint flag");
        resetRace.SetAttributeValue("cpReset", A + "," + B);
        Check(AttributeRequirements.Evaluate(resetRace, checkpoint, new Dictionary<string, XElement>(), context).Any(a => a.Message.Contains("one first-checkpoint")), "cpReset is single-root, unlike resetguid");
        marker.SetAttributeValue("resetguid", "bad");
        Check(AttributeRequirements.Evaluate(marker, resetGroup, new Dictionary<string, XElement>(), context).Any(a => a.Message.Contains("valid GUIDs")), "Invalid reset lists produce a heading alert");
    }
}
