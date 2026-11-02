using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private bool checkpointTestActive, resetMessageControlsAdded;
    private readonly CheckpointTestSession checkpointTest = new();
    private readonly Dictionary<string, CheckpointTestNode> checkpointNodes = [];
    private readonly HashSet<string> cpResetInside = [];
    private bool cpResetBaseline = true;
    private IReadOnlyDictionary<string, int> checkpointTestVisibility = new Dictionary<string, int>();
    private string checkpointMapCharacter = "";

    private void BuildCheckpointNodes()
    {
        checkpointNodes.Clear();
        var categories = CategoryValues();
        foreach (var page in pages)
        foreach (var element in page.Xml.Descendants().Where(e => e.Name.LocalName is "POI" or "Trail"))
        {
            string E(string n, string f = "") => AttributeRules.Effective(element, n, f, categories);
            bool cp = VisualAttributeMath.Enabled(E("isCheckpoint")) && element.Name.LocalName == "POI";
            bool finish = VisualAttributeMath.Enabled(E("cpFinish"));
            if (!cp && !finish) continue;
            string guid = PathingBehavior.GuidKey(Value(element, "GUID"));
            if (guid.Length == 0 || !uint.TryParse(E("MapID"), out uint map)) continue;
            float F(string n, float fallback = 0) => float.TryParse(E(n), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && float.IsFinite(v) ? v : fallback;
            var edges = new List<(Vector3 A, Vector3 B)>();
            Vector3 position = new(F("xpos"), F("ypos"), F("zpos"));
            if (element.Name.LocalName == "Trail")
            {
                var trail = TrailFor(element) ?? RawTrailFor(element, categories);
                if (trail == null) continue;
                foreach (var segment in trail.Segments)
                for (int i = 1; i < segment.Points.Count; i++) edges.Add((segment.Points[i - 1].Position, segment.Points[i].Position));
                if (edges.Count == 0) continue;
                position = edges[0].A;
            }
            uint DefaultColor() => AttributeColor.ToRgba(VisualAttributeMath.Color(E("color", "FFFFFFFF")));
            uint Color(string name) => CheckpointAuthoring.TryRgba(E(name), out uint c) ? c : DefaultColor();
            string key = page.Name + "|" + ElementKey(element);
            checkpointNodes[key] = new(key, guid, map, position, Math.Max(.01f, F("cpRadius", 2)),
                finish ? [] : GuidResetTargets.Parse(E("nextCheck")).Keys, CheckpointAuthoring.TryCount(E("nextCheckCount", "2"), out int count) ? count : 2,
                Color("cpColor"), Color("cpPreColor"), finish, cp, edges);
        }
    }
    private void ToggleCheckpointTest()
    {
        if (checkpointTestActive) { StopCheckpointTest(); return; }
        if (!TrySyncXml() || !mumbleSession.IsPlayable || gameFrame == null || overlay?.IsRenderingActive != true)
        { Notice("Checkpoint test", "Open the overlay on a playable map before starting a checkpoint test."); return; }
        StopTrailInsertTool();
        BuildCheckpointNodes();
        var nodes = checkpointNodes.Values.Where(n => n.Map == gameFrame.MapId).ToList();
        if (nodes.GroupBy(n => n.Guid).Any(g => g.Count() > 1))
        { Notice("Duplicate checkpoint GUID", "Each checkpoint or finish node needs a unique GUID before it can be tested."); return; }
        string selectedKey = selected?.DocumentName + "|" + selected?.Key;
        var root = nodes.FirstOrDefault(n => n.Key == selectedKey);
        if (root == null)
        {
            var incoming = nodes.SelectMany(n => n.Next).ToHashSet(StringComparer.Ordinal);
            var roots = nodes.Where(n => n.Checkpoint && !incoming.Contains(n.Guid)).ToList();
            if (roots.Count == 1) root = roots[0];
        }
        if (root == null) { Notice("Choose a starting checkpoint", "Select the first checkpoint in Elements, then click Test checkpoints."); return; }
        checkpointTest.Start(nodes, root.Guid); advancedBaseline = true; insideAdvanced.Clear(); interactionSession.Reset(); checkpointTestActive = true; cpResetBaseline = true; cpResetInside.Clear();
        checkpointMapCharacter = gameFrame.MapId + ":" + gameFrame.CharacterName;
        overlay.SetCheckpointTest(true); worldMarkers?.SetCheckpointTestIndicator(true); RefreshAttributeAlerts(); ApplyPreviewVisibility();
    }
    private void StopCheckpointTest()
    {
        checkpointTestActive = false; cpResetInside.Clear(); cpResetBaseline = true; advancedBaseline = true; insideAdvanced.Clear(); interactionSession.Reset();
        overlay?.SetCheckpointTest(false); worldMarkers?.SetCheckpointTestIndicator(false); worldMarkers?.ShowResetNotification(""); RefreshAttributeAlerts(); ApplyPreviewVisibility();
    }
    private void UpdateCheckpointTest()
    {
        if (!checkpointTestActive) return;
        if (gameFrame == null || !CanInteractInWorld()) { checkpointTest.Pause(); cpResetBaseline = true; return; }
        if (checkpointMapCharacter != gameFrame.MapId + ":" + gameFrame.CharacterName) { StopCheckpointTest(); return; }
        var inside = previewRules.Values.Where(r => r.Map == gameFrame.MapId && r.Get("cpReset").Length > 0
            && PreviewCategoryVisible(r.Category) && AdvancedVisible(r.Key)
            && Vector3.DistanceSquared(r.Position, gameFrame.AvatarPosition) <= Math.Pow(Math.Max(0, r.Number("triggerRange", 2)), 2)).ToList();
        bool reset = false;
        if (!cpResetBaseline)
            foreach (var rule in inside.Where(r => r.Flag("autoTrigger") && !cpResetInside.Contains(r.Key))) reset |= ApplyCheckpointReset(rule);
        cpResetInside.Clear(); foreach (var rule in inside) cpResetInside.Add(rule.Key); cpResetBaseline = false;
        if (reset) { ApplyPreviewVisibility(); return; }
        string? hit = checkpointTest.Update(gameFrame.AvatarPosition, gameFrame.MapId, n =>
            PreviewCategoryVisible(previewRules.TryGetValue(n.Key, out var rule) ? rule.Category : "") && AdvancedVisible(n.Key));
        if (hit != null)
        {
            StatusText.Text = checkpointTest.Finished ? "Checkpoint test finished. Cancel the test to return to editing." : "Checkpoint reached.";
            worldMarkers?.SetCheckpointTestIndicator(true, checkpointTest.Finished); ApplyPreviewVisibility();
        }
    }
    private bool TryCheckpointResetManual()
    {
        if (!checkpointTestActive || gameFrame == null || !CanInteractInWorld()) return false;
        var rule = previewRules.Values.Where(r => r.Map == gameFrame.MapId && r.Get("cpReset").Length > 0 && PreviewCategoryVisible(r.Category) && AdvancedVisible(r.Key)
            && (r.Number("triggerRange", -1) >= 0 || r.Flag("autoTrigger")))
            .Where(r => Vector3.DistanceSquared(r.Position, gameFrame.AvatarPosition) <= Math.Pow(Math.Max(0, r.Number("triggerRange", 2)), 2))
            .OrderBy(r => Vector3.DistanceSquared(r.Position, gameFrame.AvatarPosition)).FirstOrDefault();
        if (rule == null || !ApplyCheckpointReset(rule)) return false;
        ApplyPreviewVisibility(); return true;
    }
    private bool ApplyCheckpointReset(PreviewRule rule)
    {
        var targets = GuidResetTargets.Parse(rule.Get("cpReset"));
        if (targets.Invalid.Count != 0 || targets.Keys.Count != 1 || !checkpointTest.Nodes.ContainsKey(targets.Keys[0])) return false;
        checkpointTest.Reset(targets.Keys[0]); worldMarkers?.SetCheckpointTestIndicator(true);
        ShowResetMessage(rule.Get("resetMessage")); return true;
    }
    private WorldMarker CheckpointMarkerView(WorldMarker marker, IReadOnlySet<string> selectedKeys)
    {
        if (!checkpointNodes.TryGetValue(marker.Key, out var node)) return marker;
        uint rgba;
        if (!checkpointTestActive) rgba = node.Checkpoint ? selectedKeys.Contains(marker.Key) ? node.Color : node.PreColor : AttributeColor.ToRgba(ToArgb(marker.Style.Color));
        else
        {
            if (!checkpointTestVisibility.TryGetValue(node.Guid, out int depth)) return marker with { Style = marker.Style with { WorldVisible = false, MapVisible = false, MinimapVisible = false } };
            rgba = depth == 0 ? node.Color : DimCheckpointColor(node.PreColor, depth);
        }
        return marker with { Style = marker.Style with { Color = FromArgb(AttributeColor.ToArgb(rgba)) } };
    }
    private static uint DimCheckpointColor(uint rgba, int depth) => rgba & 0xFFFFFF00 | (uint)Math.Clamp((int)((rgba & 255) * Math.Pow(.75, depth)), 0, 255);
    private static uint ToArgb(Color color) => (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
    private static Color FromArgb(uint color) => Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    private void ShowResetMessage(string message)
    {
        worldMarkers?.ShowResetNotification(string.IsNullOrWhiteSpace(message) ? "" : message);
    }
    private void AddResetMessageControls(System.Xml.Linq.XElement element)
    {
        if (resetMessageControlsAdded) return; resetMessageControlsAdded = true;
        string value = EffectiveValue(element, "resetMessage", "");
        var enabled = new CheckBox { Content = "Reset Message", IsChecked = !string.IsNullOrWhiteSpace(value), Margin = new Thickness(0, 8, 0, 0) };
        enabled.Click += (_, _) => ChangeAttribute("resetMessage", enabled.IsChecked == true ? "Race reset." : "");
        AttributesHost.Children.Add(enabled);
        if (enabled.IsChecked == true) Field("resetMessage", value, text => ChangeAttribute("resetMessage", text));
    }
    private bool ColorAttributeField(System.Xml.Linq.XElement element, AttributeMember member)
    {
        if (!AttributeColor.IsColor(member.Name, member.Type)) return false;
        string value = Value(element, member.Name);
        bool rgba = member.Type.Equals("rgba", StringComparison.OrdinalIgnoreCase) || member.Name is "cpColor" or "cpPreColor";
        Field(member.Name, value, text => ChangeAttribute(member.Name, text));
        AttributeColor.Parse(value, rgba, out uint argb);
        double opacity = (argb >> 24) / 255.0;
        double brightness = ((argb >> 16 & 255) + (argb >> 8 & 255) + (argb & 255)) * opacity + 96 * (1 - opacity);
        var button = new Button { Content = "Choose color…", Background = new SolidColorBrush(FromArgb(argb)), Foreground = brightness > 400 ? Brushes.Black : Brushes.White };
        button.Click += (_, _) => { var dialog = new AttributeColorPickerWindow(value, rgba) { Owner = this }; if (dialog.ShowDialog() == true) ChangeAttribute(member.Name, dialog.Value); };
        AttributesHost.Children.Add(button); return true;
    }
}
