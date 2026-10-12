using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TacoStuffer.Models;

namespace TacoStuffer;

public partial class MainWindow
{
    private string SearchQuery = "";
    private DispatcherTimer? searchTimer;
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchStatus == null) return;
        if (searchTimer == null)
        {
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            searchTimer.Tick += (_, _) =>
            {
                searchTimer.Stop();
                SearchQuery = SearchBox.Text.Trim();
                _ = ApplySearchFilter();
            };
        }
        searchTimer.Stop();
        searchTimer.Start();
    }

    private void ClearSearch()
    {
        searchTimer?.Stop();
        SearchQuery = "";
        SearchBox.Text = "";
        searchTimer?.Stop();
        _ = ApplySearchFilter();
    }

    private async void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            searchTimer?.Stop();
            SearchQuery = SearchBox.Text.Trim();
            await ApplySearchFilter();
            var row = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => ExplorerSearch.Matches(SearchText(r), SearchQuery));
            if (row != null)
            {
                FocusRow(row);
                ElementsList.ScrollIntoView(row);
            }
            e.Handled = true;
        }
    }

    private static string SearchText(ExplorerRow row) => row.Label + " " + row.DocumentName + " " + row.Key
        + (row.Kind is "point" or "segment" ? "" : " " + string.Join(" ", row.Element?.Attributes().Select(a => a.Name + " " + a.Value) ?? []));

    private List<SearchNode> searchNodes = [];
    private long searchRevision;
    private System.Threading.CancellationTokenSource? searchCancellation;
    private readonly Dictionary<TrailSegment, (long Revision, int Count, long[] Ids)> searchPointIds = [];
    private sealed record SearchSegment(ExplorerRow Row, long Trail, long Segment, long[] Points);

    private sealed record SearchResult(List<ExplorerRow> Rows, int Matches);

    private List<SearchSegment> SearchSegments(List<ExplorerRow> rows)
    {
        var result = new List<SearchSegment>();
        foreach (var row in rows.Where(r => r.Kind == "segment"))
        {
            if (TrailFor(row.Element) is not { } trail || !long.TryParse(row.Key.Split('|').Last(), out long id)) continue;
            var segment = trail.Segments.FirstOrDefault(s => s.Id == id);
            if (segment == null) continue;
            if (!searchPointIds.TryGetValue(segment, out var saved) || saved.Revision != segment.GeometryRevision || saved.Count != segment.Points.Count)
                searchPointIds[segment] = saved = (segment.GeometryRevision, segment.Points.Count, segment.Points.Select(p => p.Id).ToArray());
            result.Add(new(row, trail.Number, id, saved.Ids));
        }
        return result;
    }

    private void ReindexSearch()
    {
        searchNodes = allExplorerRows.Select(r => new SearchNode(r.RowId, r.ParentId, SearchText(r))).ToList();
        searchRevision++;
        searchCancellation?.Cancel();
    }

    private async System.Threading.Tasks.Task ApplySearchFilter()
    {
        // Capture immutable IDs/text on the dispatcher; the worker never touches
        // WPF controls or mutable XML/trail geometry.
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new();
        var token = searchCancellation.Token;
        long revision = searchRevision;
        string query = SearchQuery;
        var rows = allExplorerRows;
        var nodes = searchNodes;
        SearchResult result;
        try
        {
            if (query.Length == 0) result = new(ExplorerHierarchy.Visible(rows), 0);
            else
            {
                SearchStatus.Text = "Searching…";
                var segments = SearchSegments(rows);
                result = await System.Threading.Tasks.Task.Run(() => ComputeSearch(rows, nodes, segments, query, token), token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested || revision != searchRevision || query != SearchQuery) return;
        var keys = ElementsList.SelectedItems.Cast<ExplorerRow>().Select(r => r.RowId).ToHashSet();
        bool prior = updating;
        updating = true;
        try
        {
            ElementsList.ItemsSource = result.Rows;
            foreach (var row in result.Rows.Where(r => keys.Contains(r.RowId))) ElementsList.SelectedItems.Add(row);
            ShowSearchStatus(query, result.Matches);
            FilterQuickMenu();
        }
        finally
        {
            updating = prior;
        }
    }

    private void ShowSearchStatus(string query, int matches) => SearchStatus.Text = query.Length == 0
        ? "Search elements or categories · Ctrl+F" : matches == 0 ? "No matching elements" : $"{matches} matching elements · Esc clears";

    private List<ExplorerRow> FilterExplorer(List<ExplorerRow> rows)
    {
        if (SearchQuery.Length == 0)
        {
            ShowSearchStatus("", 0);
            return ExplorerHierarchy.Visible(rows);
        }
        var result = ComputeSearch(rows, searchNodes, SearchSegments(rows), SearchQuery, default);
        ShowSearchStatus(SearchQuery, result.Matches);
        return result.Rows;
    }

    private static SearchResult ComputeSearch(List<ExplorerRow> rows, List<SearchNode> cachedNodes, List<SearchSegment> segments, string query, System.Threading.CancellationToken token)
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("Search.Worker");
        var pointRows = new List<ExplorerRow>();
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var existing = rows.Where(r => r.Kind == "point").Select(r => r.RowId).ToHashSet();
        foreach (var segment in segments)
        {
            token.ThrowIfCancellationRequested();
            var segmentRow = segment.Row;
            foreach (long id in segment.Points)
            {
                token.ThrowIfCancellationRequested();
                string key = new TrailPointRef(segment.Trail, segment.Segment, id).Key;
                string label = $"◆ Point {id:0000000}";
                string text = label + " " + segmentRow.DocumentName + " " + key;
                if (!words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)) || existing.Contains(segmentRow.DocumentName + "|" + key)) continue;
                pointRows.Add(new(label, segmentRow.DocumentName, segmentRow.Element, segmentRow.Depth + 1, "point", key) { ParentId = segmentRow.RowId });
            }
        }
        token.ThrowIfCancellationRequested();
        var nodes = cachedNodes.Concat(pointRows.Select(r => new SearchNode(r.RowId, r.ParentId, r.Label + " " + r.DocumentName + " " + r.Key))).ToList();
        var keep = ExplorerSearch.Find(nodes, query);
        int matches = nodes.Count(n => words.All(w => n.Text.Contains(w, StringComparison.OrdinalIgnoreCase)));
        var extras = pointRows.ToLookup(r => r.ParentId);
        var filtered = new List<ExplorerRow>();
        foreach (var row in rows)
        {
            if (keep.Contains(row.RowId)) filtered.Add(row);
            filtered.AddRange(extras[row.RowId]);
        }
        return new(ExplorerHierarchy.Prepare(filtered, new HashSet<string>()), matches);
    }

    private void FilterQuickMenu()
    {
        HashSet<string>? keep = null;
        if (SearchQuery.Length > 0)
        {
            var source = quickMenuRows.Select(pair => new SearchNode(pair.Key, pair.Key.Contains('.') ? pair.Key[..pair.Key.LastIndexOf('.')] : null, SearchText(pair.Value)));
            keep = ExplorerSearch.Find(source, SearchQuery);
            foreach (string path in keep.OrderBy(p => p.Count(c => c == '.'))) EnsureQuickMenuNode(path);
        }
        foreach (var pair in quickMenuNodes)
        pair.Value.Visibility = keep == null || keep.Contains(pair.Key) ? Visibility.Visible : Visibility.Collapsed;
    }
}
