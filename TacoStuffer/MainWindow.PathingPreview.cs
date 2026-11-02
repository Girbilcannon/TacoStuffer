using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private sealed record PreviewRule(string Key, string Guid, string Category, uint Map, Vector3 Position, Dictionary<string, string> Values)
    {
        public string Get(string key, string fallback = "") => Values.GetValueOrDefault(key, fallback);
        public double Number(string key, double fallback = 0) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : fallback;
        public bool Flag(string key) => VisualAttributeMath.Enabled(Get(key, "false"));
        public IReadOnlyList<string> ResetTargets { get; init; } = [];
        public bool Action => ResetTargets.Count > 0 || Number("behavior") is not (0 or 5) || Get("show").Length > 0 || Get("hide").Length > 0 || Get("toggleCategory", Get("toggle")).Length > 0;
    }
    private readonly Dictionary<string, PreviewRule> previewRules = [];
    private readonly HashSet<string> insideAdvanced = [];
    private bool advancedBaseline = true;
    private string previewStateProject = "", previewStateFile = "";
    private PathingPreviewState pathingState = new();
    private string advancedVisibility = "";
    private long lastAdvancedEvaluation;
    private Dictionary<string, string> CategoryDisplayNames() => CategoryValues().ToDictionary(
        pair => pair.Key, pair => Value(pair.Value, "DisplayName", CategoryName(pair.Value)), StringComparer.Ordinal);
    private void EnsurePathingState()
    {
        if (previewStateProject == projectFile) return;
        previewStateProject = projectFile ?? "";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectFile ?? ".").ToUpperInvariant())));
        previewStateFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TacoStuffer", "PreviewState", hash + ".json");
        pathingState = PathingPreviewState.Load(previewStateFile);
        insideAdvanced.Clear(); advancedBaseline = true; advancedVisibility = "";
    }
    private void SavePathingState()
    {
        try { pathingState.Save(previewStateFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = "Preview state could not be saved: " + ex.Message; }
    }
    private void BuildPathingPreview()
    {
        if (checkpointTestActive) StopCheckpointTest();
        EnsurePathingState(); previewRules.Clear();
        var categories = CategoryValues();
        string[] fields = ["mount", "behavior", "invertBehavior", "resetLength", "triggerRange", "autoTrigger", "show", "hide", "toggle", "toggleCategory", "resetguid", "cpReset", "resetMessage"];
        foreach (var page in pages)
            foreach (var element in page.Xml.Descendants().Where(e => e.Name.LocalName is "POI" or "Trail"))
            {
                string E(string n, string f = "") => AttributeRules.Effective(element, n, f, categories);
                float F(string n) => float.TryParse(E(n), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && float.IsFinite(v) ? v : 0;
                _ = uint.TryParse(E("MapID"), out uint map);
                var values = fields.Select(n => (Name: n, Value: E(n, "\0"))).Where(p => p.Value != "\0").ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
                if (element.Name.LocalName == "Trail") values["behavior"] = "0";
                string key = page.Name + "|" + ElementKey(element);
                previewRules[key] = new(key, Value(element, "GUID"), Value(element, "type"), map, new(F("xpos"), F("ypos"), F("zpos")), values)
                {
                    ResetTargets = element.Name.LocalName == "POI" ? GuidResetTargets.Parse(E("resetguid")).Values : []
                };
            }
        insideAdvanced.Clear(); advancedBaseline = true; advancedVisibility = "";
    }
    private bool AdvancedVisible(string key)
    {
        if (gameFrame == null || !previewRules.TryGetValue(key, out var rule)) return true;
        if (!PathingBehavior.MountVisible(rule.Values.TryGetValue("mount", out var mount) ? mount : null, gameFrame.MountIndex)) return false;
        return checkpointTestActive && checkpointNodes.ContainsKey(key) || !interactionTestMode || !pathingState.Hidden(rule.Guid, (int)rule.Number("behavior"), rule.Flag("invertBehavior"), gameFrame.CharacterName, gameFrame.MapId, gameFrame.ShardId, DateTimeOffset.UtcNow);
    }
    private void InitializePreviewCategories()
    {
        EnsurePathingState();
        foreach (var pair in CategoryValues())
        {
            bool enabled = pathingState.Categories.TryGetValue(pair.Key, out bool saved) ? saved : VisualAttributeMath.Enabled(Value(pair.Value, "defaultToggle", "true"));
            if (enabled) hiddenPreviewCategories.Remove(pair.Key); else hiddenPreviewCategories.Add(pair.Key);
        }
    }
    private bool CategoryMenuHidden(string path)
    {
        var categories = CategoryValues();
        while (path.Length > 0)
        {
            if (categories.TryGetValue(path, out var category) && VisualAttributeMath.Enabled(Value(category, "isHidden", "false"))) return true;
            int dot = path.LastIndexOf('.'); path = dot < 0 ? "" : path[..dot];
        }
        return false;
    }
    private void SaveCategoryPreview(string path, bool enabled)
    {
        EnsurePathingState(); pathingState.Categories[path] = enabled; SavePathingState();
    }
    private void CategoryAction(string path, bool? enabled)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        bool active = enabled ?? hiddenPreviewCategories.Contains(path);
        if (active) hiddenPreviewCategories.Remove(path); else hiddenPreviewCategories.Add(path);
        pathingState.Categories[path] = active;
    }
    private void TriggerRule(PreviewRule rule)
    {
        if (gameFrame == null) return;
        string toggle = rule.Get("toggleCategory", rule.Get("toggle"));
        CategoryAction(toggle, null); CategoryAction(rule.Get("show"), true); CategoryAction(rule.Get("hide"), false);
        pathingState.Activate(rule.Guid, (int)rule.Number("behavior"), rule.Number("resetLength"), gameFrame.CharacterName, gameFrame.MapId, gameFrame.ShardId, DateTimeOffset.UtcNow, rule.ResetTargets);
        SavePathingState(); RebuildQuickMenu(); ApplyPreviewVisibility();
        if (rule.ResetTargets.Count > 0)
        {
            StatusText.Text = $"Reset applied to {rule.ResetTargets.Count} GUID(s).";
            ShowResetMessage(rule.Get("resetMessage"));
        }
    }
    private void TriggerNearbyAdvanced()
    {
        if (TryCheckpointResetManual()) return;
        if (!interactionTestMode || gameFrame == null || !CanInteractInWorld()) return;
        var nearest = previewRules.Values.Where(r => r.Action && !(checkpointTestActive && (checkpointNodes.ContainsKey(r.Key) || r.Get("cpReset").Length > 0)) && r.Map == gameFrame.MapId && PreviewCategoryVisible(r.Category) && AdvancedVisible(r.Key))
            .Select(r => (Rule: r, Distance: Vector3.DistanceSquared(r.Position, gameFrame.AvatarPosition)))
            .Where(p => p.Distance <= Math.Pow(Math.Max(0, p.Rule.Number("triggerRange", 2)), 2)).OrderBy(p => p.Distance).FirstOrDefault();
        if (nearest.Rule != null) TriggerRule(nearest.Rule);
    }
    private void UpdateAdvancedPreview(MumbleSnapshot? frame, bool allowAutomatic)
    {
        if (frame == null) return;
        long tick = Environment.TickCount64;
        if (!allowAutomatic && tick - lastAdvancedEvaluation < 100) return;
        lastAdvancedEvaluation = tick;
        EnsurePathingState(); pathingState.Observe(frame.MapId);
        if (allowAutomatic)
        {
            var inside = previewRules.Values.Where(r => r.Action && !(checkpointTestActive && (checkpointNodes.ContainsKey(r.Key) || r.Get("cpReset").Length > 0)) && r.Map == frame.MapId && PreviewCategoryVisible(r.Category) && AdvancedVisible(r.Key)
                && Vector3.DistanceSquared(r.Position, frame.AvatarPosition) <= Math.Pow(Math.Max(0, r.Number("triggerRange", 2)), 2)).ToList();
            if (!advancedBaseline && interactionTestMode && CanInteractInWorld())
                foreach (var rule in inside.Where(r => r.Flag("autoTrigger") && !insideAdvanced.Contains(r.Key)))
                    if (PreviewCategoryVisible(rule.Category) && AdvancedVisible(rule.Key)) TriggerRule(rule);
            insideAdvanced.Clear(); foreach (var rule in inside) insideAdvanced.Add(rule.Key); advancedBaseline = false;
        }
        // Update visibility only when its result changes; preserve GPU meshes/icons.
        string signature = frame.MountIndex + ":" + string.Concat(previewRules.Keys.Select(key => AdvancedVisible(key) ? '1' : '0'));
        if (signature == advancedVisibility) return;
        advancedVisibility = signature; ApplyPreviewVisibility();
    }
    private bool AdvancedAttributeField(XElement element, AttributeMember member)
    {
        if (member.Type is "guid-list" or "guid-single")
        {
            Field(member.Name, Value(element, member.Name), value => ChangeAttribute(member.Name, value));
            var choose = new Button { Content = member.Name == "resetguid" ? "Choose markers…" : "Choose checkpoints…", Margin = new Thickness(0, 6, 0, 0) };
            choose.Click += (_, _) => ChooseResetTargets(element, member.Name);
            AttributesHost.Children.Add(choose);
            return true;
        }
        if (member.Type == "behavior")
        {
            string current = Value(element, member.Name, "0");
            var choices = PathingBehavior.Modes.Select(m => new PathChoice(m.Id.ToString(CultureInfo.InvariantCulture), m.Id + " · " + m.Label)).ToList();
            if (!choices.Any(c => c.Value == current)) choices.Add(new(current, "Unknown behavior (" + current + ")"));
            var combo = PathChoicePresentation.Combo(choices, current);
            combo.SelectionChanged += (_, _) => { if (!rebuildingAttributes && combo.SelectedValue is string value && value != current) ChangeAttribute(member.Name, value); };
            AttributesHost.Children.Add(combo); return true;
        }
        if (member.Type == "category")
        {
            string current = Value(element, member.Name);
            AttributesHost.Children.Add(new TextBlock { Text = member.Name });
            var combo = PathChoicePresentation.Combo(PathChoices.Categories(CategoryDisplayNames(), current: current, includeNone: true), current);
            combo.SelectionChanged += (_, _) => { if (!rebuildingAttributes && combo.SelectedValue is string value && value != current) ChangeAttribute(member.Name, value); };
            AttributesHost.Children.Add(combo); return true;
        }
        if (member.Type == "bounce")
        {
            var values = new List<string> { "bounce", "rise" };
            string current = Value(element, member.Name); if (!values.Contains(current)) values.Insert(0, current);
            AttributesHost.Children.Add(new TextBlock { Text = member.Name });
            var combo = new ComboBox { ItemsSource = values, SelectedItem = current, IsEditable = true, IsTextSearchEnabled = true, Text = current };
            combo.LostKeyboardFocus += (_, _) => { if (!rebuildingAttributes && combo.Text != current) ChangeAttribute(member.Name, combo.Text); };
            combo.SelectionChanged += (_, _) => { if (!rebuildingAttributes && combo.SelectedItem is string value && value != current) ChangeAttribute(member.Name, value); };
            AttributesHost.Children.Add(combo); return true;
        }
        if (member.Type == "mounts")
        {
            var selectedMounts = Value(element, member.Name).Split(',').Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string mount in PathingBehavior.Mounts)
            {
                var check = new CheckBox { Content = mount, IsChecked = selectedMounts.Contains(mount) };
                check.Click += (_, _) => { if (check.IsChecked == true) selectedMounts.Add(mount); else selectedMounts.Remove(mount); ChangeAttribute(member.Name, string.Join(',', selectedMounts)); };
                AttributesHost.Children.Add(check);
            }
            return true;
        }
        return false;
    }
    private void AddAdvancedActions(XElement element, string group)
    {
        if (group == "resetguid" || group == "checkpoint" && EffectiveValue(element, "cpReset", "\0") != "\0") AddResetMessageControls(element);
        if (group == "behavior")
        {
            var reset = new Button { Content = "Reset preview behavior state" }; reset.Click += (_, _) => { pathingState.Timers.Clear(); pathingState = new PathingPreviewState { Categories = pathingState.Categories }; SavePathingState(); advancedVisibility = ""; ApplyPreviewVisibility(); }; AttributesHost.Children.Add(reset);
        }
        if (group == "script") AddScriptPicker();
    }
    private void AddScriptPicker()
    {
        AttributesHost.Children.Add(new TextBlock { Text = "Hooks contain function calls, not filenames. A Blish HUD pack.lua loader must load your script library. In the referenced Blish implementation, script-filter true means hidden.", TextWrapping = TextWrapping.Wrap });
        var files = PathChoicePresentation.Combo(PathChoices.Files(codePages.Where(p => p.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name)));
        var functions = new ComboBox { IsEditable = true, ToolTip = "Lua function call" };
        var hooks = new ComboBox { ItemsSource = new[] { "script-trigger", "script-focus", "script-tick", "script-filter", "script-once" }, SelectedIndex = 0 };
        files.SelectionChanged += (_, _) => { var page = codePages.FirstOrDefault(p => p.Name == files.SelectedValue as string); functions.ItemsSource = page == null ? new List<string>() : System.Text.RegularExpressions.Regex.Matches(page.Editor.Text, @"(?:function\s+|local\s+function\s+)([A-Za-z_][\w.:]*)\s*\(").Select(m => m.Groups[1].Value + "()").Distinct().ToList(); };
        AttributesHost.Children.Add(files); AttributesHost.Children.Add(functions); AttributesHost.Children.Add(hooks);
        var assign = new Button { Content = "Assign function to hook" }; assign.Click += (_, _) => { if (hooks.SelectedItem is string name && !string.IsNullOrWhiteSpace(functions.Text)) ChangeAttribute(name, functions.Text); }; AttributesHost.Children.Add(assign);
        var loader = new Button { Content = "Create / open pack.lua loader" };
        loader.Click += (_, _) =>
        {
            var existing = codePages.FirstOrDefault(p => p.Name.Equals("pack.lua", StringComparison.OrdinalIgnoreCase));
            if (existing != null) { OpenTab(existing.Tab); return; }
            if (projectFolder == null) return;
            if (File.Exists(Path.Combine(projectFolder, "pack.lua"))) { Notice("Existing loader", "pack.lua already exists on disk. Import it into the project before editing; it has not been overwritten."); return; }
            string Quote(string value) => "\"" + value.Replace("\\", "/").Replace("\"", "\\\"") + "\"";
            string text = "-- Blish HUD entry point. Review library order and add new libraries here.\n" + string.Join("\n", codePages.Where(p => p.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)).Select(p => "Pack:Require(" + Quote(p.Name) + ")")) + "\n";
            RegisterCode(new ProjectCode("pack.lua", text, true)); if (!importedAssets.Contains("pack.lua", StringComparer.OrdinalIgnoreCase)) importedAssets.Add("pack.lua");
            OpenTab(codePages[^1].Tab); RefreshExplorer(refreshScene: false);
            StatusText.Text = "Created pack.lua for review. Save Project before publishing.";
        };
        AttributesHost.Children.Add(loader);

    }
}
