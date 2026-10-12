using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private List<string> categoryOrder = [];
    private ExplorerRow? middleDragRow;
    private Point middleDragOrigin;
    private DropGuideAdorner? dropGuide;
    private AdornerLayer? dropGuideLayer;
    private void ClearDropGuide()
    {
        if (dropGuide != null) dropGuideLayer?.Remove(dropGuide);
        dropGuide = null;
        dropGuideLayer = null;
    }

    private void ShowDropGuide(FrameworkElement target, CategoryDropPosition position)
    {
        ClearDropGuide();
        dropGuideLayer = AdornerLayer.GetAdornerLayer(target);
        if (dropGuideLayer == null) return;
        dropGuide = new DropGuideAdorner(target, position);
        dropGuideLayer.Add(dropGuide);
    }

    private void PanelDragLeave(object sender, DragEventArgs e) => ClearDropGuide();

    private void ElementsMiddleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        var item = RowContainer(e.OriginalSource);
        middleDragRow = item == null ? null : ElementsList.ItemContainerGenerator.ItemFromContainer(item) as ExplorerRow;
        if (middleDragRow?.Kind is not ("document" or "MarkerCategory" or "POI" or "Trail")) middleDragRow = null;
        middleDragOrigin = e.GetPosition(ElementsList);
    }

    private void ElementsMiddleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        middleDragRow = null;
        ClearDropGuide();
        e.Handled = true;
    }

    private void ElementsMiddleMove(MouseEventArgs e)
    {
        if (middleDragRow == null || e.MiddleButton != MouseButtonState.Pressed) return;
        Point point = e.GetPosition(ElementsList);
        if (Math.Abs(point.X - middleDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - middleDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = middleDragRow;
        middleDragRow = null;
        e.Handled = true;
        try
        {
            DragDrop.DoDragDrop(ElementsList, new DataObject("TacoStuffer.ElementOrder", row), DragDropEffects.Move);
        }
        finally
        {
            ClearDropGuide();
        }
    }

    private (ExplorerRow Source, ExplorerRow Target, ListBoxItem Item, CategoryDropPosition Position)? ElementOrderTarget(DragEventArgs e)
    {
        if (e.Data.GetData("TacoStuffer.ElementOrder") is not ExplorerRow source) return null;
        var item = RowContainer(e.OriginalSource);
        if (item == null || ElementsList.ItemContainerGenerator.ItemFromContainer(item) is not ExplorerRow target) return null;
        if (source.DocumentName == target.DocumentName && source.Key == target.Key) return null;
        bool valid = source.Kind == "document" ? target.Kind == "document"
            : source.Kind == "MarkerCategory" ? target.Kind == "MarkerCategory" && source.Element != null && target.Element != null && CategoryFullPath(source.Element).Contains('.') && CategoryFullPath(target.Element).Contains('.') && CategoryFullPath(source.Element) != CategoryFullPath(target.Element) && !CategoryFullPath(target.Element).StartsWith(CategoryFullPath(source.Element) + '.', StringComparison.Ordinal)
            : (source.Kind is "POI" or "Trail") && (target.Kind is "POI" or "Trail") && source.DocumentName == target.DocumentName && source.Element != null && target.Element != null && Value(source.Element, "type") == Value(target.Element, "type");
        if (!valid) return null;
        var position = e.GetPosition(item).Y < item.ActualHeight / 2 ? CategoryDropPosition.Before : CategoryDropPosition.After;
        return (source, target, item, position);
    }

    private void ElementsOrderOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        ClearDropGuide();
        if (ElementOrderTarget(e) is not { } target) return;
        e.Effects = DragDropEffects.Move;
        ShowDropGuide(target.Item, target.Position);
        StatusText.Text = "Reorder " + target.Source.Label + " · " + target.Position + " · " + target.Target.Label;
    }

    private void ElementsOrderDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropGuide();
        if (ElementOrderTarget(e) is not { } drop || !TrySyncXml()) return;
        try
        {
            if (drop.Source.Kind == "document")
            {
                ReorderXml(drop.Source.DocumentName, drop.Target.DocumentName, drop.Position);
                return;
            }
            var source = ElementsList.Items.Cast<ExplorerRow>().First(r => r.DocumentName == drop.Source.DocumentName && r.Key == drop.Source.Key);
            var target = ElementsList.Items.Cast<ExplorerRow>().First(r => r.DocumentName == drop.Target.DocumentName && r.Key == drop.Target.Key);
            if (source.Kind == "MarkerCategory")
            {
                string old = CategoryFullPath(source.Element!);
                string destination = CategoryFullPath(target.Element!);
                var changed = CategoryOperations.Move(pages.Select(p => p.Xml).ToList(), old, destination, drop.Position, out string path, categoryOrder);
                categoryOrder = CategoryOrder.Move(categoryOrder, old, path, destination, drop.Position);
                ApplyDocuments(changed);
            }
            else
            {
                var element = source.Element!;
                element.Remove();
                if (drop.Position == CategoryDropPosition.Before) target.Element!.AddBeforeSelf(element);
                else target.Element!.AddAfterSelf(element);
                WritePages();
            }
            StatusText.Text = "Element order updated. Save to persist; build uses this order.";
        }
        catch (InvalidOperationException ex)
        {
            Notice("Reorder blocked", ex.Message);
        }
    }

    private void ReorderXml(string sourceName, string targetName, CategoryDropPosition position)
    {
        if (!TrySyncXml()) return;
        var source = pages.First(p => p.Name == sourceName);
        var target = pages.First(p => p.Name == targetName);
        if (source == target) return;
        pages.Remove(source);
        int index = pages.IndexOf(target) + (position == CategoryDropPosition.After ? 1 : 0);
        pages.Insert(index, source);
        var mainSelected = Documents.SelectedItem;
        var comparisonSelected = ComparisonDocuments.SelectedItem;
        var owners = pages.ToDictionary(p => p, p => TabOwner(p.Tab));
        updating = true;
        foreach (var page in pages) owners[page]?.Items.Remove(page.Tab);
        foreach (var page in pages)
        {
            if (owners[page] == Documents) Documents.Items.Insert(Documents.Items.IndexOf(AddTab), page.Tab);
            else if (owners[page] == ComparisonDocuments) ComparisonDocuments.Items.Add(page.Tab);
        }
        Documents.SelectedItem = mainSelected;
        ComparisonDocuments.SelectedItem = comparisonSelected;
        updating = false;
        RefreshExplorer();
        StatusText.Text = "XML order updated for Elements, tabs, saved project and archive.";
    }
}
