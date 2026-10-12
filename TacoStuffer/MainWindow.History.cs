using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private readonly HistoryJournal<EditorState> history = new(s => s.Fingerprint());
    private bool restoringHistory, historyQueued;
    private string? historyLabel, recordingHistorySource;
    private long recordingHistoryTrail;
    private EditorState? lastHistoryState;
    private EditorState CaptureHistory()
    {
        return new(pages.Select(p => new HistoryXml(p.Name, p.FormattedXml, PageSource(p),
            TabOwner(p.Tab) != null || detachedTabs.ContainsKey(p.Tab))).ToList(),
            editableTrails.Select(t => recordingHistorySource != null && t.Number == recordingHistoryTrail ? recordingHistorySource : EditableTrails.Serialize(t)).ToList(),
            categoryOrder.ToList(), new(textureLabels, StringComparer.OrdinalIgnoreCase), nextMarkerNumber, nextTrailNumber);
    }

    private static string HistorySource(RichTextBox editor)
    {
        // TextRange appends the final paragraph terminator; do not feed it back
        // as content, otherwise each restore adds another blank line.
        string text = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
        return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text;
    }

    private void ResetHistory()
    {
        recordingHistorySource = null;
        historyQueued = false;
        historyLabel = null;
        lastHistoryState = CaptureHistory();
        history.Reset(lastHistoryState);
        UpdateHistoryMenu();
    }

    private void QueueHistoryCheckpoint(string? label = null)
    {
        if (restoringHistory || !history.Initialized) return;
        if (label != null) historyLabel = label;
        if (historyQueued) return;
        historyQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (historyQueued) CommitHistoryCheckpoint();
        }));
    }

    private void CommitHistoryCheckpoint(string? label = null)
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("History.Commit");
        if (restoringHistory || !history.Initialized) return;
        historyQueued = false;
        var state = CaptureHistory();
        if (history.Commit(state, label ?? historyLabel ?? DescribeHistory(lastHistoryState, state))) lastHistoryState = state;
        historyLabel = null;
        UpdateHistoryMenu();
    }

    private void FlushHistoryCheckpoint()
    {
        if (restoringHistory || !history.Initialized) return;
        if (xmlTimer.IsEnabled)
        {
            xmlTimer.Stop();
            TrySyncXml();
        }
        if (historyQueued) CommitHistoryCheckpoint();
    }

    private static string DescribeHistory(EditorState? before, EditorState after)
    {
        if (before == null) return "Edit project";
        if (before.Documents.Count != after.Documents.Count) return after.Documents.Count > before.Documents.Count ? "Add XML document" : "Delete XML document";
        if (!before.Documents.Select(d => d.Name).SequenceEqual(after.Documents.Select(d => d.Name))) return "Rename / reorder XML documents";
        if (!before.Trails.SequenceEqual(after.Trails)) return "Edit trail geometry";
        int Count(EditorState s, string name) => s.Documents.Sum(d => XDocument.Parse(d.Xml).Descendants(name).Count());
        foreach (string kind in new[] { "POI", "Trail", "MarkerCategory" })
        {
            int a = Count(before, kind), b = Count(after, kind);
            if (a != b) return (b > a ? "Add " : "Delete ") + (kind == "POI" ? "marker" : kind == "Trail" ? "trail" : "category");
        }
        if (!before.TextureLabels.OrderBy(p => p.Key).SequenceEqual(after.TextureLabels.OrderBy(p => p.Key))) return "Rename texture";
        return "Edit XML / attributes / categories";
    }

    private void BeginRecordingHistory()
    {
        FlushHistoryCheckpoint();
        if (recordingTrail == null) return;
        recordingHistoryTrail = recordingTrail.Number;
        recordingHistorySource = EditableTrails.Serialize(recordingTrail);
    }

    private void CompleteRecordingHistory()
    {
        if (recordingHistorySource == null) return;
        recordingHistorySource = null;
        CommitHistoryCheckpoint("Record trail length");
    }

    private void UpdateHistoryMenu()
    {
        UndoMenuItem.IsEnabled = history.CanUndo;
        RedoMenuItem.IsEnabled = history.CanRedo;
        UndoMenuItem.Header = history.CanUndo ? "Undo " + history.UndoEntries[^1].Label : "Undo";
        RedoMenuItem.Header = history.CanRedo ? "Redo " + history.RedoEntries[^1].Label : "Redo";
    }

    private void UndoProject(object? sender = null, RoutedEventArgs? e = null) => StepHistory(false);

    private void RedoProject(object? sender = null, RoutedEventArgs? e = null) => StepHistory(true);

    private void StepHistory(bool redo)
    {
        if (projectFile == null) return;
        if (trailRecording && !trailPaused)
        {
            PauseTrailSamples();
            UpdateTrailControls();
        }
        FlushHistoryCheckpoint();
        if (redo ? !history.CanRedo : !history.CanUndo)
        {
            StatusText.Text = redo ? "Nothing to redo." : "Nothing to undo.";
            return;
        }
        string label = (redo ? history.RedoEntries : history.UndoEntries)[^1].Label;
        var state = redo ? history.Redo() : history.Undo();
        RestoreHistory(state);
        lastHistoryState = state;
        UpdateHistoryMenu();
        StatusText.Text = (redo ? "Redid " : "Undid ") + label + ".";
    }

    private void RestoreHistory(EditorState state)
    {
        restoringHistory = true;
        updating = true;
        xmlTimer.Stop();
        sourceSelectionTimer.Stop();
        string? document = selected?.DocumentName, key = selected?.Key;
        var pointSelection = selectedTrailPoints.ToList();
        try
        {
            recordingHistorySource = null;
            trailRecording = trailPaused = false;
            recordingTrail = null;
            recordingSegment = null;
            trailSampler.Reset();
            foreach (var page in pages.Where(p => state.Documents.All(d => d.Name != p.Name)).ToList())
            {
                CloseTab(page.Tab);
                pages.Remove(page);
            }
            var ordered = new List<XmlPage>();
            foreach (var saved in state.Documents)
            {
                var page = pages.FirstOrDefault(p => p.Name == saved.Name);
                if (page == null)
                {
                    var editor = new RichTextBox { FontFamily = new FontFamily("Consolas"), FontSize = 13, AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                    var tab = new TabItem { Content = editor };
                    Register(saved.Name, editor, tab, saved.Xml, loadEditor: false);
                    page = pages[^1];
                    if (saved.Open) OpenTab(tab);
                }
                updating = true;
                page.Xml = ParseXml(saved.Xml);
                SetPageSource(page, saved.Source);
                page.Dirty = saved.Source != saved.Xml;
                ordered.Add(page);
            }
            pages.Clear();
            pages.AddRange(ordered);
            // Existing detached/comparison views stay attached; only reorder their tabs.
            foreach (var host in new[] { Documents, ComparisonDocuments })
            {
                var attached = pages.Where(p => host.Items.Contains(p.Tab)).ToList();
                var active = host.SelectedItem;
                foreach (var page in attached) host.Items.Remove(page.Tab);
                foreach (var page in attached) if (host == Documents) host.Items.Insert(host.Items.IndexOf(AddTab), page.Tab);
                else host.Items.Add(page.Tab);
                if (active != null && host.Items.Contains(active)) host.SelectedItem = active;
            }
            searchPointIds.Clear();
            assetPickerCache.Clear();
            editableTrails.Clear();
            editableTrails.AddRange(state.Trails.Select(EditableTrails.Deserialize));
            nextMarkerNumber = state.NextMarker;
            nextTrailNumber = state.NextTrail;
            categoryOrder = state.CategoryOrder.ToList();
            textureLabels.Clear();
            foreach (var pair in state.TextureLabels) textureLabels[pair.Key] = pair.Value;
            pointLookups.Clear();
            selectedTrailPoints.Clear();
            initializedTrailSegments.Clear();
            selected = null;
            updating = false;
            RefreshExplorer();
            var row = document == null || key == null ? null : RevealRow(document, key);
            if (row != null)
            {
                selected = row;
                ElementsList.SelectedItem = row;
            }
            else
            {
                AttributesHost.Children.Clear();
                SelectionTitle.Text = "Select an element";
                CategoryPath.Text = "";
            }
            var validPoints = pointSelection.Where(p => ResolvePoint(p) != null).ToList();
            if (validPoints.Count > 0) SelectTrailPoints(validPoints);
            RefreshOverlayWorkspace();
            UpdateComparisonLayout();
            UpdateTrailControls();
            EditorNotice.Text = "History restored. Save to retain this state.";
        }
        finally
        {
            updating = false;
            restoringHistory = false;
            historyQueued = false;
            historyLabel = null;
        }
    }

    private void ShowHistory(object sender, RoutedEventArgs e)
    {
        FlushHistoryCheckpoint();
        new HistoryWindow(history, () => UndoProject(), () => RedoProject()) { Owner = this }.ShowDialog();
    }
}
