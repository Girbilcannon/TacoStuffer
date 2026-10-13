using System.Windows;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TacoStuffer.Models;
using TacoStuffer.Services;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private readonly MumbleLinkReader mumble = new();
    private readonly MumbleSession mumbleSession = new();
    private readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool renderingWorld;
    private WorldMarkerWindow? worldMarkers;
    private GameWindowEvents? gameEvents;
    private GameClientWindow gameClient;
    private MumbleSnapshot? gameFrame;
    private long lastProcessProbe;
    private bool gameMonitorClosed;
    private bool previousOverlayDeleteDown;
    private void InitializeGameConnection()
    {
        gameEvents = new GameWindowEvents();
        gameEvents.Changed += () =>
        {
            if (!gameMonitorClosed) Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    RequestPreviewPresentation();
                    UpdateOverlayVisibility();
                }));
        };
        Activated += (_, _) => RequestPreviewPresentation();
        PreviewMouseDown += (_, _) => RequestPreviewPresentation();
        gameTimer.Tick += (_, _) => PollGame();

        Closed += (_, _) =>
        {
            gameMonitorClosed = true;
            gameTimer.Stop();
            StopWorldRendering();
            worldMarkers?.Close();
            gameEvents.Dispose();
            mumble.Dispose();
        };
        PollGame();
        gameTimer.Start();
    }

    private bool previewPresentationQueued;
    // Editor activation/clicks can disturb native overlay stacking. Coalesce a
    // presentation repair on the UI dispatcher; do not rebuild pack geometry just
    // to restore visibility above the hosted game.
    private void RequestPreviewPresentation()
    {
        if (gameMonitorClosed || previewPresentationQueued || worldMarkers == null) return;
        previewPresentationQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            previewPresentationQueued = false;
            if (gameMonitorClosed) return;
            worldMarkers?.RequestPresentationRepair();
            UpdateOverlayVisibility();
        }));
    }

    private void StartWorldRendering()
    {
        if (renderingWorld) return;
        CompositionTarget.Rendering += RenderWorld;
        renderingWorld = true;
    }

    private void StopWorldRendering()
    {
        if (!renderingWorld) return;
        CompositionTarget.Rendering -= RenderWorld;
        renderingWorld = false;
    }

    private void RenderWorld(object? sender, EventArgs e) => UpdateWorldProjection();

    private void PollGame()
    {
        if (gameMonitorClosed) return;
        PollGameShortcuts(overlay?.IsVisible == true && NativeGameWindow.IsGameInputActive(gameClient) && mumbleSession.IsPlayable);
        long now = Environment.TickCount64;
        gameFrame = mumble.Read();
        if (gameFrame != null || now - lastProcessProbe >= 1000)
        {
            var hosted = GameViewport.Client;
            gameClient = hosted.Handle != 0 && (gameFrame == null || gameFrame.ProcessId == (uint)hosted.ProcessId)
                ? hosted : GameClientWindow.Find(gameFrame?.ProcessId ?? 0);
            lastProcessProbe = now;
        }
        if (gameClient.ProcessId == 0)
        {
            mumbleSession.Reset();
            // Keep the named mapping available for a game that starts later.
            // The reader is disposed when Taco Stuffer closes or an actual read fails.
            gameFrame = null;
        }
        else mumbleSession.Observe(gameFrame, now);
        bool ready = mumbleSession.IsPlayable && gameFrame?.ProcessId == (uint)gameClient.ProcessId && gameClient.Handle != 0;
        GameConnectionDot.Foreground = ready ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TortillaBrush");
        GameConnectionText.Text = ready ? "GW2 · Map " + gameFrame!.MapId : gameClient.ProcessId == 0 ? "GW2 disconnected" : "GW2 · waiting for map";
        GameConnectionText.ToolTip = ready ? gameFrame!.CharacterName + " · MumbleLink connected" : mumble.Error ?? "Waiting for fresh character and map telemetry.";
        // Connection status and overlay availability use the same readiness gate.
        // Explain a missing authoring target on click instead of leaving a connected game locked out.
        OverlayButton.IsEnabled = OverlayMenuItem.IsEnabled = ready;
        string overlayHint = !ready ? mumble.Error ?? "Enter a playable GW2 map and wait for fresh telemetry."
            : projectFile == null ? "GW2 connected. Create or open a .cilantro project before recording."
            : pages.Count == 0 ? "GW2 connected. Add an XML document to this project before recording."
            : "Open the game overlay. Visible while GW2 or Taco Stuffer is active.";
        OverlayButton.ToolTip = OverlayMenuItem.ToolTip = overlayHint;
        if (trailRecording && (!ready || gameFrame?.MapId != recordingTrail?.MapId) && !trailPaused)
        {
            PauseTrailSamples();
            RefreshExplorer();
        }
        PollPlayInApp(ready);
        UpdateTrailControls();
        UpdateOverlayVisibility();
    }

    private void UpdateOverlayVisibility()
    {
        if (gameMonitorClosed || overlay == null) return;
        if (gameClient.ProcessId == 0 || gameClient.ProcessId != overlay.BoundProcessId)
        {
            overlay.Close();
            return;
        }
        overlay.UpdateVisibility(gameClient, mumbleSession.IsPlayable && projectFile != null, gameFrame, Environment.ProcessId, GameViewport.IsAttached);
        UpdateHostedCodeVisibility();
        UpdateWorldProjection();
    }

    private void UpdateWorldProjection()
    {
        if (gameMonitorClosed || overlay == null || worldMarkers == null) return;
        if (OwnedWindows.Cast<Window>().Any(w => w != hostedCodeWindow && w.IsActive))
        {
            worldMarkers.Conceal();
            return;
        }
        PollGameShortcuts(overlay.IsVisible && NativeGameWindow.IsGameInputActive(gameClient) && mumbleSession.IsPlayable);
        if (!overlay.IsVisible || !mumbleSession.IsPlayable || !gameClient.TryBounds(out _) || !NativeGameWindow.IsForeground(gameClient, overlay.Handle, Environment.ProcessId))
        {
            worldMarkers.Conceal();
            return;
        }
        if (worldSceneMap != gameFrame?.MapId) RefreshWorldMarkers();
        bool deleteDown = NativeGameWindow.DeleteDown;
        bool deletePressed = deleteDown && !previousOverlayDeleteDown;
        previousOverlayDeleteDown = deleteDown;
        if (deletePressed && NativeGameWindow.IsGameInputActive(gameClient)) overlay.HandleDelete();
        var camera = mumble.Read();
        bool sameSession = camera != null && camera.ProcessId == (uint)overlay.BoundProcessId && camera.MapId == gameFrame?.MapId && camera.MapType == gameFrame?.MapType && camera.CharacterName == gameFrame?.CharacterName;
        if (sameSession) SampleTrail(camera!);
        worldMarkers.InteractiveExclusionHandle = hostedCodeWindow?.IsVisible == true ? new System.Windows.Interop.WindowInteropHelper(hostedCodeWindow).Handle : 0;
        worldMarkers.Update(gameClient, sameSession, camera, overlay.Handle);
    }

    private void RefreshOverlayWorkspace()
    {
        if (overlay == null) return;
        var assets = pages.SelectMany(page => page.Xml.Descendants().Where(e => e.Name.LocalName is "POI" or "Trail").Select(e =>
            new OverlayAsset(page.Name, ElementKey(e), Value(e, "DisplayName", Value(e, "name", AssetStem(e))), e.Name.LocalName, Value(e, "type"), Value(e, "MapID", "unspecified")))).ToList();
        var targets = pages.SelectMany(page => page.Xml.Descendants("MarkerCategory").Select(c => new OverlayCategory(page.Name, CategoryFullPath(c), c.Attributes().Any(a => a.Name.LocalName.Equals("isSeparator", StringComparison.OrdinalIgnoreCase))))).ToList();
        overlay.SetEditableTrails(editableTrails);
        overlay.SetWorkspace(pages.Select(p => p.Name).ToList(), targets, assets);
        RefreshWorldMarkers();
        UpdateTrailControls();
    }

    private readonly HashSet<string> hiddenPreviewCategories = [];
    private List<WorldMarker> previewMarkers = [];
    private List<WorldTrail> previewTrails = [];
    private bool PreviewCategoryVisible(string path) => PreviewCategories.Visible(path, hiddenPreviewCategories);

    private List<WorldMarker> VisiblePreviewMarkers() => previewMarkers.Select(m => PreviewCategoryVisible(m.Category) ? m : m with
        { Style = m.Style with { WorldVisible = false, MapVisible = false, MinimapVisible = false } }).ToList();

    private List<WorldTrail> VisiblePreviewTrails() => previewTrails.Select(t => PreviewCategoryVisible(t.Category) ? t : t with
        { WorldVisible = false, MapVisible = false, MinimapVisible = false }).ToList();

    private void ApplyPreviewVisibility()
    {
        using var timing = WorkspacePerformance.Measure("QuickMenu.TogglePreview");
        worldMarkers?.SetPreviewScene(VisiblePreviewMarkers(), VisiblePreviewTrails());
    }

    private uint worldSceneMap;
    private void RefreshWorldMarkers()
    {
        using var timing = TacoStuffer.Services.WorkspacePerformance.Measure("World.BuildScene");
        if (worldMarkers == null) return;
        if (worldSceneMap != gameFrame?.MapId) rawTrailPreview.Clear();
        worldSceneMap = gameFrame?.MapId ?? 0;
        var dots = new List<WorldMarker>();
        var categoryValues = CategoryValues();
        foreach (var page in pages)
        foreach (var poi in page.Xml.Descendants("POI"))
        {
            string path = Value(poi, "type");
            string visible = AttributeRules.Effective(poi, "inGameVisibility", "1", categoryValues);
            if (!uint.TryParse(AttributeRules.Effective(poi, "MapID", "", categoryValues), NumberStyles.None, CultureInfo.InvariantCulture, out uint map)) continue;
            if (map != worldSceneMap) continue;
            bool Coordinate(string key, out float value) => float.TryParse(Value(poi, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
            if (!Coordinate("xpos", out float x) || !Coordinate("ypos", out float y) || !Coordinate("zpos", out float z)) continue;
            string E(string name, string fallback) => AttributeRules.Effective(poi, name, fallback, categoryValues);
            float F(string name, float fallback) => float.TryParse(E(name, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && float.IsFinite(n) ? n : fallback;
            uint argb = VisualAttributeMath.Color(E("color", "FFFFFFFF"));
            Color color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            var style = new MarkerStyle(E("iconFile", ""), Math.Max(0.001f, F("iconSize", 1)), F("heightOffset", 0), F("alpha", 1), F("fadeNear", -1), F("fadeFar", -1), Math.Clamp(F("minSize", 5), 0, 4096), Math.Clamp(F("maxSize", 100), 0, 4096), color, VisualAttributeMath.Enabled(visible), VisualAttributeMath.Enabled(E("miniMapVisibility", "1")), VisualAttributeMath.Enabled(E("mapVisibility", "1")), Math.Clamp(F("mapDisplaySize", 20), 1, 256))
            {
                GameScale = preferences.MarkerGameScale, MapScale = preferences.MarkerMapScale, MinimapScale = preferences.MarkerMinimapScale,
                Rotation = VisualAttributeMath.Rotation(E("rotate", ""), E("rotate-x", ""), E("rotate-y", ""), E("rotate-z", "")),
                Sticky = VisualAttributeMath.Enabled(E("screenSticky", "false")) ? new StickyStyle(F("stickyDistance", 350), VisualAttributeMath.Enabled(E("stickyHideOriginal", "true")), F("stickyX", 0), F("stickyY", 0), F("stickyScale", 1)) : null
            };
            dots.Add(new(page.Name + "|" + ElementKey(poi), Value(poi, "DisplayName", Value(poi, "name", AssetStem(poi))), map, new Vector3(x, y, z), style) { Category = path });
        }
        var lines = new List<WorldTrail>();
        foreach (var page in pages)
        foreach (var e in page.Xml.Descendants("Trail"))
        {
            var trail = TrailFor(e) ?? RawTrailFor(e, categoryValues);
            if (trail == null || trail.MapId != worldSceneMap) continue;
            string path = Value(e, "type");
            string E(string n, string f) => AttributeRules.Effective(e, n, f, categoryValues);
            float F(string n, float f) => float.TryParse(E(n, f.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : f;
            uint argb = VisualAttributeMath.Color(E("color", "FFFFFFFF"));
            Color tint = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            lines.Add(new WorldTrail(trail, E("texture", ""), F("trailScale", 1), F("alpha", 1), F("fadeNear", -1), F("fadeFar", -1), F("heightOffset", 0), tint)
            {
                Key = page.Name + "|" + ElementKey(e), Category = path, GameWidth = preferences.TrailGameWidth, MapWidth = preferences.TrailMapWidth, MinimapWidth = preferences.TrailMinimapWidth,
                AnimSpeed = F("animSpeed", 1), WorldVisible = VisualAttributeMath.Enabled(E("inGameVisibility", "1")),
                MinimapVisible = VisualAttributeMath.Enabled(E("miniMapVisibility", "1")), MapVisible = VisualAttributeMath.Enabled(E("mapVisibility", "1")), MapSize = Math.Clamp(F("mapDisplaySize", 20), 1, 256)
            });
        }
        foreach (var reference in selectedTrailPoints)
        if (ResolvePoint(reference) is { } point && editableTrails.FirstOrDefault(t => t.Number == reference.Trail) is { } trail)
            dots.Add(new(reference.Key, $"Point {point.Id:0000000}", trail.MapId, point.Position, new MarkerStyle("", 1, 0, 1, -1, -1, 5, 10, Color.FromRgb(224, 35, 255), true, false, false, 10), true));
        worldMarkers.ProjectFolder = projectFolder;
        worldMarkers.ApplyVisualPreferences(visualPreviewPreferences ?? preferences);
        previewMarkers = dots.Where(m => !m.IsTrailPoint).ToList();
        previewTrails = lines;
        worldMarkers.SetScene(VisiblePreviewMarkers(), VisiblePreviewTrails());
        SyncOverlaySelection();
    }

    private void RecordMarker(string xml, string category)
    {
        if (overlay == null) return;
        PollGame();
        if (overlay == null) return;
        if (projectFile == null || !mumbleSession.IsPlayable || gameFrame == null || gameFrame.ProcessId != (uint)overlay.BoundProcessId || !gameClient.TryBounds(out _))
        {
            overlay.RecordingResult("Waiting for a fresh playable map. No marker added.");
            return;
        }
        if (!TrySyncXml())
        {
            overlay.RecordingResult("Fix the XML errors in the main editor before recording.");
            return;
        }
        var page = pages.FirstOrDefault(p => p.Name == xml);
        if (page == null)
        {
            overlay.RecordingResult("The selected XML no longer exists. Choose another target.");
            return;
        }
        try
        {
            var marker = MarkerRecording.Create(page.Xml, pages.Select(p => p.Xml).ToList(), category, gameFrame.MapId, gameFrame.AvatarPosition, nextMarkerNumber);
            var pois = page.Xml.Root!.Element("POIs");
            if (pois == null)
            {
                pois = new XElement("POIs");
                page.Xml.Root.Add(pois);
            }
            pois.Add(marker.Poi);
            nextMarkerNumber = marker.NextNumber;
            string key = ElementKey(marker.Poi);
            WritePages();
            var row = RevealRow(page.Name, key);
            if (row != null) FocusRow(row, activateDetached: false);
            overlay.SelectAsset(page.Name, key);
            overlay.RecordingResult(Value(marker.Poi, "DisplayName") + " recorded · map " + gameFrame.MapId);
            StatusText.Text = "Recorded " + Value(marker.Poi, "DisplayName") + " → " + page.Name + " / " + category + ". Save to keep your changes.";
            UpdateWorldProjection();
        }
        catch (InvalidOperationException ex)
        {
            overlay.RecordingResult(ex.Message);
        }
    }

    private void SyncOverlaySelection()
    {
        if (worldMarkers == null) return;
        worldMarkers.SetNameSelection(ElementsList.SelectedItems.Cast<ExplorerRow>()
            .Where(r => r.Kind == "POI").Select(r => r.DocumentName + "|" + r.Key));
        if (selected?.Kind is "point" or "segment")
        {
            var handles = new List<WorldMarker>();
            foreach (var reference in selectedTrailPoints)
            if (ResolvePoint(reference) is { } point && editableTrails.FirstOrDefault(t => t.Number == reference.Trail) is { } pointTrail)
                handles.Add(new(reference.Key, $"Point {point.Id:0000000}", pointTrail.MapId, point.Position, new MarkerStyle("", 1, 0, 1, -1, -1, 5, 10, Color.FromRgb(224, 35, 255), true, false, false, 10), true));
            worldMarkers.SetPointMarkers(handles);
            worldMarkers.SetSelection(selectedTrailPoints.Select(p => p.Key).ToList());
            overlay?.SetPointSelection(selectedTrailPoints.ToList());
            if (selected.Kind == "segment" && TrailFor(selected.Element) is { } segmentTrail && long.TryParse(selected.Key.Split('|').Last(), out long id)) overlay?.SelectSegment(new(segmentTrail.Number, id));
            return;
        }
        selectedTrailPoints.Clear();
        worldMarkers.SetPointMarkers([]);
        bool marker = selected?.Element?.Name.LocalName == "POI";
        worldMarkers.SetSelection(marker ? [selected!.DocumentName + "|" + selected.Key] : []);
        if (selected?.Element?.Name.LocalName is "POI" or "Trail")
        {
            overlay?.SelectAsset(selected.DocumentName, selected.Key);
            if (selected.Element.Name.LocalName == "Trail" && TrailFor(selected.Element) is { } selectedTrail) ChooseTrail(selectedTrail.Number, null);
        }
        else overlay?.ClearAssetSelection();
    }

    private void ClearWorldSelection()
    {
        bool prior = updating;
        updating = true;
        try
        {
            selected = null;
            selectedTrailPoints.Clear();
            ElementsList.SelectedItems.Clear();
        }
        finally
        {
            updating = prior;
        }
        if (worldMarkers != null)
        {
            worldMarkers.SetNameSelection([]);
            worldMarkers.SetPointMarkers([]);
            worldMarkers.SetSelection([]);
        }
        overlay?.ClearAssetSelection();
        AttributesHost.Children.Clear();
        SelectionTitle.Text = "Select an element";
        CategoryPath.Text = "";
        StatusText.Text = "Marker deselected.";
    }

    private void SelectWorldTrail(TrailPointRef point)
    {
        var source = previewTrails.FirstOrDefault(t => t.Trail.Number == point.Trail);
        if (!editableTrails.Any(t => t.Number == point.Trail))
        {
            // Binary-only trails still select their XML object; their points require Make Editable.
            if (source != null) SelectWorldMarker(source.Key);
            if ((visualPreviewPreferences ?? preferences).TrailOverlaySelection != TrailSelectionMode.Trail)
                overlay?.TrailResult("Trail selected. Use Make Editable in the editor to select its segments or points.");
            return;
        }
        switch ((visualPreviewPreferences ?? preferences).TrailOverlaySelection)
        {
            case TrailSelectionMode.TrailSegment:
                SelectTrailSegment(new(point.Trail, point.Segment));
                break;
            case TrailSelectionMode.NearestPoint:
                SelectTrailPoints([point]);
                break;
            default:
                if (source != null) SelectWorldMarker(source.Key);
                break;
        }
    }

    private void SelectWorldMarker(string key)
    {
        if (TrailPointRef.Parse(key) is { } point)
        {
            SelectTrailPoints([point]);
            return;
        }
        int bar = key.IndexOf('|');
        if (bar < 0) return;
        var row = RevealRow(key[..bar], key[(bar + 1)..]);
        if (row != null) FocusRow(row, activateDetached: false);
    }

    private void MoveWorldMarker(string key, Vector3 position, bool final)
    {
        if (!final)
        {
            if (selected?.DocumentName + "|" + selected?.Key != key) return;
            bool prior = updating;
            updating = true;
            try
            {
                foreach (var input in AttributesHost.Children.OfType<TextBox>())
                if (input.Tag is string name && name is ("xpos" or "ypos" or "zpos"))
                    input.Text = (name == "xpos" ? position.X : name == "ypos" ? position.Y : position.Z).ToString("R", CultureInfo.InvariantCulture);
            }
            finally
            {
                updating = prior;
            }
            return;
        }
        int bar = key.IndexOf('|');
        if (bar < 0) return;
        var page = pages.FirstOrDefault(p => p.Name == key[..bar]);
        var poi = page?.Xml.Descendants("POI").FirstOrDefault(p => ElementKey(p) == key[(bar + 1)..]);
        if (poi == null) return;
        // Do not overwrite raw editor changes made during a drag.
        if (!TrySyncXml()) return;
        poi = page!.Xml.Descendants("POI").FirstOrDefault(p => ElementKey(p) == key[(bar + 1)..]);
        if (poi == null) return;
        SetPosition(poi, position);
        WritePages();
        StatusText.Text = final ? "Marker position updated. Save to keep your changes." : "Moving marker…";
    }

    private static void SetPosition(XElement poi, Vector3 position)
    {
        poi.SetAttributeValue("xpos", position.X.ToString("R", CultureInfo.InvariantCulture));
        poi.SetAttributeValue("ypos", position.Y.ToString("R", CultureInfo.InvariantCulture));
        poi.SetAttributeValue("zpos", position.Z.ToString("R", CultureInfo.InvariantCulture));
    }

    private void RepositionMarker(OverlayAsset asset)
    {
        PollGame();
        if (overlay == null || !mumbleSession.IsPlayable || gameFrame == null || gameFrame.ProcessId != (uint)overlay.BoundProcessId || !gameClient.TryBounds(out _)) return;
        if (!TrySyncXml())
        {
            overlay.RecordingResult("Fix the XML errors before repositioning.");
            return;
        }
        var page = pages.FirstOrDefault(p => p.Name == asset.DocumentName);
        var poi = page?.Xml.Descendants("POI").FirstOrDefault(p => ElementKey(p) == asset.Key);
        if (poi == null) return;
        SetPosition(poi, gameFrame.AvatarPosition);
        poi.SetAttributeValue("MapID", gameFrame.MapId);
        WritePages();
        SelectWorldMarker(asset.DocumentName + "|" + asset.Key);
        overlay.RecordingResult("Marker moved to character · map " + gameFrame.MapId);
    }

    private void AddOverlayCategory(string xml, string path)
    {
        var row = RevealRow(xml, "category:" + path);
        if (row != null) AddChildCategory(row);
        if (selected?.Element?.Name.LocalName == "MarkerCategory") overlay?.SetTarget(selected.DocumentName, CategoryFullPath(selected.Element));
    }

}

