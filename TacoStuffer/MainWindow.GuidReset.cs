using System.Windows;
using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private void ChooseResetTargets(XElement element, string attribute)
    {
        var current = GuidResetTargets.Parse(EffectiveValue(element, attribute, ""));
        if (current.Invalid.Count > 0)
        {
            Notice("Invalid GUID targets", "Correct the invalid GUIDs in this field before choosing markers.");
            return;
        }
        var labels = CategoryDisplayNames();
        var choices = pages.SelectMany(page => page.Xml.Descendants().Where(marker => marker.Name.LocalName == "POI" || marker.Name.LocalName == "Trail" && VisualAttributeMath.Enabled(EffectiveValue(marker, "cpFinish", "false")))
            .Where(marker => attribute == "resetguid" ? marker.Name.LocalName == "POI" : VisualAttributeMath.Enabled(EffectiveValue(marker, "isCheckpoint", "false")) || VisualAttributeMath.Enabled(EffectiveValue(marker, "cpFinish", "false"))).Select(marker => new
        {
            Guid = Value(marker, "GUID"),
            Key = PathingBehavior.GuidKey(Value(marker, "GUID")),
            Label = Value(marker, "DisplayName", Value(marker, "name", "Marker")),
            Detail = page.Name + " · " + labels.GetValueOrDefault(Value(marker, "type"), Value(marker, "type"))
        })).Where(marker => marker.Key.Length > 0).GroupBy(marker => marker.Key)
            .Select(group => new GuidTargetChoice(group.First().Guid,
                group.First().Label + (group.Count() > 1 ? $" (+{group.Count() - 1} linked)" : ""), group.First().Detail))
            .OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var known = choices.Select(c => PathingBehavior.GuidKey(c.Guid)).ToHashSet(StringComparer.Ordinal);
        var external = current.Values.Where(value => !known.Contains(PathingBehavior.GuidKey(value))).ToList();
        bool single = attribute == "cpReset";
        string title = attribute == "resetguid" ? "Choose reset targets" : single ? "Choose first checkpoint" : "Choose next checkpoints";
        var dialog = new ResetGuidPickerWindow(choices, current.Keys.ToHashSet(StringComparer.Ordinal), title, single) { Owner = this };
        if (dialog.ShowDialog() == true)
            ChangeAttribute(attribute, single ? dialog.SelectedGuids.FirstOrDefault() ?? external.FirstOrDefault() ?? "" : string.Join(',', external.Concat(dialog.SelectedGuids)));
    }
}
