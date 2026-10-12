using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private readonly HashSet<string> collapsedElements = [];
    private List<ExplorerRow> allExplorerRows = [];
    private void InitializeHierarchy() => ElementsList.AddHandler(ExplorerRowView.ToggleEvent, new RoutedEventHandler(ToggleHierarchy));

    private void ToggleHierarchy(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ExplorerRowView { Row: { } row }) return;
        ToggleHierarchyRow(row);
        e.Handled = true;
    }

    private void ToggleHierarchyRow(ExplorerRow row)
    {
        if (!row.HasChildren) return;
        if (!collapsedElements.Add(row.RowId)) collapsedElements.Remove(row.RowId);
        selected = row;
        RefreshExplorer(refreshScene: false);
        var visible = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.RowId == row.RowId);
        if (visible == null) return;
        updating = true;
        ElementsList.SelectedItems.Clear();
        ElementsList.SelectedItems.Add(visible);
        selected = visible;
        updating = false;
        if (visible.Element != null) ShowAttributes();
        else
        {
            AttributesHost.Children.Clear();
            SelectionTitle.Text = visible.Label;
            CategoryPath.Text = "";
        }
    }

    private ExplorerRow? RevealRow(string document, string key)
    {
        if (SearchQuery.Length > 0 && !ElementsList.Items.Cast<ExplorerRow>().Any(r => r.DocumentName == document && r.Key == key)) ClearSearch();
        var row = allExplorerRows.FirstOrDefault(r => r.DocumentName == document && r.Key == key);
        if (row == null && TrailPointRef.Parse(key) is { } point)
            return RevealTrailPointRows(document, [point]).FirstOrDefault();
        if (row == null) return null;
        bool changed = false;
        string? parent = row.ParentId;
        while (parent != null)
        {
            changed |= collapsedElements.Remove(parent);
            parent = allExplorerRows.FirstOrDefault(r => r.RowId == parent)?.ParentId;
        }
        if (changed) RefreshExplorer(refreshScene: false);
        return ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.DocumentName == document && r.Key == key);
    }

    private List<ExplorerRow> RevealTrailPointRows(string document, IEnumerable<TrailPointRef> points)
    {
        var refs = points.ToList();
        if (SearchQuery.Length > 0 && refs.Any(p => !ElementsList.Items.Cast<ExplorerRow>().Any(r => r.DocumentName == document && r.Key == p.Key))) ClearSearch();
        var index = allExplorerRows.GroupBy(r => r.RowId).ToDictionary(g => g.Key, g => g.First());
        bool changed = false;
        foreach (var reference in refs)
        {
            string? id = document + $"|segment|{reference.Trail}|{reference.Segment}";
            while (id != null && index.TryGetValue(id, out var row))
            {
                changed |= collapsedElements.Remove(id);
                id = row.ParentId;
            }
        }
        if (changed) RefreshExplorer(refreshScene: false);
        var visible = ElementsList.Items.Cast<ExplorerRow>().GroupBy(r => r.RowId).ToDictionary(g => g.Key, g => g.First());
        return refs.Select(p => visible.GetValueOrDefault(document + "|" + p.Key)).OfType<ExplorerRow>().ToList();
    }

    private List<XElement> AttributeTargets()
    {
        var targets = ElementsList.SelectedItems.Cast<ExplorerRow>().Where(r => r.Kind is "MarkerCategory" or "POI" or "Trail").Select(r => r.Element!).Distinct().ToList();
        if (targets.Count == 0 && selected?.Element != null && selected.Kind is not ("point" or "segment")) targets.Add(selected.Element);
        return targets;
    }

    private bool AttributeApplies(XElement target, string name)
    {
        if ((target.Name == "POI" && name.Equals("texture", StringComparison.OrdinalIgnoreCase)) || (target.Name == "Trail" && name.Equals("iconFile", StringComparison.OrdinalIgnoreCase))) return false;
        var definition = catalog.Groups.FirstOrDefault(g => g.Members.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        return definition == null || definition.Applies(target);
    }

    private void ApplyAttributeValues(IReadOnlyList<XElement> targets, IReadOnlyDictionary<string, string?> values)
    {
        try
        {
            AttributeRules.Apply(targets, values, AttributeApplies);
            WritePages();
        }
        catch (InvalidOperationException ex)
        {
            Notice("Attribute type", ex.Message);
        }
    }

    private void AddMissingControls(AttributeDefinition definition)
    {
        if (!TrySyncXml()) return;
        var targets = AttributeTargets();
        var members = definition.Members.Where(m => !m.Optional).ToList();
        if (targets.Any(t => members.Any(m => !AttributeApplies(t, m.Name))))
        {
            Notice("Attribute type", "This control does not apply to every selected object.");
            return;
        }
        foreach (var target in targets)
        {
            var values = members.Where(m => EffectiveValue(target, m.Name, "\0") == "\0").ToDictionary(m => m.Name, m => (string?)m.DefaultValue);
            AttributeRules.Apply([target], values, AttributeApplies);
        }
        WritePages();
    }
}
