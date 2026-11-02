using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using TacoStuffer.Models;
using TacoStuffer.Views;
using TacoStuffer.Services;

namespace TacoStuffer;

public partial class MainWindow
{
    private OverlayEditTool overlayEditTool;
    private OverlayToolbarWindow? overlayToolbar;
    private readonly List<TrailPointRef> mergeEndpoints = [];
    private string? overlaySelectionAnchor;
    private void SetOverlayTool(OverlayEditTool tool)
    {
        if (tool != OverlayEditTool.Select && (workspaceBusy || overlay?.IsRenderingActive != true || !mumbleSession.IsPlayable || !TrySyncXml())) return;
        if (tool != OverlayEditTool.Select) { StopTrailRecording(); StopCheckpointTest(); }
        mergeEndpoints.Clear(); overlayEditTool = tool; trailInsertToolActive = tool == OverlayEditTool.InsertPoint;
        if (tool != OverlayEditTool.Select) trailInsertMap = gameFrame!.MapId;
        worldMarkers?.SetEditTool(tool); UpdateOverlayToolbar();
    }
    private void OpenOverlayToolbar()
    {
        if (overlayToolbar == null)
        {
            overlayToolbar = new OverlayToolbarWindow(preferences.OverlayToolbarPosition);
            overlayToolbar.Command += ToolbarCommand;
            overlayToolbar.InteractionFinished += ReturnToolbarFocusToGame;
            overlayToolbar.PositionRemembered += position =>
            {
                preferences.OverlayToolbarPosition = position;
                if (visualPreviewPreferences != null) visualPreviewPreferences.OverlayToolbarPosition = position;
                try { preferences.Save(PreferenceFile); }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                { StatusText.Text = "Toolbar position retained for this session; could not save it: " + ex.Message; }
            };
            overlayToolbar.Closed += (_, _) => overlayToolbar = null;
        }
        overlayToolbar.Open(); UpdateOverlayToolbar(); UpdateToolbarLayer(); ReturnToolbarFocusToGame();
    }
    private void ReturnToolbarFocusToGame()
    {
        // Run after WPF releases the button/grip capture, never on a timer.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (overlay?.IsRenderingActive != true || workspaceBusy ||
                !NativeGameWindow.IsForeground(gameClient, 0, Environment.ProcessId) ||
                Application.Current.Windows.Cast<Window>().Any(w => w.IsVisible && w.Owner?.IsEnabled == false)) return;
            overlayLayers.Invalidate();
            UpdateToolbarLayer();
            NativeGameWindow.FocusGameInput(gameClient);
        }));
    }
    private readonly OverlayLayerState overlayLayers = new();
    private void UpdateToolbarLayer()
    {
        if (worldMarkers == null || overlay == null) return;
        nint VisibleHandle(Window? window) => window?.IsVisible == true ? new System.Windows.Interop.WindowInteropHelper(window).Handle : 0;
        // Explicit front-to-back order. World/GPU rendering is below every HUD.
        var order = new[] { VisibleHandle(overlay), VisibleHandle(hostedCodeWindow), VisibleHandle(overlayToolbar), VisibleHandle(infoHud), VisibleHandle(copyHud) }
            .Where(h => h != 0).ToArray();
        if (!overlayLayers.Changed(order, NativeGameWindow.EditorOcclusionWindow, NativeGameWindow.HostedGameOverlay)) return;
        nint above = NativeGameWindow.EditorOcclusionWindow;
        foreach (nint handle in order)
        {
            if (!NativeGameWindow.GetWindowRect(handle, out var rect) ||
                !NativeGameWindow.PlaceOverlay(handle, above, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, true, keepAnchor: true))
            { overlayLayers.Invalidate(); return; }
            above = handle;
        }
        bool topmost = NativeGameWindow.OcclusionFor(true) == 0;
        worldMarkers.PresentationAnchor = order.LastOrDefault(h => NativeGameWindow.IsTopmost(h) == topmost);
        worldMarkers.RequestPresentationRepair();
    }
    private void UpdateOverlayToolbar()
    {
        if (overlayToolbar == null || overlay == null) return;
        overlayToolbar.SetState(overlayEditTool, interactionTestMode, checkpointTestActive, overlay.ToolbarButtons());
    }
    private void ToolbarCommand(string command)
    {
        if (Enum.TryParse<OverlayEditTool>(command, out var tool)) { SetOverlayTool(tool); return; }
        if (command == "interactions") overlay?.SetInteractionTests(!interactionTestMode);
        else if (command == "checkpoints") ToggleCheckpointTest();
        else overlay?.InvokeToolbarAction(command);
        UpdateOverlayToolbar();
    }
    private void ApplyOverlaySelection(IReadOnlyList<string> incoming, bool shift, bool control)
    {
        sourceSelectionTimer.Stop();
        var clicked = incoming.ToList();
        if (shift && incoming.Count == 1 && TrailPointRef.Parse(incoming[0]) is { } end && TrailPointRef.Parse(overlaySelectionAnchor ?? "") is { } start && start.Trail == end.Trail && start.Segment == end.Segment)
        {
            var points = editableTrails.FirstOrDefault(t => t.Number == end.Trail)?.Segments.FirstOrDefault(s => s.Id == end.Segment)?.Points;
            if (points != null)
            {
                int a = points.FindIndex(p => p.Id == start.Point), b = points.FindIndex(p => p.Id == end.Point);
                if (a >= 0 && b >= 0) clicked = points.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1).Select(p => new TrailPointRef(end.Trail, end.Segment, p.Id).Key).ToList();
            }
        }
        var current = ElementsList.SelectedItems.Cast<ExplorerRow>().Where(r => r.Kind is "POI" or "point").Select(r => r.Kind == "point" ? r.Key : r.RowId);
        var keys = OverlaySelection.Combine(current, clicked, shift, control);
        if (incoming.Count == 1 && !shift) overlaySelectionAnchor = incoming[0];
        if (keys.Count == 0) { ClearWorldSelection(); return; }
        // Reveal hidden parent branches before selecting the corresponding ListBox rows.
        foreach (string key in keys.Where(k => TrailPointRef.Parse(k) == null))
        {
            int bar = key.IndexOf('|'); if (bar > 0) RevealRow(key[..bar], key[(bar + 1)..]);
        }
        var pointsToReveal = keys.Select(TrailPointRef.Parse).OfType<TrailPointRef>().ToList();
        foreach (var group in pointsToReveal.GroupBy(p => p.Trail))
        {
            var trail = editableTrails.FirstOrDefault(t => t.Number == group.Key); if (trail == null) continue;
            var element = TrailElement(trail); var page = pages.FirstOrDefault(p => p.Xml == element?.Document); if (page != null) RevealTrailPointRows(page.Name, group);
        }
        var rows = allExplorerRows.Where(r => r.Kind is "POI" or "point").Where(r => keys.Contains(r.Kind == "point" ? r.Key : r.RowId)).ToList();
        bool prior = updating; updating = true;
        try { ElementsList.SelectedItems.Clear(); foreach (var row in rows) ElementsList.SelectedItems.Add(row); selected = rows.FirstOrDefault(r => (r.Kind == "point" ? r.Key : r.RowId) == incoming.LastOrDefault()) ?? rows.LastOrDefault(); }
        finally { updating = prior; }
        selectedTrailPoints.Clear(); foreach (var p in pointsToReveal.Where(p => ResolvePoint(p) != null)) selectedTrailPoints.Add(p);
        if (selected != null)
        {
            var page = pages.FirstOrDefault(p => p.Name == selected.DocumentName);
            if (page != null) OpenTab(page.Tab, activateDetached: false);
            ShowAttributes();
            if (page != null && selected?.Element is { } element)
            {
                selectingFromExplorer = true;
                try { SelectElementText(page.Editor, element); }
                finally { selectingFromExplorer = false; }
            }
        }
        else ClearWorldSelection();
        if (selectedTrailPoints.Count > 0 && trailRecording && !trailPaused) PauseTrailSamples();
        UpdateTrailControls();
    }
    private void ApplyWorldTransform(IReadOnlyDictionary<string, Vector3> positions, bool final)
    {
        if (!final) { UpdateLiveCoordinates(EditableTrails.Pivot(positions.Values)); return; }
        if (!TrySyncXml()) return;
        var points = new List<(TrailPointRef Ref, TrailPoint Point, Vector3 Position)>();
        var markers = new List<(System.Xml.Linq.XElement Element, Vector3 Position)>();
        foreach (var item in positions)
        {
            if (!EditableTrails.Finite(item.Value)) return;
            if (TrailPointRef.Parse(item.Key) is { } point)
            { if (item.Value == Vector3.Zero || ResolvePoint(point) is not { } resolved) return; points.Add((point, resolved, item.Value)); }
            else
            {
                int bar = item.Key.IndexOf('|'); if (bar < 0) return;
                var page = pages.FirstOrDefault(p => p.Name == item.Key[..bar]); var element = page?.Xml.Descendants("POI").FirstOrDefault(e => ElementKey(e) == item.Key[(bar + 1)..]);
                if (element == null) return; markers.Add((element, item.Value));
            }
        }
        sourceSelectionTimer.Stop();
        foreach (var p in points) { editableTrails.First(t => t.Number == p.Ref.Trail).Segments.First(s => s.Id == p.Ref.Segment).ResetResampling(); p.Point.Position = p.Position; }
        foreach (var marker in markers) SetPosition(marker.Element, marker.Position);
        if (markers.Count > 0) WritePages(); else { worldMarkers?.InvalidateTrails(); BuildCheckpointNodes(); SyncOverlaySelection(); }
        UpdateLiveCoordinates(EditableTrails.Pivot(positions.Values)); QueueHistoryCheckpoint("Move overlay selection");
    }
    private void EditOverlayPoint(TrailPointRef point)
    {
        if (workspaceBusy || !mumbleSession.IsPlayable || !TrySyncXml()) return;
        var trail = editableTrails.FirstOrDefault(t => t.Number == point.Trail); if (trail == null) return;
        try
        {
            if (overlayEditTool == OverlayEditTool.MergeSegments)
            {
                if (!TrailTopology.Endpoint(trail, point)) throw new InvalidOperationException("Choose a segment endpoint.");
                if (mergeEndpoints.Contains(point)) { mergeEndpoints.Remove(point); }
                else if (mergeEndpoints.Count < 2) mergeEndpoints.Add(point);
                ApplyOverlaySelection(mergeEndpoints.Select(p => p.Key).ToList(), false, false);
                ToolMessage(mergeEndpoints.Count + " of 2 endpoints chosen. Press Enter to merge; Esc cancels."); return;
            }
            if (overlayEditTool == OverlayEditTool.ReverseSegment)
            {
                FlushHistoryCheckpoint(); TrailTopology.Reverse(trail, point);
                RefreshExplorer(); ApplyOverlaySelection([point.Key], false, false);
                CommitHistoryCheckpoint("Reverse trail segment");
                ToolMessage("Segment direction reversed. Click another point to reverse its segment, or Esc to exit.");
                return;
            }
            if (overlayEditTool is not (OverlayEditTool.RemovePoint or OverlayEditTool.SplitSegment)) return;
            FlushHistoryCheckpoint(); TrailTopology.Remove(trail, point, overlayEditTool == OverlayEditTool.SplitSegment);
            selectedTrailPoints.Remove(point); selected = null; RefreshExplorer();
            CommitHistoryCheckpoint(overlayEditTool == OverlayEditTool.SplitSegment ? "Split trail segment" : "Remove trail point");
            ToolMessage("Trail updated. Tool remains active; Esc exits.");
        }
        catch (InvalidOperationException ex) { ToolMessage(ex.Message); }
    }
    private void MergeOverlaySegments()
    {
        if (overlayEditTool != OverlayEditTool.MergeSegments || mergeEndpoints.Count != 2 || workspaceBusy || !TrySyncXml()) return;
        try
        {
            var a = mergeEndpoints[0]; var b = mergeEndpoints[1];
            var hierarchy = allExplorerRows.Where(r => r.Kind == "Trail").Select(r => TrailFor(r.Element))
                .OfType<EditableTrail>().Distinct().ToList();
            FlushHistoryCheckpoint(); var seam = TrailTopology.Merge(hierarchy, a, b); mergeEndpoints.Clear(); selectedTrailPoints.Clear(); selected = null;
            RefreshExplorer(); ApplyOverlaySelection([seam.Key], false, false); CommitHistoryCheckpoint("Merge trail segments"); ToolMessage("Segments merged in Outliner order into Trail " + seam.Trail + ". Choose another pair, or Esc to exit.");
        }
        catch (InvalidOperationException ex) { ToolMessage(ex.Message); }
    }
    private void ToolMessage(string text) { StatusText.Text = text; overlay?.TrailResult(text); worldMarkers?.ShowResetNotification(text); }
}
