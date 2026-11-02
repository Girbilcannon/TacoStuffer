using System.Xml.Linq;
using TacoStuffer.Models;

namespace TacoStuffer;

public partial class MainWindow
{
    private void ChangeAssetName(string attribute, string value)
    {
        if (!TrySyncXml() || selected?.Element == null) return;
        var selection = AttributeTargets().ToHashSet();
        if (selection.Any(e => e.Name.LocalName is not ("POI" or "Trail")))
        {
            Notice("Asset naming", "Select markers and trails only to rename recorded assets together.");
            ShowAttributes();
            return;
        }
        var targets = allExplorerRows.Where(r => (r.Kind is "POI" or "Trail") && r.Element != null && selection.Contains(r.Element))
            .Select(r => r.Element!).Distinct().ToList();
        if (targets.Count == 0) targets.Add(selected.Element);
        ApplyAssetNameChanges(targets, new Dictionary<string, string> { [attribute] = value });
    }

    // Both entry points plan every requested field before changing any XML.
    private void ApplyAssetNameChanges(IReadOnlyList<XElement> targets, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0) return;
        try
        {
            var plans = BatchAssetNaming.PlanChanges(targets, values,
                pages.SelectMany(p => p.Xml.Descendants().Where(e => e.Name.LocalName is "POI" or "Trail")));
            FlushHistoryCheckpoint();
            foreach (var plan in plans)
                for (int i = 0; i < targets.Count; i++) SetValue(targets[i], plan.Key, plan.Value[i]);
            WritePages();
            QueueHistoryCheckpoint(targets.Count > 1 ? "Batch rename assets" : "Rename asset");
        }
        catch (InvalidOperationException ex)
        {
            Notice("Asset naming", ex.Message);
            ShowAttributes();
        }
    }

    private void RenameOutlinerAssets(ExplorerRow actual)
    {
        var selection = ElementsList.SelectedItems.Cast<ExplorerRow>().ToList();
        if (!selection.Any(r => r.RowId == actual.RowId)) selection = [actual];
        if (selection.Any(r => r.Kind is not ("POI" or "Trail")))
        {
            Notice("Asset naming", "Select markers and trails only to rename recorded assets together.");
            return;
        }
        var ids = selection.Select(r => r.RowId).ToHashSet();
        // Snapshot identities and hierarchy order before opening the modal dialog.
        var orderedIds = allExplorerRows.Where(r => ids.Contains(r.RowId)).Select(r => r.RowId).Distinct().ToList();
        bool batch = orderedIds.Count > 1;
        var dialog = new Views.RenameWindow(batch ? $"Rename {orderedIds.Count} assets" : "Rename " + actual.Kind,
            Value(actual.Element!, "name", AssetStem(actual.Element!)),
            Value(actual.Element!, "DisplayName", AssetStem(actual.Element!)), batch)
        { Owner = this };
        if (dialog.ShowDialog() != true || !TrySyncXml()) return;
        var rows = allExplorerRows.Where(r => (r.Kind is "POI" or "Trail") && r.Element != null)
            .GroupBy(r => r.RowId).ToDictionary(g => g.Key, g => g.First().Element!);
        if (orderedIds.Any(id => !rows.ContainsKey(id)))
        {
            Notice("Asset naming", "The selected assets changed while the dialog was open. Select them again before renaming.");
            return;
        }
        var values = new Dictionary<string, string>();
        if (!batch || dialog.NameEdited) values["name"] = dialog.NameText;
        if (!batch || dialog.DisplayNameEdited) values["DisplayName"] = dialog.DisplayNameText;
        ApplyAssetNameChanges(orderedIds.Select(id => rows[id]).ToList(), values);
    }
}
