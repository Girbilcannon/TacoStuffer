using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record AttributeMemberSelection(AttributeDefinition Group, string Name);

// Picker-only authoring policy. No catalog defaults or runtime semantics change.
// Unknown/custom bundles stay intact: optional does not imply independent.
public static class AttributePickerOptions
{
    private static readonly HashSet<string> IndependentGroups = new(StringComparer.OrdinalIgnoreCase) {
        "iconFile", "texture", "fade", "rotate", "position", "mapDisplaySize",
        "showHide", "toggle", "info", "copy", "trigger", "tip", "script"
    };
    public static bool CanSplit(AttributeDefinition group) => !group.IsCustom && group.Members.Count > 1 && IndependentGroups.Contains(group.Key);
    public static string Label(string name) => name switch
    {
        "iconFile" => "Icon File",
        "iconSize" => "Icon Size",
        "texture" => "Texture",
        "trailScale" => "Trail Scale",
        "fadeNear" => "Fade Near",
        "fadeFar" => "Fade Far",
        "rotate-x" => "Rotate X",
        "rotate-y" => "Rotate Y",
        "rotate-z" => "Rotate Z",
        "xpos" => "Position X",
        "ypos" => "Position Y",
        "zpos" => "Position Z",
        "mapDisplaySize" => "Map Display Size",
        "scaleOnMapWithZoom" => "Scale on Map with Zoom",
        "show" => "Show",
        "hide" => "Hide",
        "toggle" => "Toggle",
        "toggleCategory" => "Toggle Category",
        "info" => "Info Text",
        "infoRange" => "Info Range",
        "copy" => "Copy Text",
        "copy-message" => "Copy Message",
        "triggerRange" => "Trigger Range",
        "autoTrigger" => "Auto Trigger",
        "tip-name" => "Tip Name",
        "tip-description" => "Tip Description",
        "script-trigger" => "Script Trigger",
        "script-focus" => "Script Focus",
        "script-tick" => "Script Tick",
        "script-filter" => "Script Filter",
        "script-once" => "Script Once",
        _ => name
    };

    // A parent checkbox requests the complete independent control bundle. Child
    // checkboxes request exactly one XML member through AddMember below.
    public static Dictionary<string, string?> AddBundle(XElement target, AttributeDefinition group, IReadOnlyDictionary<string, XElement> categories)
    {
        if (!CanSplit(group)) return AttributePaneRules.AddGroup(target, group, categories);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in group.Members)
        {
            // toggleCategory is a higher-priority alias for toggle. Creating an
            // empty alias would mask the value the author enters into toggle.
            // Keep that existing alternate-target option opt-in, not blank.
            if (IsOptionalAlias(group, member) && AttributeRules.Effective(target, member.Name, "\0", categories) == "\0") continue;
            foreach (var pair in AddMember(target, new(group, member.Name), categories)) values[pair.Key] = pair.Value;
        }
        return values;
    }

    public static bool HasCompleteBundle(XElement target, AttributeDefinition group) => group.Members
        .Where(m => !IsOptionalAlias(group, m)).All(m => AttributePaneRules.HasLocal(target, m.Name));
    private static bool IsOptionalAlias(AttributeDefinition group, AttributeMember member) =>
        group.Key.Equals("toggle", StringComparison.OrdinalIgnoreCase) && member.Name.Equals("toggleCategory", StringComparison.OrdinalIgnoreCase);

    public static Dictionary<string, string?> AddMember(XElement target, AttributeMemberSelection selection, IReadOnlyDictionary<string, XElement> categories)
    {
        if (!CanSplit(selection.Group) || !selection.Group.Applies(target))
            throw new InvalidOperationException("This attribute group does not offer individual fields for this object.");
        var member = selection.Group.Members.SingleOrDefault(m => m.Name.Equals(selection.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected attribute is not part of this group.");
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (AttributePaneRules.HasLocal(target, member.Name)) return values;
        // Explicit selection adds even an optional member, preserving each target's
        // effective inherited value. It never adds siblings or dependencies.
        string fallback = member.DefaultValue;
        if (member.Name.Equals("infoRange", StringComparison.OrdinalIgnoreCase))
            fallback = (MarkerInteraction.TryRadius(AttributeRules.Effective(target, "triggerRange", "", categories), out float range) ? range : 2)
                .ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        values[member.Name] = AttributeRules.Effective(target, member.Name, fallback, categories);
        return values;
    }
}
