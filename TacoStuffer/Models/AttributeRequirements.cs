using System.Globalization;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record AttributeAlert(string Message, string Detail);
public readonly record struct AttributeRequirementContext(bool InfoEnabled = true, bool ClipboardEnabled = true,
    bool PreviewActive = false, bool TestInteractions = false, bool AutomaticClipboardEnabled = true, bool CheckpointTesting = false);

// Advisory authoring checks: they never change XML or disable a reader's runtime
// fallbacks. Effective values include category inheritance and explicit empties.
public static class AttributeRequirements
{
    public static IReadOnlyList<AttributeAlert> Evaluate(XElement element, AttributeDefinition group,
        IReadOnlyDictionary<string, XElement> categories, AttributeRequirementContext context)
    {
        var alerts = new List<AttributeAlert>();
        string E(string name) => AttributeRules.Effective(element, name, "", categories);
        bool Has(string name) => AttributeRules.Effective(element, name, "\0", categories) != "\0";
        bool Text(string name) => !string.IsNullOrWhiteSpace(E(name));
        bool Flag(string name) => VisualAttributeMath.Enabled(E(name));
        bool Number(string name, out double number) => double.TryParse(E(name), NumberStyles.Float,
            CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
        bool rangeValid = Number("triggerRange", out double range) && range >= 0;
        bool category = element.Name.LocalName == "MarkerCategory";
        bool copy = E("copy").Length > 0;
        bool showHide = Text("show") || Text("hide");
        string toggle = Has("toggleCategory") ? E("toggleCategory") : E("toggle");
        bool toggles = !string.IsNullOrWhiteSpace(toggle);
        bool validBehavior = int.TryParse(E("behavior"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int behavior);
        bool behaviorAction = PathingBehavior.Modes.Any(m => m.Id == behavior) && behavior is not (0 or 5);
        bool bounce = E("bounce").Equals("bounce", StringComparison.OrdinalIgnoreCase) || E("bounce").Equals("rise", StringComparison.OrdinalIgnoreCase);
        var resets = GuidResetTargets.Parse(E("resetguid"));
        var checkpointReset = GuidResetTargets.Parse(E("cpReset"));
        bool activation = copy || showHide || toggles || behaviorAction || resets.Values.Count > 0 || checkpointReset.Values.Count == 1;
        bool infoUsesRange = Text("info") && !Number("infoRange", out _);

        void Requires(string name, string? detail = null) => alerts.Add(new("This attribute requires: " + name,
            detail ?? "Add " + name + " through Add Attributes or inherit it from a category."));
        void Trigger()
        {
            if (!rangeValid) Requires("Trigger", "Add a Trigger Range in meters. An inherited Trigger Range also satisfies this check. Readers may use a fallback radius when it is omitted; this alert recommends an explicit authoring setting.");
        }
        void Preview()
        {
            if (context.PreviewActive && !context.TestInteractions)
                Requires("Test marker interactions", "Enable Test marker interactions in the overlay to test behavior and category actions in Taco Stuffer.");
        }

        switch (group.Key)
        {
            case "copy":
                if (!copy) Requires("Copy Text", "Enter the text to copy.");
                if (!category) Trigger(); // Category/separator copying has no proximity requirement.
                if (!context.ClipboardEnabled) Requires("clipboard permission", "Enable clipboard actions in Preferences to test Copy.");
                else if (!category && Flag("autoTrigger") && !context.AutomaticClipboardEnabled)
                    Requires("automatic clipboard permission", "Preferences currently allow manual copying only. Allow automatic clipboard actions to test Auto Trigger copying.");
                if (!category && Flag("autoTrigger")) Preview();
                break;
            case "showHide":
                if (!showHide) Requires("a target category", "Choose a category in Show or Hide.");
                Trigger(); Preview(); break;
            case "toggle":
                if (!toggles) Requires("a target category", "Choose a Toggle target. An explicitly present toggleCategory takes precedence over toggle.");
                Trigger(); Preview(); break;
            case "behavior":
                if (behavior == 5) alerts.Add(new("Behavior mode 5 is not implemented.", "Choose a supported behavior mode; no map-reset algorithm is available for mode 5."));
                else if (!validBehavior || !PathingBehavior.Modes.Any(m => m.Id == behavior)) Requires("a supported behavior mode");
                if (behaviorAction)
                {
                    Trigger(); Preview();
                    if (!category && PathingBehavior.GuidKey(E("GUID")).Length == 0) Requires("a valid GUID", "Behavior state is tracked by marker GUID.");
                }
                if (behavior == 4 && (!Number("resetLength", out double seconds) || seconds <= 0))
                    Requires("Reset Length greater than 0", "Add Reset Length in seconds. A zero-length timer expires immediately.");
                if (behavior == 0 && Flag("invertBehavior")) Requires("an active Behavior", "Invert Behavior has no effect when Behavior is 0.");
                break;
            case "bounce":
                if (!bounce) Requires("Bounce or Rise", "Choose a supported animation mode.");
                Trigger(); break;
            case "trigger":
                if (!activation && !bounce && !infoUsesRange)
                    alerts.Add(new("No effect without an attribute that uses Trigger.", "Add Copy, Show/Hide, Toggle, Reset GUID, an active Behavior, or Bounce. Info can also use Trigger Range when Info Range is omitted."));
                if (Has("triggerRange") && !rangeValid) Requires("a valid Trigger Range", "Enter a finite, nonnegative distance in meters.");
                if (Flag("autoTrigger") && !activation)
                    alerts.Add(new("Auto Trigger has no effect without an activation action.", "Bounce uses proximity focus and Info uses proximity display; neither needs automatic activation. Add Copy, Show/Hide, Toggle, Reset GUID, or an active Behavior."));
                else if (Flag("autoTrigger")) Preview();
                break;
            case "resetguid":
                if (resets.Values.Count == 0) Requires("checkpoint GUIDs", "Choose markers or paste a comma-separated list of checkpoint GUIDs.");
                if (resets.Invalid.Count > 0) Requires("valid GUIDs", "Invalid targets: " + string.Join(", ", resets.Invalid));
                Trigger(); Preview();
                break;
            case "checkpoint":
                if (!context.CheckpointTesting) Requires("Taquito or checkpoint test mode", "Use Test checkpoints in the overlay to preview the temporary race. These custom attributes require Taquito when the pack is compiled.");
                if (Flag("isCheckpoint"))
                {
                    if (!category && PathingBehavior.GuidKey(E("GUID")).Length == 0) Requires("a valid checkpoint GUID");
                    foreach (string color in new[] { "cpColor", "cpPreColor" })
                        if (Has(color) && !CheckpointAuthoring.TryRgba(E(color), out _)) Requires(color + " in RRGGBBAA format");
                    if (Has("nextCheckCount") && !CheckpointAuthoring.TryCount(E("nextCheckCount"), out _)) Requires("a nonnegative nextCheckCount");
                    if (Has("cpRadius") && (!Number("cpRadius", out double radius) || radius <= 0)) Requires("a positive cpRadius");
                    if (GuidResetTargets.Parse(E("nextCheck")).Invalid.Count > 0) Requires("valid nextCheck GUIDs");
                }
                if (Text("cpReset"))
                {
                    if (checkpointReset.Invalid.Count > 0 || checkpointReset.Values.Count != 1) Requires("one first-checkpoint GUID for cpReset");
                    if (!rangeValid && !Flag("autoTrigger")) Requires("Trigger or Auto Trigger", "Configure manual Trigger Range or automatic activation for the reset marker. Normal checkpoint contact uses cpRadius.");
                }
                break;
            case "cpFinish":
                if (Flag("cpFinish") && !category && PathingBehavior.GuidKey(E("GUID")).Length == 0) Requires("a valid finish GUID");
                break;
            case "info":
                if (!Text("info")) Requires("Info Text", "Enter the instructions to display.");
                if (!context.InfoEnabled) Requires("Info popups enabled", "Enable Show marker information in Preferences.");
                break;
            case "mount":
                if (!E("mount").Split(',').Any(token => PathingBehavior.Mounts.Contains(token.Trim(), StringComparer.OrdinalIgnoreCase)))
                    Requires("at least one mount", "Select allowed mounts. An explicitly empty Mount list hides the item; none means unmounted.");
                break;
            case "script":
                Requires("a Lua-capable reader", "Taco Stuffer preserves script hooks but cannot execute them. Compile the pack and load it in a compatible reader with scripts enabled.");
                break;
            default:
                foreach (var member in group.Members.Where(m => !m.Optional && !Has(m.Name))) Requires(member.Name);
                break;
        }
        return alerts;
    }
}
