using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private List<PackSelection> SelectionReferences(IEnumerable<ExplorerRow> rows) => rows.Where(r => r.Element != null).Select(r =>
    {
        int doc = pages.FindIndex(p => p.Name == r.DocumentName);
        int element = pages[doc].Xml.Descendants().ToList().IndexOf(r.Element!);
        if (element < 0) throw new InvalidOperationException("Selection changed; select the item again.");
        return new PackSelection(doc, element);
    }).ToList();

    private void ApplyDocuments(List<XDocument> changed)
    {
        for (int i = 0; i < pages.Count; i++) pages[i].Xml = changed[i];
        WritePages();
    }

    private void DeleteSelection()
    {
        if (ElementsList.SelectedItems.Cast<ExplorerRow>().Any(r => r.Kind == "document"))
        {
            DeleteXmlSelection();
            return;
        }
        if (ElementsList.SelectedItems.Cast<ExplorerRow>().Any(r => r.Kind == "segment"))
        {
            DeleteTrailSegments();
            return;
        }
        if (ElementsList.SelectedItems.Cast<ExplorerRow>().Any(r => r.Kind == "point"))
        {
            ShowTrailPointAttributes();
            DeleteTrailPoints(selectedTrailPoints.ToList());
            return;
        }
        if (!TrySyncXml()) return;
        var rows = ElementsList.SelectedItems.Cast<ExplorerRow>().Where(r => r.Kind is "MarkerCategory" or "POI" or "Trail").ToList();
        if (rows.Count == 0) return;
        try
        {
            var changed = PackEditing.Delete(pages.Select(p => p.Xml).ToList(), SelectionReferences(rows));
            int Count(IEnumerable<XDocument> docs, string kind) => docs.Sum(d => d.Descendants(kind).Count());
            var current = pages.Select(p => p.Xml).ToList();
            int categories = Count(current, "MarkerCategory") - Count(changed, "MarkerCategory");
            int markers = Count(current, "POI") - Count(changed, "POI");
            int trails = Count(current, "Trail") - Count(changed, "Trail");
            var confirm = new DeleteConfirmWindow(categories, markers, trails) { Owner = DeleteDialogOwner, Topmost = DeleteDialogOwner.Topmost };
            if (confirm.ShowDialog() != true) return;
            selected = null;
            ApplyDocuments(changed);
            PruneEditableTrails();
            RefreshExplorer();
            AttributesHost.Children.Clear();
            SelectionTitle.Text = "Select an element";
            CategoryPath.Text = "";
            StatusText.Text = $"Deleted {categories} category declarations, {markers} markers, {trails} trails. Asset files retained.";
        }
        catch (InvalidOperationException ex)
        {
            Notice("Delete blocked", ex.Message);
        }
    }

    private void PruneEditableTrails()
    {
        pointLookups.Clear();
        var guids = pages.SelectMany(p => p.Xml.Descendants("Trail")).Select(e => Value(e, "GUID")).ToHashSet();
        editableTrails.RemoveAll(t => !guids.Contains(t.Guid));
        if (recordingTrail != null && !editableTrails.Contains(recordingTrail))
        {
            StopTrailRecording();
            recordingTrail = null;
            recordingSegment = null;
        }
        selectedTrailPoints.RemoveWhere(p => ResolvePoint(p) == null);
        UpdateTrailControls();
    }

    private void DeleteXmlSelection()
    {
        if (!TrySyncXml()) return;
        var names = ElementsList.SelectedItems.Cast<ExplorerRow>().Where(r => r.Kind == "document").Select(r => r.DocumentName).ToHashSet();
        if (names.Count == 0 && selected?.Kind == "document") names.Add(selected.DocumentName);
        if (names.Count == 0) return;
        var confirm = new ConfirmActionWindow("Delete XML documents", "Delete " + string.Join(", ", names) + " and all marker/category/trail definitions inside? Files are removed from the project root on Save. Editable source files are retained on disk as untracked recovery copies.") { Owner = DeleteDialogOwner, Topmost = DeleteDialogOwner.Topmost };
        if (confirm.ShowDialog() != true) return;
        foreach (var page in pages.Where(p => names.Contains(p.Name)).ToList())
        {
            CloseTab(page.Tab);
            pages.Remove(page);
        }
        selected = null;
        PruneEditableTrails();
        if (pages.Count == 0) overlay?.Close();
        RefreshExplorer();
        AttributesHost.Children.Clear();
        SelectionTitle.Text = "Select an element";
        CategoryPath.Text = "";
        StatusText.Text = "XML documents deleted in memory. Save to update project files.";
    }

}
