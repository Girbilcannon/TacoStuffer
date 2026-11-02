using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TacoStuffer.Models;

public static class PathingBehavior
{
    public static readonly (int Id, string Label)[] Modes = [(0, "Always visible"), (1, "Until map change"), (2, "Until daily reset (UTC)"), (3, "Permanent activation"), (4, "Timed reset"), (5, "Map reset (unimplemented)"), (6, "Once per map instance"), (7, "Daily per character"), (101, "Until weekly reset (Blish HUD)")];
    public static readonly string[] Mounts = ["none", "jackal", "griffon", "springer", "skimmer", "raptor", "rollerbeetle", "warclaw", "skyscale", "skiff", "siegeturtle"];
    public static bool MountVisible(string? filter, byte mount) => filter == null || filter.Split(',').Any(s => mount < Mounts.Length && s.Trim().Equals(Mounts[mount], StringComparison.OrdinalIgnoreCase));
    public static string GuidKey(string text)
    {
        if (Guid.TryParse(text, out var guid)) return guid.ToString("D");
        try { var bytes = Convert.FromBase64String(text); return bytes.Length == 16 ? new Guid(bytes).ToString("D") : ""; }
        catch (FormatException) { return ""; }
    }
    public static DateTimeOffset NextWeekly(DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        int days = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        var reset = new DateTimeOffset(now.Date.AddDays(days).AddHours(7).AddMinutes(30), TimeSpan.Zero);
        return reset <= now ? reset.AddDays(7) : reset;
    }
}

// Authoring-preview state is isolated by project and never exported into a pack.
public sealed class PathingPreviewState
{
    public Dictionary<string, long> Timers { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> Categories { get; set; } = new(StringComparer.Ordinal);
    private readonly HashSet<string> mapHidden = [];
    private readonly HashSet<string> instanceHidden = [];
    private uint? currentMap;
    public void Observe(uint map)
    {
        if (currentMap != map) { mapHidden.Clear(); currentMap = map; }
    }
    private static string TimerKey(string guid, int mode, string character) => mode == 7 ? guid + "|character|" + character : guid;
    public bool Hidden(string guidText, int mode, bool invert, string character, uint map, uint shard, DateTimeOffset now)
    {
        string guid = PathingBehavior.GuidKey(guidText);
        if (mode <= 0 || guid.Length == 0) return false;
        Observe(map);
        // Mode 5 has no state implementation, but the reader still installs
        // its filter: inversion therefore hides it. Modes 2/3/4/101 share GUID state.
        bool hidden = mode == 1 ? mapHidden.Contains(guid) : mode == 6 ? instanceHidden.Contains($"{map}:{shard}:{guid}")
            : mode is 2 or 3 or 4 or 7 or 101 && Timers.TryGetValue(TimerKey(guid, mode, character), out long until) && until > now.ToUnixTimeMilliseconds();
        return invert ? !hidden : hidden;
    }
    public void Trigger(string guidText, int mode, double resetSeconds, string character, uint map, uint shard, DateTimeOffset now)
    {
        string guid = PathingBehavior.GuidKey(guidText); if (guid.Length == 0) return;
        Observe(map);
        if (mode == 1) { mapHidden.Add(guid); return; }
        if (mode == 6) { instanceHidden.Add($"{map}:{shard}:{guid}"); return; }
        long expiry;
        if (mode is 2 or 7) expiry = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero).ToUnixTimeMilliseconds();
        else if (mode == 3) expiry = long.MaxValue;
        else if (mode == 4) expiry = now.AddSeconds(double.IsFinite(resetSeconds) ? Math.Clamp(resetSeconds, 0, 3155760000) : 0).ToUnixTimeMilliseconds();
        else if (mode == 101) expiry = PathingBehavior.NextWeekly(now).ToUnixTimeMilliseconds();
        else return;
        Timers[TimerKey(guid, mode, character)] = expiry;
    }

    public int Reset(IEnumerable<string> targets, string character)
    {
        int changedTargets = 0;
        foreach (string guid in targets.Select(PathingBehavior.GuidKey).Where(g => g.Length > 0).Distinct(StringComparer.Ordinal))
        {
            // Reset all matching buckets, rather than stopping after the first
            // match. Preserve other characters' daily state and unrelated GUIDs.
            bool changed = mapHidden.Remove(guid);
            changed |= Timers.Remove(guid);
            changed |= Timers.Remove(TimerKey(guid, 7, character));
            changed |= instanceHidden.RemoveWhere(key => key.EndsWith(":" + guid, StringComparison.Ordinal)) > 0;
            if (changed) changedTargets++;
        }
        return changedTargets;
    }

    public int Activate(string guidText, int mode, double resetSeconds, string character, uint map, uint shard,
        DateTimeOffset now, IEnumerable<string> resetTargets)
    {
        // The activator's own behavior runs first; explicit reset targets run
        // afterward. A reset that includes itself therefore remains reusable.
        Trigger(guidText, mode, resetSeconds, character, map, shard, now);
        return Reset(resetTargets, character);
    }
    public static PathingPreviewState Load(string path)
    {
        try
        {
            var state = File.Exists(path) ? JsonSerializer.Deserialize<PathingPreviewState>(File.ReadAllText(path)) : null;
            if (state == null) return new();
            state.Timers ??= new(); state.Categories ??= new(); return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class BouncePreview
{
    private sealed record Phase(bool Focus, double Started, double From, double Last);
    private readonly Dictionary<string, Phase> phases = [];
    public void Clear() => phases.Clear();
    public double Sample(string key, bool focused, string mode, double height, double duration, double delay, double now)
    {
        duration = double.IsFinite(duration) ? Math.Clamp(duration, .01, 3600) : 1;
        height = double.IsFinite(height) ? Math.Clamp(height, -10000, 10000) : 2;
        delay = double.IsFinite(delay) ? Math.Clamp(delay, 0, 3600) : 0;
        if (!phases.TryGetValue(key, out var phase)) phase = new(focused, now, 0, 0);
        if (phase.Focus != focused) phase = new(focused, now, phase.Last, phase.Last);
        double t = Math.Max(0, now - phase.Started - (focused ? delay : 0)) / duration;
        double value;
        if (!focused)
        {
            double x = Math.Min(t, 1), bounce;
            if (x < 1 / 2.75) bounce = 7.5625 * x * x;
            else if (x < 2 / 2.75) { x -= 1.5 / 2.75; bounce = 7.5625 * x * x + .75; }
            else if (x < 2.5 / 2.75) { x -= 2.25 / 2.75; bounce = 7.5625 * x * x + .9375; }
            else { x -= 2.625 / 2.75; bounce = 7.5625 * x * x + .984375; }
            value = phase.From * (1 - bounce);
        }
        else if (mode.Equals("rise", StringComparison.OrdinalIgnoreCase))
        {
            double x = Math.Min(t, 1); value = height * (x < .5 ? 8 * x * x * x * x : 1 - Math.Pow(-2 * x + 2, 4) / 2);
        }
        else
        {
            double x = t % 2; if (x > 1) x = 2 - x;
            value = height * (x < .5 ? 2 * x * x : 1 - Math.Pow(-2 * x + 2, 2) / 2);
        }
        phases[key] = phase with { Last = value }; return value;
    }
}
