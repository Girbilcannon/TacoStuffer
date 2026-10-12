using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TacoStuffer.Services;
using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private readonly List<EditableTrail> editableTrails = [];
    private readonly HashSet<TrailPointRef> selectedTrailPoints = [];
    private long nextTrailNumber = 1;
    private Window? deleteOwner;
    private Window DeleteDialogOwner => deleteOwner ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible) ?? (overlay?.IsVisible == true ? overlay : this);
    private void WithDeleteOwner(Window owner, Action action)
    {
        var previous = deleteOwner;
        deleteOwner = owner;
        try
        {
            action();
        }
        finally
        {
            deleteOwner = previous;
        }
    }

    private readonly TrailSampler trailSampler = new();
    private readonly HashSet<string> initializedTrailSegments = [];
    private EditableTrail? recordingTrail;
    private TrailSegment? recordingSegment;
    private bool trailRecording, trailPaused;
    private long trailSidebarUpdate;
    private string? trailResumeNotice;
    private Dictionary<string, EditableTrail>? editableTrailIndex;
    private EditableTrail? TrailFor(XElement? e)
    {
        editableTrailIndex ??= editableTrails.GroupBy(t => t.Guid).ToDictionary(g => g.Key, g => g.First());
        return e == null ? null : editableTrailIndex.GetValueOrDefault(Value(e, "GUID"));
    }

    private readonly Dictionary<TrailSegment, (List<TrailPoint> Source, int Count, Dictionary<long, TrailPoint> Index)> pointLookups = [];
    private TrailPoint? ResolvePoint(TrailPointRef reference)
    {
        var segment = editableTrails.FirstOrDefault(t => t.Number == reference.Trail)?.Segments.FirstOrDefault(s => s.Id == reference.Segment);
        if (segment == null) return null;
        if (!pointLookups.TryGetValue(segment, out var lookup) || !ReferenceEquals(lookup.Source, segment.Points) || lookup.Count != segment.Points.Count)
        {
            lookup = (segment.Points, segment.Points.Count, segment.Points.ToDictionary(p => p.Id));
            pointLookups[segment] = lookup;
        }
        return lookup.Index.GetValueOrDefault(reference.Point);
    }

    private XElement? TrailElement(EditableTrail trail) => pages.SelectMany(p => p.Xml.Descendants("Trail")).FirstOrDefault(e => Value(e, "GUID") == trail.Guid);

    private void NewTrail(string xml, string category)
    {
        PollGame();
        if (overlay == null || !mumbleSession.IsPlayable || gameFrame == null || !TrySyncXml()) return;
        var page = pages.FirstOrDefault(p => p.Name == xml);
        if (page == null || !page.Xml.Descendants("MarkerCategory").Any(c => CategoryFullPath(c) == category && c.Attributes().All(a => !a.Name.LocalName.Equals("isSeparator", StringComparison.OrdinalIgnoreCase)))) return;
        StopTrailRecording();
        long number = Math.Max(nextTrailNumber, editableTrails.Select(t => t.Number + 1).DefaultIfEmpty(1).Max());
        string name = $"trail_{number:0000}";
        while (pages.Any(p => p.Xml.Descendants("Trail").Any(t => Value(t, "name") == name || Value(t, "trailData") == ProjectAssetPaths.FilePath(assetPaths.TrailFiles, name + ".trl"))) || (projectFolder != null && File.Exists(Path.Combine(projectFolder, ProjectAssetPaths.FilePath(assetPaths.TrailFiles, name + ".trl")))))
        {
            number++;
            name = $"trail_{number:0000}";
        }
        var trail = new EditableTrail { Number = number, MapId = gameFrame.MapId, Guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) };
        editableTrails.Add(trail);
        editableTrailIndex = null;
        nextTrailNumber = number + 1;
        recordingTrail = trail;
        recordingSegment = trail.AddSegment();
        recordingSegment.Smoothness = trailSampler.Smoothness;
        var parent = page.Xml.Root!.Element("POIs");
        if (parent == null)
        {
            parent = new XElement("POIs");
            page.Xml.Root.Add(parent);
        }
        var element = new XElement("Trail", new XAttribute("name", name), new XAttribute("DisplayName", $"Trail {number:0000}"), new XAttribute("GUID", trail.Guid), new XAttribute("MapID", trail.MapId), new XAttribute("type", category), new XAttribute("trailData", ProjectAssetPaths.FilePath(assetPaths.TrailFiles, name + ".trl")));
        parent.Add(element);
        WritePages();
        var row = RevealRow(xml, ElementKey(element));
        if (row != null) FocusRow(row, activateDetached: false);
        overlay?.SetTarget(xml, category);
        UpdateTrailControls();
        overlay?.TrailResult("Trail created. Start recording when ready.");
    }

    private void ChooseTrail(long number, long? segment)
    {
        var trail = editableTrails.FirstOrDefault(t => t.Number == number);
        if (trail == null) return;
        if (trailRecording && (recordingTrail != trail || (segment.HasValue && recordingSegment?.Id != segment))) StopTrailRecording();
        var previous = recordingTrail;
        recordingTrail = trail;
        if (segment.HasValue) recordingSegment = trail.Segments.FirstOrDefault(s => s.Id == segment);
        else if (previous != trail || recordingSegment == null) recordingSegment = trail.Segments.LastOrDefault();
        UpdateTrailControls();
    }

    private void StartTrailRecording()
    {
        PollGame();
        if (overlay == null || !mumbleSession.IsPlayable || gameFrame == null || recordingTrail == null || !TrySyncXml()) return;
        if (gameFrame.MapId != recordingTrail.MapId)
        {
            overlay.TrailResult("Return to map " + recordingTrail.MapId + " to continue this trail.");
            return;
        }
        if (trailRecording && !trailPaused) return;
        BeginRecordingHistory();
        if (recordingSegment == null)
        {
            recordingSegment = recordingTrail.AddSegment();
            recordingSegment.Smoothness = trailSampler.Smoothness;
        }
        trailSampler.Smoothness = recordingSegment.Smoothness;
        trailSampler.Reset();
        trailRecording = true;
        trailPaused = false;
        trailResumeNotice = null;
        SampleTrail(gameFrame);
        UpdateTrailControls();
    }

    private void PauseTrailRecording()
    {
        if (!trailRecording) return;
        if (trailPaused)
        {
            PollGame();
            if (!mumbleSession.IsPlayable || gameFrame?.MapId != recordingTrail?.MapId) return;
            BeginRecordingHistory();
            if (recordingSegment != null) trailSampler.Smoothness = recordingSegment.Smoothness;
            trailSampler.Reset();
            trailPaused = false;
            SampleTrail(gameFrame!);
        }
        else
        {
            PauseTrailSamples();
            RefreshExplorer();
        }
        UpdateTrailControls();
    }

    private void NewTrailSegment()
    {
        if (!trailRecording || !trailPaused || recordingTrail == null) return;
        PollGame();
        if (!mumbleSession.IsPlayable || gameFrame?.MapId != recordingTrail.MapId) return;
        BeginRecordingHistory();
        recordingSegment = recordingTrail.AddSegment();
        recordingSegment?.ResetResampling();
        if (recordingSegment != null) recordingSegment.Smoothness = trailSampler.Smoothness;
        trailSampler.Reset();
        trailPaused = false;
        SampleTrail(gameFrame!);
        RefreshExplorer();
        UpdateTrailControls();
    }

    private void StopTrailRecording()
    {
        FlushTrailSample();
        trailSampler.Reset();
        trailRecording = false;
        trailPaused = false;
        CompleteRecordingHistory();
        UpdateTrailControls();
    }

    private void FlushTrailSample()
    {
        if (!trailRecording || trailPaused || recordingTrail == null || recordingSegment == null || trailSampler.Latest is not { } position || !recordingSegment.Add(position)) return;
        worldMarkers?.InvalidateTrails();
        overlay?.AppendTrailPoint(recordingTrail.Number, recordingSegment, recordingSegment.Points[^1]);
    }

    private void PauseTrailSamples()
    {
        FlushTrailSample();
        trailPaused = true;
        trailSampler.Reset();
        CompleteRecordingHistory();
    }

    private void SetTrailSmoothness(int level)
    {
        if (trailRecording && !trailPaused)
        {
            FlushTrailSample();
            trailSampler.Smoothness = Math.Clamp(level, 1, 10);
            trailSampler.Reset();
            if (recordingSegment != null)
            {
                recordingSegment.ResetResampling();
                recordingSegment.Smoothness = trailSampler.Smoothness;
            }
            return;
        }
        if (SelectedSegment() is { } segment)
        {
            if (segment.Smoothness == level) return;
            try
            {
                using var timing = WorkspacePerformance.Measure("Trail.ResampleSelectedSegment");
                EditableTrails.Resample(segment, level);
                selectedTrailPoints.Clear();
                RefreshResampledSegment(segment);
                QueueHistoryCheckpoint("Change segment smoothness");
                StatusText.Text = "Segment smoothness updated. Save to retain edits.";
            }
            catch (InvalidOperationException ex)
            {
                Notice("Segment smoothness", ex.Message);
            }
            finally
            {
                UpdateTrailControls();
            }
            return;
        }
        // Trail/point selections do not own a smoothness setting.
    }

    private void RefreshResampledSegment(TrailSegment segment)
    {
        if (selected == null || TrailFor(selected.Element) is not { } trail) return;
        var reference = new TrailSegmentRef(trail.Number, segment.Id);
        string id = selected.DocumentName + $"|segment|{trail.Number}|{segment.Id}";
        var keys = ElementsList.SelectedItems.Cast<ExplorerRow>().Where(r => r.Kind != "point").Select(r => r.RowId).ToHashSet();
        var rows = new List<ExplorerRow>();
        foreach (var row in allExplorerRows)
        {
            if (row.ParentId == id && row.Kind == "point") continue;
            if (row.RowId != id)
            {
                rows.Add(row);
                continue;
            }
            var changed = row with { Label = $"Segment {segment.Id:00000} · {segment.Points.Count} points", HasChildren = segment.Points.Count > 0 };
            rows.Add(changed);
            if (!collapsedElements.Contains(id) || SearchQuery.Length > 0)
                foreach (var point in segment.Points)
                rows.Add(new($"◆ Point {point.Id:0000000}", row.DocumentName, row.Element, row.Depth + 1, "point", new TrailPointRef(trail.Number, segment.Id, point.Id).Key));
        }
        bool prior = updating;
        updating = true;
        try
        {
            allExplorerRows = ExplorerHierarchy.Prepare(rows, collapsedElements);
            ReindexSearch();
            ElementsList.ItemsSource = FilterExplorer(allExplorerRows);
            foreach (var row in ElementsList.Items.Cast<ExplorerRow>().Where(r => keys.Contains(r.RowId))) ElementsList.SelectedItems.Add(row);
            selected = allExplorerRows.FirstOrDefault(r => r.RowId == id) ?? selected;
            overlay?.RefreshSegment(reference);
        }
        finally
        {
            updating = prior;
        }
        worldMarkers?.InvalidateTrails();
        ShowAttributes();
    }

    private TrailSegment? SelectedSegment()
    {
        if (selected?.Kind != "segment" || TrailFor(selected.Element) is not { } trail || !long.TryParse(selected.Key.Split('|').Last(), out long id)) return null;
        return trail.Segments.FirstOrDefault(s => s.Id == id);
    }

    private void SelectTrailSegment(TrailSegmentRef reference)
    {
        sourceSelectionTimer.Stop();
        var trail = editableTrails.FirstOrDefault(t => t.Number == reference.Trail);
        var element = trail == null ? null : TrailElement(trail);
        var page = pages.FirstOrDefault(p => element?.Document == p.Xml);
        if (page == null) return;
        if (trailRecording && !trailPaused)
        {
            PauseTrailSamples();
            RefreshExplorer();
        }
        var row = RevealRow(page.Name, $"segment|{reference.Trail}|{reference.Segment}");
        if (row != null) FocusRow(row, highlightXml: false, activateDetached: false);
    }

    private void ShowTrailSegmentAttributes()
    {
        selectedTrailPoints.Clear();
        var segment = SelectedSegment();
        if (segment == null) return;
        if (trailRecording && !trailPaused) PauseTrailSamples();
        if (TrailFor(selected!.Element) is { } trail) ChooseTrail(trail.Number, segment.Id);
        AttributesHost.Children.Clear();
        SelectionTitle.Text = $"Segment {segment.Id:00000}";
        CategoryPath.Text = $"{segment.Points.Count} points · smoothness {segment.Smoothness}/10";
        AttributesHost.Children.Add(new TextBlock { Text = "This selection applies only to the segment. Expand it and select points explicitly to edit coordinates. The overlay slider adjusts this segment's density while stopped or paused.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush") });
        SyncOverlaySelection();
        UpdateTrailControls();
    }

    private void UpdateTrailControls()
    {
        if (overlay == null) return;
        bool running = trailRecording && !trailPaused;
        var segment = running ? recordingSegment : SelectedSegment();
        overlay.SetTrailSmoothnessContext(segment?.Smoothness, !running);
        overlay.SetTrailState(recordingTrail?.Number, recordingSegment?.Id, recordingSegment?.Points.Count > 0, trailRecording, trailPaused, recordingTrail == null || gameFrame?.MapId == recordingTrail.MapId);
    }

    private void SampleTrail(MumbleSnapshot frame)
    {
        if (!trailRecording || trailPaused || recordingTrail == null || recordingSegment == null) return;
        if (frame.MapId != recordingTrail.MapId || !mumbleSession.IsPlayable)
        {
            PauseTrailSamples();
            trailResumeNotice = "Recording paused: map/session changed. Resume on map " + recordingTrail.MapId + ".";
            overlay?.TrailResult(trailResumeNotice);
            UpdateTrailControls();
            Dispatcher.BeginInvoke(new Action(() => RefreshExplorer()));
            return;
        }
        if (!trailSampler.ShouldRecord(frame.AvatarPosition) || !recordingSegment.Add(frame.AvatarPosition)) return;
        worldMarkers?.InvalidateTrails();
        overlay?.AppendTrailPoint(recordingTrail.Number, recordingSegment, recordingSegment.Points[^1]);
        if (Environment.TickCount64 - trailSidebarUpdate > 500)
        {
            trailSidebarUpdate = Environment.TickCount64;
            overlay?.TrailResult($"Trail {recordingTrail.Number:0000} · Segment {recordingSegment.Id:00000} · {recordingSegment.Points.Count} points");
        }
    }

    private void SelectTrailPoints(IReadOnlyList<TrailPointRef> points)
    {
        sourceSelectionTimer.Stop();
        bool justPaused = trailRecording && !trailPaused;
        if (justPaused)
        {
            PauseTrailSamples();
            UpdateTrailControls();
            RefreshExplorer();
        }
        selectedTrailPoints.Clear();
        foreach (var p in points.Where(p => ResolvePoint(p) != null)) selectedTrailPoints.Add(p);
        if (selectedTrailPoints.Count == 0)
        {
            ClearWorldSelection();
            return;
        }
        var first = selectedTrailPoints.First();
        ChooseTrail(first.Trail, first.Segment);
        var trail = editableTrails.First(t => t.Number == first.Trail);
        var e = TrailElement(trail);
        var page = pages.FirstOrDefault(p => e?.Document == p.Xml);
        if (page == null) return;
        var rows = RevealTrailPointRows(page.Name, selectedTrailPoints);
        bool prior = updating;
        updating = true;
        try
        {
            ElementsList.SelectedItems.Clear();
            foreach (var row in rows) ElementsList.SelectedItems.Add(row);
            selected = rows.LastOrDefault();
        }
        finally
        {
            updating = prior;
        }
        if (selected != null)
        {
            OpenTab(page.Tab, activateDetached: false);
            ShowTrailPointAttributes();
        }
        else RefreshWorldMarkers();
    }

    private void ShowTrailPointAttributes()
    {
        if (trailRecording && !trailPaused)
        {
            PauseTrailSamples();
            UpdateTrailControls();
        }
        selectedTrailPoints.Clear();
        foreach (var row in ElementsList.SelectedItems.Cast<ExplorerRow>()) if (TrailPointRef.Parse(row.Key) is { } pointRef && ResolvePoint(pointRef) != null) selectedTrailPoints.Add(pointRef);
        if (selectedTrailPoints.FirstOrDefault() is { } focus) ChooseTrail(focus.Trail, focus.Segment);
        AttributesHost.Children.Clear();
        SelectionTitle.Text = selectedTrailPoints.Count + " trail points";
        CategoryPath.Text = "Editable coordinates · Mumble Y-up meters";
        var positions = selectedTrailPoints.Select(ResolvePoint).OfType<TrailPoint>().Select(p => p.Position).ToList();
        var pivot = EditableTrails.Pivot(positions);
        foreach (var pair in new[] { ("xpos", pivot.X), ("ypos", pivot.Y), ("zpos", pivot.Z) }) Field(pair.Item1, pair.Item2.ToString("R", CultureInfo.InvariantCulture), value =>
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || !float.IsFinite(v)) return;
            var now = EditableTrails.Pivot(selectedTrailPoints.Select(ResolvePoint).OfType<TrailPoint>().Select(p => p.Position));
            var delta = pair.Item1 == "xpos" ? new Vector3(v - now.X, 0, 0) : pair.Item1 == "ypos" ? new Vector3(0, v - now.Y, 0) : new Vector3(0, 0, v - now.Z);
            ApplyPointTransform(selectedTrailPoints.ToDictionary(p => p.Key, p => ResolvePoint(p)!.Position + delta), true);
        });
        var delete = new Button { Content = "Delete selected points", Margin = new Thickness(0, 12, 0, 0) };
        delete.Click += (_, _) => DeleteTrailPoints(selectedTrailPoints.ToList());
        AttributesHost.Children.Add(delete);
        AttributesHost.Children.Add(new TextBlock { Text = "Shift-click a range in one segment; Ctrl-click to combine points. Multiple points move together; rotation rings use their average pivot. Points and segments have no XML attributes.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 12, 0, 0) });
        SyncOverlaySelection();
    }

    private void ApplyPointTransform(IReadOnlyDictionary<string, Vector3> positions, bool final)
    {
        var resolved = positions.Select(p => (Ref: TrailPointRef.Parse(p.Key), Value: p.Value)).Where(p => p.Ref != null).ToList();
        if (resolved.Any(p => !EditableTrails.Finite(p.Value) || p.Value == Vector3.Zero))
        {
            if (final) Notice("Point position", "A trail point cannot use TacO's zero-position segment separator.");
            return;
        }
        if (final)
        {
            foreach (var p in resolved)
            if (ResolvePoint(p.Ref!) is { } point)
            {
                editableTrails.First(t => t.Number == p.Ref!.Trail).Segments.First(s => s.Id == p.Ref!.Segment).ResetResampling();
                point.Position = p.Value;
            }
            RefreshExplorer();
            StatusText.Text = "Trail points updated. Save to retain editable data.";
        }
        else UpdateLiveCoordinates(EditableTrails.Pivot(resolved.Select(p => p.Value)));
    }

    private void UpdateLiveCoordinates(Vector3 position)
    {
        bool prior = updating;
        updating = true;
        try
        {
            foreach (var input in AttributesHost.Children.OfType<TextBox>()) if (input.Tag is string name && name is ("xpos" or "ypos" or "zpos")) input.Text = (name == "xpos" ? position.X : name == "ypos" ? position.Y : position.Z).ToString("R", CultureInfo.InvariantCulture);
        }
        finally
        {
            updating = prior;
        }
    }

    private void DeleteTrailPoints(IReadOnlyList<TrailPointRef> points)
    {
        if (trailRecording)
        {
            overlay?.TrailResult("Pause/stop recording before editing points.");
            if (!trailPaused) return;
        }
        if (points.Count == 0) return;
        var confirm = new ConfirmActionWindow("Delete trail points", "Delete " + points.Count + " selected points? Remaining neighbors connect directly.") { Owner = DeleteDialogOwner, Topmost = DeleteDialogOwner.Topmost };
        if (confirm.ShowDialog() != true) return;
        foreach (var r in points)
        if (editableTrails.FirstOrDefault(t => t.Number == r.Trail)?.Segments.FirstOrDefault(s => s.Id == r.Segment) is { } segment)
        {
            segment.ResetResampling();
            segment.Points.RemoveAll(p => p.Id == r.Point);
        }
        selectedTrailPoints.Clear();
        selected = null;
        RefreshExplorer();
        ClearWorldSelection();
    }

    private void DeleteTrailSegments()
    {
        if (trailRecording && !trailPaused)
        {
            Notice("Segment deletion", "Pause or stop recording first.");
            return;
        }
        var targets = ElementsList.SelectedItems.Cast<ExplorerRow>().Where(r => r.Kind == "segment").Select(r => (Trail: TrailFor(r.Element), Id: long.Parse(r.Key.Split('|').Last()))).ToList();
        if (targets.Count == 0) return;
        var confirm = new ConfirmActionWindow("Delete segments", $"Delete {targets.Count} selected segments and the points they contain? Other segments remain disconnected.") { Owner = DeleteDialogOwner, Topmost = DeleteDialogOwner.Topmost };
        if (confirm.ShowDialog() != true) return;
        foreach (var target in targets)
        {
            if (target.Trail == recordingTrail && recordingSegment?.Id == target.Id)
            {
                StopTrailRecording();
                recordingSegment = null;
            }
            target.Trail?.Segments.RemoveAll(s => s.Id == target.Id);
        }
        selectedTrailPoints.Clear();
        selected = null;
        RefreshExplorer();
        ClearWorldSelection();
        UpdateTrailControls();
    }

    private void DeleteOverlayAsset(OverlayAsset asset)
    {
        var row = RevealRow(asset.DocumentName, asset.Key);
        if (row != null)
        {
            FocusRow(row, activateDetached: false);
            DeleteSelection();
        }
    }

    private bool workspaceBusy;
    private async void ImportEditableTrail(ExplorerRow row)
    {
        if (workspaceBusy || !TrySyncXml() || projectFolder == null || projectFile == null) return;
        if (trailRecording)
        {
            Notice("Make Editable", "Stop recording before converting a trail.");
            return;
        }
        row = RevealRow(row.DocumentName, row.Key) ?? row;
        if (row.Element == null || TrailFor(row.Element) != null) return;
        workspaceBusy = true;
        IsEnabled = false;
        var currentOverlay = overlay;
        if (currentOverlay != null) currentOverlay.IsEnabled = false;
        StatusText.Text = "Converting trail to editable points…";
        try
        {
            var categories = CategoryValues();
            string path = TacoImporter.PackPath(AttributeRules.Effective(row.Element, "trailData", "", categories));
            string guid = Value(row.Element, "GUID");
            var tags = pages.SelectMany(p => p.Xml.Descendants("Trail")).ToList();
            if (guid.Length == 0 || tags.Count(t => Value(t, "GUID") == guid) > 1)
            {
                var used = pages.SelectMany(p => p.Xml.Descendants().Attributes("GUID")).Select(a => a.Value).ToHashSet();
                do
                {
                    guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                }
                while (used.Contains(guid));
            }
            string sourceFile = Path.Combine(projectFolder, path);
            long number = nextTrailNumber;
            string conversionGuid = guid;
            var trail = await System.Threading.Tasks.Task.Run(() => TacoImporter.ReadTrail(File.ReadAllBytes(sourceFile), number, conversionGuid));
            string map = AttributeRules.Effective(row.Element, "MapID", "", categories);
            if (map.Length > 0 && (!uint.TryParse(map, out uint id) || id != trail.MapId)) throw new InvalidOperationException("XML/TRL MapID mismatch. Correct the reference before converting this trail.");
            string target = path;
            if (tags.Count(t => AttributeRules.Effective(t, "trailData", "", categories).Replace('\\', '/') == path) > 1)
            {
                target = ProjectAssetPaths.FilePath(assetPaths.TrailFiles, $"trail_{trail.Number:0000}.trl");
                while (File.Exists(Path.Combine(projectFolder, target)) || tags.Any(t => Value(t, "trailData") == target)) target = ProjectAssetPaths.FilePath(assetPaths.TrailFiles, Guid.NewGuid().ToString("N") + ".trl");
            }
            SetValue(row.Element, "GUID", guid);
            SetValue(row.Element, "MapID", trail.MapId.ToString(CultureInfo.InvariantCulture));
            SetValue(row.Element, "trailData", target);
            editableTrails.Add(trail);
            editableTrailIndex = null;
            nextTrailNumber++;
            WritePages();
            QueueHistoryCheckpoint("Make trail editable");
            StatusText.Text = "Saving editable trail and project…";
            var snapshot = CurrentSnapshot();
            string file = projectFile;
            // Recording and mutations are blocked until this immutable document
            // snapshot and its trail sources have finished their atomic save.
            await System.Threading.Tasks.Task.Run(() => ProjectStore.Save(file, snapshot));
            savedFingerprint = WorkspaceFingerprint();
            StatusText.Text = "Trail is editable. Its JSON source was saved in EDITABLE_TRAILS; original TRL retained.";
        }
        catch (Exception ex) when (ProjectError(ex))
        {
            Notice("Make Editable", ex.Message);
        }
        finally
        {
            workspaceBusy = false;
            IsEnabled = true;
            if (currentOverlay != null) currentOverlay.IsEnabled = true;
        }
    }
}
