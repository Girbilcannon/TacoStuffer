using System.IO;
using System.Text.Json;
using System.Numerics;

namespace TacoStuffer.Models;

public sealed record EditorAction(string Id, string Label);

public enum TrailSelectionMode { Trail, TrailSegment, NearestPoint }

public sealed record KeyChord(int VirtualKey, int Modifiers, string KeyName)
{
    public string Display => ((Modifiers & 2) != 0 ? "Ctrl + " : "") + ((Modifiers & 4) != 0 ? "Shift + " : "") + ((Modifiers & 1) != 0 ? "Alt + " : "") + KeyName;
}

public sealed class EditorPreferences
{
    public const float DefaultMarkerNameDistance = 3500;
    public const float MaxMarkerNameDistance = 5000;
    public bool ShowMarkerNames { get; set; } = true;
    public bool OnlyShowForSelected { get; set; } = true;
    public float MarkerNameDistanceThreshold { get; set; } = DefaultMarkerNameDistance;
    public TrailSelectionMode TrailOverlaySelection { get; set; } = TrailSelectionMode.Trail;
    public string GameExecutablePath { get; set; } = "";
    public float MarkerGameScale { get; set; } = 1;
    public float MarkerMapScale { get; set; } = 1;
    public float MarkerMinimapScale { get; set; } = 1;
    public float TrailGameWidth { get; set; } = 1;
    public float TrailMapWidth { get; set; } = 1;
    public float TrailMinimapWidth { get; set; } = 1;
    public static float ValidMultiplier(float value) => float.IsFinite(value) ? Math.Clamp(value, .25f, 4f) : 1;
    public static float ValidNameDistance(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, MaxMarkerNameDistance) : DefaultMarkerNameDistance;

    public bool ShouldShowMarkerName(bool selected, Vector3 player, Vector3 marker)
    {
        if (!ShowMarkerNames || (OnlyShowForSelected && !selected)) return false;
        float distance = Vector3.DistanceSquared(player, marker);
        float threshold = ValidNameDistance(MarkerNameDistanceThreshold);
        return float.IsFinite(distance) && distance <= threshold * threshold;
    }

    public void NormalizeVisuals()
    {
        MarkerNameDistanceThreshold = ValidNameDistance(MarkerNameDistanceThreshold);
        if (!Enum.IsDefined(TrailOverlaySelection)) TrailOverlaySelection = TrailSelectionMode.Trail;
        MarkerGameScale = ValidMultiplier(MarkerGameScale);
        MarkerMapScale = ValidMultiplier(MarkerMapScale);
        MarkerMinimapScale = ValidMultiplier(MarkerMinimapScale);
        TrailGameWidth = ValidMultiplier(TrailGameWidth);
        TrailMapWidth = ValidMultiplier(TrailMapWidth);
        TrailMinimapWidth = ValidMultiplier(TrailMinimapWidth);
    }

    public void ResetDefaults()
    {
        ShowMarkerNames = OnlyShowForSelected = true;
        MarkerNameDistanceThreshold = DefaultMarkerNameDistance;
        TrailOverlaySelection = TrailSelectionMode.Trail;
        Keybinds.Clear();
        MarkerGameScale = 1;
        MarkerMapScale = 1;
        MarkerMinimapScale = 1;
        TrailGameWidth = 1;
        TrailMapWidth = 1;
        TrailMinimapWidth = 1;
    }

    public Dictionary<string, KeyChord> Keybinds { get; set; } = [];
    public static readonly IReadOnlyList<EditorAction> Actions = [
    new("category", "Add Category"), new("marker", "Record New Marker"), new("reposition", "Reposition Marker (To Character)"),
    new("trail", "Create New Trail File"), new("start", "Start recording Trail"), new("pause", "Pause Trail Recording"),
    new("continue", "Continue Trail Recording"), new("segment", "Start Recording New Trail Segment"), new("stop", "Stop Recording Trail")];
    public static bool Reserved(KeyChord chord) => chord.Modifiers == 2 && chord.VirtualKey is 78 or 79 or 83 or 90 or 70
        || chord.Modifiers == 6 && chord.VirtualKey == 90
        || chord.VirtualKey is 27 or 46 or 113;

    public static bool Valid(KeyChord chord) => chord.VirtualKey is >= 8 and <= 254
        && chord.VirtualKey is not (16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165)
        && chord.Modifiers is >= 0 and <= 7 && !string.IsNullOrWhiteSpace(chord.KeyName);

    public string? Conflict(string action, KeyChord chord)
    {
        if (!Valid(chord)) return "Choose a key with optional Ctrl, Shift and Alt modifiers.";
        if (Reserved(chord)) return "That shortcut is reserved by the editor.";
        var other = Keybinds.FirstOrDefault(p => p.Key != action && p.Value.VirtualKey == chord.VirtualKey && p.Value.Modifiers == chord.Modifiers);
        return other.Key == null ? null : "Already assigned to " + (Actions.FirstOrDefault(a => a.Id == other.Key)?.Label ?? other.Key) + ".";
    }

    public EditorPreferences Copy() => new()
    {
        GameExecutablePath = GameExecutablePath, ShowMarkerNames = ShowMarkerNames, OnlyShowForSelected = OnlyShowForSelected,
        MarkerNameDistanceThreshold = MarkerNameDistanceThreshold, TrailOverlaySelection = TrailOverlaySelection,
        Keybinds = new(Keybinds), MarkerGameScale = MarkerGameScale, MarkerMapScale = MarkerMapScale,
        MarkerMinimapScale = MarkerMinimapScale, TrailGameWidth = TrailGameWidth, TrailMapWidth = TrailMapWidth, TrailMinimapWidth = TrailMinimapWidth
    };

    public static EditorPreferences Load(string file)
    {
        if (!File.Exists(file)) return new();
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        // Preserve the original lower-case, boolean-only preferences file.
        var result = JsonSerializer.Deserialize<EditorPreferences>(doc.RootElement.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        // Retain a saved choice from the old checkbox. Fresh/reset preferences enable both new options.
        if (!doc.RootElement.EnumerateObject().Any(p => p.Name.Equals("ShowMarkerNames", StringComparison.OrdinalIgnoreCase)))
        {
            var old = doc.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals("ShowSelectedMarkerNames", StringComparison.OrdinalIgnoreCase));
            if (old.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) result.ShowMarkerNames = old.Value.GetBoolean();
        }
        result.GameExecutablePath ??= "";
        result.Keybinds ??= [];
        var clean = new Dictionary<string, KeyChord>();
        foreach (var action in Actions)
        if (result.Keybinds.TryGetValue(action.Id, out var key) && key != null && Valid(key) && !Reserved(key)
            && !clean.Values.Any(k => k.VirtualKey == key.VirtualKey && k.Modifiers == key.Modifiers)) clean[action.Id] = key;
        result.Keybinds = clean;
        result.NormalizeVisuals();
        return result;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        string temp = file + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, file, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
