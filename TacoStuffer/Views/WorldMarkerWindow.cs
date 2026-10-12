using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TacoStuffer.Models;
using TacoStuffer.Services;
using Point = System.Windows.Point;

namespace TacoStuffer.Views;

public sealed record MarkerStyle(string Icon, float Size, float Height, float Alpha, float FadeNear, float FadeFar, float MinSize, float MaxSize, Color Color, bool WorldVisible = true, bool MinimapVisible = true, bool MapVisible = true, float MapSize = 20)
{
    public float GameScale { get; init; } = 1;
    public float MapScale { get; init; } = 1;
    public float MinimapScale { get; init; } = 1;
    public Vector3? Rotation { get; init; }
    public StickyStyle? Sticky { get; init; }
}

public sealed record StickyStyle(float Distance, bool HideOriginal, float X, float Y, float Scale);

public sealed record WorldMarker(string Key, string Label, uint MapId, Vector3 Position, MarkerStyle Style, bool IsTrailPoint = false)
{
    public string Category { get; init; } = "";
}

public sealed record WorldTrail(EditableTrail Trail, string Texture, float Scale, float Alpha, float FadeNear, float FadeFar, float Height, Color Color)
{
    public string Key { get; init; } = "";
    public string Category { get; init; } = "";
    public float GameWidth { get; init; } = 1;
    public float MapWidth { get; init; } = 1;
    public float MinimapWidth { get; init; } = 1;
    public float AnimSpeed { get; init; } = 1;
    public bool WorldVisible { get; init; } = true;
    public bool MinimapVisible { get; init; } = true;
    public bool MapVisible { get; init; } = true;
    public float MapSize { get; init; } = 20;
}

public sealed class WorldMarkerWindow : Window
{
    private readonly MarkerSurface surface = new();
    private NativeGameWindow.Rect previousBounds;
    private bool passThrough = true;
    private DragState? drag;
    private Point? lastDragMouse;
    private OverlayMouseObserver? mouseObserver;
    private GameClientWindow currentGame;
    private nint currentPanel;
    public nint InteractiveExclusionHandle { get; set; }
    private nint previousExclusionHandle;
    private bool presentationRepair;
    public void RequestPresentationRepair()
    {
        presentationRepair = true;
        gpuTrails?.RequestPresentationRepair();
    }

    private bool closed;
    private bool previousButtonDown;
    private GpuTrailRenderer? gpuTrails;
    private bool gpuAttempted;
    public event Action? SelectionCleared;
    public event Action<string>? MarkerSelected;
    public event Action<TrailPointRef>? TrailSelected;
    public event Action<string, Vector3, bool>? MarkerMoved;
    public event Action<IReadOnlyDictionary<string, Vector3>, bool>? PointsMoved;
    public Func<bool>? BeginMove { get; set; }
    public string? SelectedKey
    {
        get => surface.SelectedKey;
        set
        {
            if (drag != null && drag.Key != value) FinishMove();
            surface.SelectedKey = value;
            surface.InvalidateVisual();
        }
    }
    public void ApplyVisualPreferences(EditorPreferences preferences)
    {
        surface.VisualPreferences = preferences.Copy();
        surface.InvalidateVisual();
    }
    public void SetNameSelection(IEnumerable<string> keys)
    {
        var selected = keys.ToHashSet();
        if (surface.NameSelectedKeys.SetEquals(selected)) return;
        surface.NameSelectedKeys = selected;
        surface.InvalidateVisual();
    }
    public string? ProjectFolder
    {
        get => surface.ProjectFolder;
        set
        {
            surface.ProjectFolder = value;
        }
    }
    public nint Handle => new WindowInteropHelper(this).Handle;
    public WorldMarkerWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = surface;
        SourceInitialized += (_, _) =>
        {
            NativeGameWindow.DisableActivation(Handle, clickThrough: true);
            HwndSource.FromHwnd(Handle)?.AddHook(NoActivate);
            mouseObserver = new OverlayMouseObserver();
            mouseObserver.Button += (down, point, root) =>
            {
                if (!closed && !Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => ObserveButton(down, point, root)));
            };
        };
        PreviewMouseLeftButtonDown += Down;
        PreviewMouseMove += Move;
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (drag != null)
            {
                FinishMove();
                e.Handled = true;
            }
        };
        LostMouseCapture += (_, _) => FinishMove();
        Closed += (_, _) =>
        {
            closed = true;
            mouseObserver?.Dispose();
            FinishMove();
            gpuTrails?.Dispose();
            gpuTrails = null;
        };
    }

    private static nint NoActivate(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21)
        {
            handled = true;
            return 3;
        }
        return 0;
    }

    public void SetSelection(IReadOnlyList<string> keys)
    {
        if (drag != null && !keys.Contains(drag.Key)) FinishMove();
        surface.SelectedKeys = keys.ToHashSet();
        surface.SelectedKey = keys.FirstOrDefault();
        surface.InvalidateVisual();
    }

    private List<WorldMarker> sceneMarkers = [];
    public void SetPointMarkers(List<WorldMarker> points)
    {
        // Selection handles do not alter trail meshes or trigger asset reloads.
        surface.Markers = sceneMarkers.Concat(points).ToList();
        surface.InvalidateVisual();
    }

    public void SetPreviewScene(List<WorldMarker> markers, List<WorldTrail> trails)
    {
        // Category toggles preserve textures/meshes and skip asset IO entirely.
        sceneMarkers = markers.Where(m => !m.IsTrailPoint).ToList();
        var points = surface.Markers.Where(m => m.IsTrailPoint);
        surface.Markers = sceneMarkers.Concat(points).ToList();
        surface.Trails = trails;
        gpuTrails?.Invalidate();
        surface.InvalidateVisual();
    }

    public void SetScene(List<WorldMarker> markers, List<WorldTrail> trails)
    {
        // Ribbon data is independent of editor selection and point handles.
        sceneMarkers = markers.Where(m => !m.IsTrailPoint).ToList();
        surface.Trails = trails;
        surface.Markers = markers;
        gpuTrails?.SetScene(trails);
        surface.ClearLabels();
        surface.RefreshIcons();
        surface.InvalidateVisual();
    }

    public void InvalidateTrails()
    {
        gpuTrails?.Invalidate();
        surface.InvalidateVisual();
    }

    public void Conceal()
    {
        FinishMove();
        surface.SetHandleHighlight(null);
        SetPassThrough(true);
        gpuTrails?.Hide();
        Topmost = false;
        if (IsVisible) Hide();
    }

    private void SetPassThrough(bool enabled)
    {
        if (Handle == 0 || enabled == passThrough) return;
        passThrough = enabled;
        NativeGameWindow.SetClickThrough(Handle, enabled);
    }

    private static bool Contains(NativeGameWindow.Rect bounds, NativeGameWindow.Point p) => p.X >= bounds.Left && p.X < bounds.Right && p.Y >= bounds.Top && p.Y < bounds.Bottom;

    private void ObserveButton(bool down, NativeGameWindow.Point point, nint root)
    {
        if (closed) return;
        if (!down)
        {
            if (drag != null)
            {
                UpdateDrag(PointFromScreen(new Point(point.X, point.Y)));
                FinishMove();
            }
            return;
        }
        // WindowFromPoint can identify the GPU display HWND even though its
        // layered/transparent style routes the actual click through to GW2.
        bool fromGpu = gpuTrails != null && gpuTrails.Handle != 0 && root == gpuTrails.Handle;
        if (!IsVisible || (!NativeGameWindow.IsGameRoot(currentGame, root) && root != Handle && !fromGpu) || !currentGame.TryBounds(out var bounds) || !Contains(bounds, point)) return;
        if (NativeGameWindow.GetWindowRect(currentPanel, out var panelBounds) && Contains(panelBounds, point)) return;
        if (InteractiveExclusionHandle != 0 && NativeGameWindow.GetWindowRect(InteractiveExclusionHandle, out var codeBounds) && Contains(codeBounds, point)) return;
        var hit = surface.Hit(PointFromScreen(new Point(point.X, point.Y)));
        if (drag == null && hit == null && SelectedKey != null)
        {
            SetSelection([]);
            SetPassThrough(true);
            SelectionCleared?.Invoke();
        }
    }

    public void Update(GameClientWindow game, bool ready, MumbleSnapshot? camera, nint panel)
    {
        currentGame = game;
        currentPanel = panel;
        if (!ready || camera == null || !game.TryBounds(out var bounds) || !NativeGameWindow.IsForeground(game, panel, Environment.ProcessId) || !surface.HasWorldContent(camera.MapId))
        {
            Conceal();
            return;
        }
        bool wasVisible = IsVisible;
        var old = surface.Camera;
        bool cameraChanged = old == null || old.CameraPosition != camera.CameraPosition || old.CameraFront != camera.CameraFront || old.Fov != camera.Fov || old.AvatarPosition != camera.AvatarPosition || old.UiState != camera.UiState || old.MapId != camera.MapId || old.Compass != camera.Compass;
        surface.Camera = camera;
        if (!wasVisible) Show();
        if (!NativeGameWindow.IsForeground(game, panel, Environment.ProcessId))
        {
            Conceal();
            return;
        }
        bool positionChanged = !wasVisible || !Topmost || !NativeGameWindow.IsWindowVisible(Handle) || !NativeGameWindow.IsTopmost(Handle)
            || !bounds.Equals(previousBounds) || InteractiveExclusionHandle != previousExclusionHandle;
        Topmost = true;
        if (positionChanged || presentationRepair)
        {
            if (NativeGameWindow.PlaceOverlay(Handle, InteractiveExclusionHandle != 0 ? InteractiveExclusionHandle : panel,
                bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, true))
            {
                previousBounds = bounds;
                previousExclusionHandle = InteractiveExclusionHandle;
                presentationRepair = false;
            }
        }
        // Native cursor sampling keeps dragging independent of WPF mouse-message
        // cadence and detects releases even when the game has keyboard focus.
        bool down = NativeGameWindow.IsSelectButtonDown;
        if (NativeGameWindow.GetCursorPos(out var cursor))
        {
            var mouse = PointFromScreen(new Point(cursor.X, cursor.Y));
            if (drag != null)
            {
                UpdateDrag(mouse);
                if (!down) FinishMove();
            }
            if (mouseObserver?.IsActive != true && down && !previousButtonDown)
                ObserveButton(true, cursor, NativeGameWindow.GetForegroundWindow());
            bool overPanel = NativeGameWindow.GetWindowRect(panel, out var panelBounds) && Contains(panelBounds, cursor);
            overPanel |= InteractiveExclusionHandle != 0 && NativeGameWindow.GetWindowRect(InteractiveExclusionHandle, out var codeBounds) && Contains(codeBounds, cursor);
            var hit = !overPanel && Contains(bounds, cursor) ? surface.Hit(mouse) : null;
            if (drag != null) surface.SetHandleHighlight(drag.Key, drag.Axis);
            else surface.SetHandleHighlight(hit?.Key, hit?.Axis ?? -99);
            SetPassThrough(drag == null && (overPanel || hit == null));
        }
        previousButtonDown = down;
        if (!gpuAttempted && surface.Trails.Count > 0)
        {
            gpuAttempted = true;
            gpuTrails = GpuTrailRenderer.TryCreate();
            surface.UseGpuTrails = gpuTrails != null;
            surface.InvalidateVisual();
        }
        if (gpuTrails != null)
        {
            try
            {
                gpuTrails.Render(bounds, Handle, camera, surface.Trails, surface.ProjectFolder, surface.DragPositions, cameraChanged || positionChanged);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException or IOException or UnauthorizedAccessException)
            {
                GpuTrailRenderer.Log("GPU rendering failed; WPF trail fallback enabled. " + ex);
                gpuTrails.Dispose();
                gpuTrails = null;
                surface.UseGpuTrails = false;
                surface.InvalidateVisual();
            }
        }
        if (cameraChanged || positionChanged || !surface.UseGpuTrails && surface.Trails.Any(t => (t.WorldVisible || t.MapVisible || t.MinimapVisible) && t.Trail.MapId == camera.MapId && t.AnimSpeed != 0)) surface.InvalidateVisual();
    }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        var hit = surface.Hit(e.GetPosition(surface));
        if (hit == null) return;
        if (hit.Trail is { } segment)
        {
            e.Handled = true;
            if (surface.NearestTrailPoint(segment, e.GetPosition(surface)) is { } point) TrailSelected?.Invoke(point);
            return;
        }
        if (hit.Axis == -2)
        {
            SetSelection([hit.Key]);
            MarkerSelected?.Invoke(hit.Key);
        }
        e.Handled = true;
        if (hit.Axis < -1 || surface.Camera == null || BeginMove?.Invoke() != true) return;
        var marker = surface.Markers.FirstOrDefault(m => m.Key == hit.Key);
        if (marker == null) return;
        var forward = Vector3.Normalize(surface.Camera.CameraFront);
        var right = Vector3.Cross(Vector3.UnitY, forward);
        right = right.LengthSquared() < 0.000001f ? Vector3.UnitX : Vector3.Normalize(right);
        var up = Vector3.Normalize(Vector3.Cross(forward, right));
        var originals = surface.Markers.Where(m => surface.SelectedKeys.Contains(m.Key)).ToDictionary(m => m.Key, m => m.Position);
        if (originals.Count == 0) originals[marker.Key] = marker.Position;
        var pivot = EditableTrails.Pivot(originals.Values);
        float units = surface.UnitsPerPixel(pivot + Vector3.UnitY * marker.Style.Height);
        Vector2 projected = hit.End - hit.Start;
        if (hit.Axis >= 0 && hit.Axis < 3 && projected.LengthSquared() < 16) return;
        drag = new(hit.Key, pivot, e.GetPosition(surface), hit.Axis,
            hit.Axis >= 0 && hit.Axis < 3 ? Vector2.Normalize(projected) : default,
            hit.Axis >= 0 && hit.Axis < 3 ? units * 75 / projected.Length() : units, right, up, originals);
        surface.SetHandleHighlight(hit.Key, hit.Axis);
        lastDragMouse = null;
        surface.DragPositions = new Dictionary<string, Vector3>(originals);
        SetPassThrough(false);
        if (!CaptureMouse()) FinishMove();
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (drag == null) return;
        // Position is sampled once per render; avoid flooding the editor with
        // updates from a high-polling-rate mouse.
        e.Handled = true;
    }

    private void UpdateDrag(Point mouse)
    {
        if (drag == null || lastDragMouse == mouse) return;
        lastDragMouse = mouse;
        var delta = new Vector2((float)(mouse.X - drag.Mouse.X), (float)(mouse.Y - drag.Mouse.Y));
        var positions = new Dictionary<string, Vector3>();
        if (drag.Axis >= 3)
        {
            // Keep X's existing drag direction; Y/Z rings need the opposite
            // pointer-to-angle sign in this Mumble-space manipulator.
            int rotationAxis = drag.Axis - 3;
            float radians = (Math.Abs(delta.X) >= Math.Abs(delta.Y) ? delta.X : -delta.Y) * 0.5f * MathF.PI / 180;
            if (rotationAxis is 1 or 2) radians = -radians;
            foreach (var p in drag.Originals) positions[p.Key] = EditableTrails.Rotate(p.Value, drag.Position, rotationAxis, radians);
        }
        else
        {
            Vector3 change = drag.Axis < 0 ? MarkerPreviewMath.PlaneDelta(delta, drag.Right, drag.Up, drag.Units) : MarkerPreviewMath.AxisDelta(delta, drag.Direction, drag.Units, drag.Axis);
            foreach (var p in drag.Originals) positions[p.Key] = p.Value + change;
        }
        if (positions.Values.Any(p => !EditableTrails.Finite(p))) return;
        surface.DragPositions = positions;
        gpuTrails?.Invalidate();
        surface.InvalidateVisual();
        if (TrailPointRef.Parse(drag.Key) != null) PointsMoved?.Invoke(positions, false);
        else MarkerMoved?.Invoke(drag.Key, positions[drag.Key], false);
    }

    private void FinishMove()
    {
        var ended = drag;
        if (ended == null) return;
        var positions = surface.DragPositions;
        drag = null;
        lastDragMouse = null;
        surface.DragPositions = null;
        surface.SetHandleHighlight(null);
        gpuTrails?.Invalidate();
        // Release first: editor work must never keep the game/panel captured.
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (NativeGameWindow.GetCapture() == Handle) NativeGameWindow.ReleaseCapture();
        SetPassThrough(true);
        surface.InvalidateVisual();
        if (positions != null)
        {
            if (TrailPointRef.Parse(ended.Key) != null) PointsMoved?.Invoke(positions, true);
            else if (positions.TryGetValue(ended.Key, out var final)) MarkerMoved?.Invoke(ended.Key, final, true);
        }
    }

    private sealed record DragState(string Key, Vector3 Position, Point Mouse, int Axis, Vector2 Direction, float Units, Vector3 Right, Vector3 Up, Dictionary<string, Vector3> Originals);

    private sealed record HitRegion(string Key, int Axis, Rect Bounds, Vector2 Start, Vector2 End, TrailSegmentRef? Trail = null);

    private sealed class MarkerSurface : FrameworkElement
    {
        public List<WorldMarker> Markers { get; set; } = [];
        public MumbleSnapshot? Camera { get; set; }
        public string? SelectedKey { get; set; }
        public string? ProjectFolder { get; set; }
        public EditorPreferences VisualPreferences { get; set; } = new();
        public Dictionary<string, Vector3>? DragPositions { get; set; }
        public HashSet<string> SelectedKeys { get; set; } = [];
        public HashSet<string> NameSelectedKeys { get; set; } = [];
        public List<WorldTrail> Trails { get; set; } = [];
        public bool UseGpuTrails { get; set; }
        private string? highlightedKey;
        private int highlightedAxis = -99;
        public void SetHandleHighlight(string? key, int axis = -99)
        {
            if (axis < -1) key = null; // Marker icons are not manipulator handles.
            if (key == null) axis = -99;
            if (highlightedKey == key && highlightedAxis == axis) return;
            highlightedKey = key;
            highlightedAxis = axis;
            InvalidateVisual();
        }

        private bool HandleHighlighted(string key, int axis) => highlightedKey == key && highlightedAxis == axis;

        public bool HasWorldContent(uint map) => Trails.Any(t => t.Trail.MapId == map) || Markers.Any(m => m.MapId == map);

        private static readonly DrawingBrush DefaultTrail = PrepareDefaultTrail();
        private static DrawingBrush PrepareDefaultTrail()
        {
            var brush = ((DrawingBrush)Application.Current.FindResource("DefaultTrailPreview")).Clone();
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.Viewport = new Rect(0, 0, 1, 1);
            brush.Freeze();
            return brush;
        }

        private static readonly ImageSource DefaultMarker = (ImageSource)Application.Current.FindResource("DefaultMarkerPreview");
        private static readonly Pen AxisHitPen = new(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), 14);
        private static readonly Pen CenterPen = new(Brushes.White, 1.5);
        private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(143, 215, 255));
        private static readonly Pen HighlightPen = new(HighlightBrush, 2);
        private static readonly Pen HighlightCenterPen = new(HighlightBrush, 1.5);
        private static readonly Pen[] AxisPens = [new(Brushes.OrangeRed, 2), new(Brushes.LimeGreen, 2), new(Brushes.DodgerBlue, 2)];
        private static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        private static readonly Brush[] AxisColors = [Brushes.OrangeRed, Brushes.LimeGreen, Brushes.DodgerBlue];
        private static readonly Brush Purple = new SolidColorBrush(Color.FromRgb(224, 35, 255));
        private static readonly Brush HitBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        private static readonly Brush LabelBackground = new SolidColorBrush(Color.FromArgb(185, 10, 12, 17));
        private readonly Dictionary<string, FormattedText> labels = [];
        private readonly Dictionary<string, ImageSource?> icons = [];
        private readonly Dictionary<string, Brush> trailBrushes = [];
        private readonly List<HitRegion> hits = [];
        private readonly List<TrailPickTriangle> trailHits = [];
        private readonly long animationStart = System.Diagnostics.Stopwatch.GetTimestamp();
        private double labelDpi;
        public void ClearLabels() => labels.Clear();

        private readonly Dictionary<string, (string Root, DateTime Stamp)> iconVersions = [];
        public void RefreshIcons()
        {
            var liveSegments = Trails.SelectMany(t => t.Trail.Segments).ToHashSet();
            foreach (var segment in mapTrailGeometry.Keys.Where(s => !liveSegments.Contains(s)).ToArray()) mapTrailGeometry.Remove(segment);
            foreach (var style in Markers.Select(m => m.Style).Concat(UseGpuTrails ? Enumerable.Empty<MarkerStyle>() : Trails.Select(t => new MarkerStyle(t.Texture, 1, 0, 1, -1, -1, 1, 100, t.Color))).DistinctBy(s => s.Icon + "|" + s.Color))
            {
                string key = style.Icon + "|" + style.Color;
                DateTime stamp = default;
                try
                {
                    if (ProjectFolder != null) stamp = File.GetLastWriteTimeUtc(Path.Combine(ProjectFolder, style.Icon.Replace('\\', Path.DirectorySeparatorChar)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
                var version = (ProjectFolder ?? "", stamp);
                if (!iconVersions.TryGetValue(key, out var old) || old != version)
                {
                    icons.Remove(key);
                    trailBrushes.Remove(key);
                    iconVersions[key] = version;
                }
                Icon(style);
            }
            var active = Markers.Select(m => m.Style.Icon + "|" + m.Style.Color).Concat(Trails.Select(t => t.Texture + "|" + t.Color)).ToHashSet();
            foreach (string key in icons.Keys.Where(k => !active.Contains(k)).ToList())
            {
                icons.Remove(key);
                trailBrushes.Remove(key);
                iconVersions.Remove(key);
            }
        }

        public HitRegion? Hit(Point point)
        {
            var mouse = new Vector2((float)point.X, (float)point.Y);
            var marker = hits.LastOrDefault(h => h.Bounds.Contains(point) && (h.Axis < 0 || SegmentDistance(mouse, h.Start, h.End) < 8));
            if (marker != null) return marker;
            if (MapArea().Contains(point) || TrailPicking.Hit(trailHits, mouse) is not { } segment) return null;
            return new("segment|" + segment.Trail + "|" + segment.Segment, -3, default, default, default, segment);
        }

        public TrailPointRef? NearestTrailPoint(TrailSegmentRef reference, Point mouse)
        {
            if (Camera == null) return null;
            var line = Trails.FirstOrDefault(t => t.Trail.Number == reference.Trail);
            var segment = line?.Trail.Segments.FirstOrDefault(s => s.Id == reference.Segment);
            if (line == null || segment == null) return null;
            var projector = new CameraProjection.Frame(Camera, (float)ActualWidth, (float)ActualHeight);
            var points = new List<(long Id, Vector2 Screen)>();
            foreach (var point in segment.Points)
            if (projector.TryProject(TrailPosition(reference.Trail, reference.Segment, point) + Vector3.UnitY * line.Height, out var screen))
                points.Add((point.Id, screen));
            long? id = TrailPicking.NearestPoint(points, new((float)mouse.X, (float)mouse.Y)) ?? segment.Points.FirstOrDefault()?.Id;
            return id.HasValue ? new(reference.Trail, reference.Segment, id.Value) : null;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a;
            return Vector2.Distance(p, a + d * Math.Clamp(Vector2.Dot(p - a, d) / Math.Max(0.0001f, d.LengthSquared()), 0, 1));
        }

        public float UnitsPerPixel(Vector3 position)
        {
            if (Camera == null) return 0;
            float depth = Vector3.Dot(position - Camera.CameraPosition, Vector3.Normalize(Camera.CameraFront));
            return Math.Max(CameraProjection.NearClip, depth) * 2 * MathF.Tan(Camera.Fov * 0.5f) / Math.Max(1, (float)ActualHeight);
        }

        private readonly Dictionary<string, ImageSource> defaultTints = [];
        private ImageSource DefaultTint(Color color, bool trail)
        {
            string key = (trail ? "trail|" : "marker|") + color;
            if (defaultTints.TryGetValue(key, out var cached)) return cached;
            if (defaultTints.Count >= 64) defaultTints.Clear();
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (trail)
                {
                    var asset = ((DrawingBrush)Application.Current.FindResource("DefaultTrailPreview")).Clone();
                    asset.Viewport = new Rect(0, 0, 32, 32);
                    dc.DrawRectangle(asset, null, new Rect(0, 0, 32, 32));
                }
                else dc.DrawImage(DefaultMarker, new Rect(0, 0, 32, 32));
            }
            var source = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            source.Render(visual);
            var pixels = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            byte[] bytes = new byte[32 * 32 * 4];
            pixels.CopyPixels(bytes, 128, 0);
            for (int i = 0; i < bytes.Length; i += 4)
            {
                bytes[i] = (byte)(bytes[i] * color.B / 255);
                bytes[i + 1] = (byte)(bytes[i + 1] * color.G / 255);
                bytes[i + 2] = (byte)(bytes[i + 2] * color.R / 255);
                bytes[i + 3] = (byte)(bytes[i + 3] * color.A / 255);
            }
            var tinted = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, bytes, 128);
            tinted.Freeze();
            return defaultTints[key] = tinted;
        }

        private ImageSource? Icon(MarkerStyle style)
        {
            if (style.Icon.Length == 0 || ProjectFolder == null) return DefaultTint(style.Color, false);
            string key = style.Icon + "|" + style.Color;
            if (icons.TryGetValue(key, out var found)) return found;
            try
            {
                string root = Path.GetFullPath(ProjectFolder) + Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(Path.Combine(root, style.Icon.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return icons[key] = null;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                var pixels = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int stride = checked(pixels.PixelWidth * 4);
                byte[] bytes = new byte[checked(stride * pixels.PixelHeight)];
                pixels.CopyPixels(bytes, stride, 0);
                for (int i = 0; i < bytes.Length; i += 4)
                {
                    bytes[i] = (byte)(bytes[i] * style.Color.B / 255);
                    bytes[i + 1] = (byte)(bytes[i + 1] * style.Color.G / 255);
                    bytes[i + 2] = (byte)(bytes[i + 2] * style.Color.R / 255);
                    bytes[i + 3] = (byte)(bytes[i + 3] * style.Color.A / 255);
                }
                var tinted = BitmapSource.Create(pixels.PixelWidth, pixels.PixelHeight, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
                tinted.Freeze();
                return icons[key] = tinted;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.IO.FileFormatException or OverflowException)
            {
                return icons[key] = null;
            }
        }

        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            hits.Clear();
            trailHits.Clear();
            if (Camera == null || ActualWidth < 2 || ActualHeight < 2) return;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (dpi != labelDpi)
            {
                labels.Clear();
                labelDpi = dpi;
            }
            if ((Camera.UiState & 1) != 0)
            {
                DrawMap(drawing);
                return;
            }
            var area = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
            var mapArea = MapArea();
            drawing.PushClip(mapArea.Width > 0 && mapArea.Height > 0 ? new CombinedGeometry(GeometryCombineMode.Exclude, area, new RectangleGeometry(mapArea)) : area);
            if (!UseGpuTrails) DrawTrails(drawing);
            AddTrailHitRegions(drawing);
            foreach (var marker in Markers)
            {
                bool selected = SelectedKeys.Contains(marker.Key);
                if (marker.IsTrailPoint && !selected) continue;
                var raw = PreviewPosition(marker.Key, marker.Position);
                if (!marker.Style.WorldVisible) continue;
                var icon = Icon(marker.Style);
                bool sticky = marker.MapId == Camera.MapId && marker.Style.Sticky is { } settings
                    && VisualAttributeMath.StickyInRange(Camera.AvatarPosition, raw, settings.Distance);
                if (sticky && marker.Style.Sticky!.HideOriginal) continue;
                var position = raw + Vector3.UnitY * marker.Style.Height;
                if (marker.MapId != Camera.MapId || !CameraProjection.TryProject(Camera, position, (float)ActualWidth, (float)ActualHeight, out var screen)) continue;
                double alpha = MarkerPreviewMath.Opacity(marker.Style.Alpha, Vector3.Distance(position, Camera.CameraPosition), marker.Style.FadeNear, marker.Style.FadeFar);
                double radius = marker.IsTrailPoint ? 6 : Math.Clamp(marker.Style.Size * 0.5 / UnitsPerPixel(position), marker.Style.MinSize, Math.Max(marker.Style.MinSize, marker.Style.MaxSize)) * marker.Style.GameScale;
                var rect = new Rect(screen.X - radius, screen.Y - radius, radius * 2, radius * 2);
                if (rect.Right < 0 || rect.Bottom < 0 || rect.Left > ActualWidth || rect.Top > ActualHeight) continue;
                if (alpha > 0 && marker.Style.Color.A > 0)
                {
                    drawing.PushOpacity(alpha);
                    if (marker.Style.Rotation is { } rotation) rect = DrawRotatedMarker(drawing, marker, icon ?? DefaultTint(marker.Style.Color, false), position, radius, rotation);
                    else drawing.DrawImage(icon ?? DefaultTint(marker.Style.Color, false), rect);
                    drawing.Pop();
                    var hit = rect;
                    hit.Inflate(4, 4);
                    hits.Add(new(marker.Key, -2, hit, default, default));
                    drawing.DrawRectangle(HitBrush, null, hit);
                }

                if (VisualPreferences.ShouldShowMarkerName(selected || NameSelectedKeys.Contains(marker.Key), Camera.AvatarPosition, raw))
                {
                    if (!labels.TryGetValue(marker.Key, out var text)) labels[marker.Key] = text = new FormattedText(marker.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Purple, dpi);
                    text.MaxTextWidth = Math.Max(1, Math.Min(300, ActualWidth - 8));
                    text.Trimming = TextTrimming.CharacterEllipsis;
                    double x = Math.Clamp(screen.X + radius + 5, 4, Math.Max(4, ActualWidth - text.Width - 4));
                    double y = Math.Clamp(screen.Y - text.Height * 0.5, 4, Math.Max(4, ActualHeight - text.Height - 4));
                    drawing.DrawRoundedRectangle(LabelBackground, null, new Rect(x - 3, y - 1, text.Width + 6, text.Height + 2), 3, 3);
                    drawing.DrawText(text, new Point(x, y));
                }
            }
            var chosen = Markers.Where(m => SelectedKeys.Contains(m.Key) && m.MapId == Camera.MapId && m.Style.WorldVisible).ToList();
            if (chosen.Count > 0)
            {
                var anchor = EditableTrails.Pivot(chosen.Select(m => PreviewPosition(m.Key, m.Position))) + (chosen.Count == 1 ? Vector3.UnitY * chosen[0].Style.Height : Vector3.Zero);
                if (CameraProjection.TryProject(Camera, anchor, (float)ActualWidth, (float)ActualHeight, out var screen)) DrawGizmo(drawing, chosen[0].Key, anchor, screen, chosen.Count > 1 && chosen.All(m => m.IsTrailPoint));
            }
            drawing.Pop();
            DrawMap(drawing);
            foreach (var marker in Markers)
            if (marker.MapId == Camera.MapId && marker.Style.WorldVisible && marker.Style.Sticky is { } sticky
                && VisualAttributeMath.StickyInRange(Camera.AvatarPosition, PreviewPosition(marker.Key, marker.Position), sticky.Distance))
                DrawSticky(drawing, marker, Icon(marker.Style) ?? DefaultTint(marker.Style.Color, false));
        }

        private void DrawSticky(DrawingContext drawing, WorldMarker marker, ImageSource image)
        {
            var style = marker.Style.Sticky!;
            var dpi = VisualTreeHelper.GetDpi(this);
            float pixelWidth = image is BitmapSource bitmap ? bitmap.PixelWidth : (float)image.Width;
            float pixelHeight = image is BitmapSource bitmap2 ? bitmap2.PixelHeight : (float)image.Height;
            var placement = VisualAttributeMath.StickyRect((float)(ActualWidth * dpi.DpiScaleX), (float)(ActualHeight * dpi.DpiScaleY), pixelWidth, pixelHeight, style.X, style.Y, style.Scale);
            var rect = new Rect(placement.X / dpi.DpiScaleX, placement.Y / dpi.DpiScaleY, placement.Width / dpi.DpiScaleX, placement.Height / dpi.DpiScaleY);
            drawing.PushOpacity(Math.Clamp(marker.Style.Alpha, 0, 1));
            drawing.DrawImage(image, rect);
            drawing.Pop();
            if (marker.Style.Alpha > 0 && marker.Style.Color.A > 0)
            {
                hits.Add(new(marker.Key, -2, rect, default, default));
                drawing.DrawRectangle(HitBrush, null, rect);
            }
        }

        private Rect DrawRotatedMarker(DrawingContext drawing, WorldMarker marker, ImageSource image, Vector3 center, double radius, Vector3 rotation)
        {
            float half = (float)radius * UnitsPerPixel(center);
            var a = center + VisualAttributeMath.RotateMarker(new(-half, half, 0), rotation);
            var b = center + VisualAttributeMath.RotateMarker(new(half, half, 0), rotation);
            var c = center + VisualAttributeMath.RotateMarker(new(half, -half, 0), rotation);
            var d = center + VisualAttributeMath.RotateMarker(new(-half, -half, 0), rotation);
            var brush = new ImageBrush(image) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), Stretch = Stretch.Fill };
            brush.Freeze();
            var projector = new CameraProjection.Frame(Camera!, (float)ActualWidth, (float)ActualHeight);
            Span<RibbonVertex> clipped = stackalloc RibbonVertex[12];
            DrawClippedTriangle(drawing, brush, projector, new(a, new(0, 0)), new(b, new(1, 0)), new(c, new(1, 1)), clipped);
            DrawClippedTriangle(drawing, brush, projector, new(a, new(0, 0)), new(c, new(1, 1)), new(d, new(0, 1)), clipped);
            var bounds = Rect.Empty;
            foreach (var vertex in new[] { a, b, c, d }) if (projector.TryProject(vertex, out var p)) bounds.Union(new Point(p.X, p.Y));
            return bounds.IsEmpty ? new Rect(0, 0, 0, 0) : bounds;
        }

        private Vector3 PreviewPosition(string key, Vector3 position) => DragPositions != null && DragPositions.TryGetValue(key, out var moved) ? moved : position;

        private Rect MapArea()
        {
            if (Camera == null || Camera.Compass == null) return Rect.Empty;
            var dpi = VisualTreeHelper.GetDpi(this);
            var viewport = MapProjection.Viewport(Camera, (float)(ActualWidth * dpi.DpiScaleX), (float)(ActualHeight * dpi.DpiScaleY));
            return viewport.Width <= 0 || viewport.Height <= 0 ? Rect.Empty : new Rect(viewport.X / dpi.DpiScaleX, viewport.Y / dpi.DpiScaleY, viewport.Width / dpi.DpiScaleX, viewport.Height / dpi.DpiScaleY);
        }

        private readonly Dictionary<TrailSegment, (long Revision, int Count, TrailGpuVertex[] Geometry)> mapTrailGeometry = [];
        private readonly HashSet<TrailSegmentRef> mapPreviewSegments = [];
        private TrailGpuVertex[] MapTrailGeometry(WorldTrail line, TrailSegment segment)
        {
            bool preview = mapPreviewSegments.Contains(new(line.Trail.Number, segment.Id));
            if (!preview && mapTrailGeometry.TryGetValue(segment, out var saved) && saved.Revision == segment.GeometryRevision && saved.Count == segment.Points.Count) return saved.Geometry;
            var geometry = GpuTrailGeometry.Build(line.Trail, segment, preview ? DragPositions : null);
            if (!preview) mapTrailGeometry[segment] = (segment.GeometryRevision, segment.Points.Count, geometry);
            return geometry;
        }

        private void DrawMap(DrawingContext drawing)
        {
            if (Camera?.Compass == null) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            float width = (float)(ActualWidth * dpi.DpiScaleX), height = (float)(ActualHeight * dpi.DpiScaleY);
            var viewport = MapProjection.Viewport(Camera, width, height);
            var area = MapArea();
            if (area.IsEmpty) return;
            drawing.PushClip(new RectangleGeometry(area));
            bool full = (Camera.UiState & 1) != 0;
            mapPreviewSegments.Clear();
            if (DragPositions != null) foreach (string key in DragPositions.Keys)
                if (TrailPointRef.Parse(key) is { } point) mapPreviewSegments.Add(new(point.Trail, point.Segment));
            if (!UseGpuTrails && MapRibbonProjection.TryBasis(Camera, width, height, out var basis))
            {
                foreach (var line in Trails)
                {
                    if (line.Trail.MapId != Camera.MapId || (full ? !line.MapVisible : !line.MinimapVisible)) continue;
                    var brush = TrailBrush(line.Texture, line.Color);
                    float ribbonWidth = MapRibbonProjection.Width(line.MapSize, Camera.Compass.UiSize) * (full ? line.MapWidth : line.MinimapWidth);
                    float uvScale = MapRibbonProjection.UvScale(basis, ribbonWidth, line.Scale);
                    float phase = VisualAttributeMath.AnimationOffset(System.Diagnostics.Stopwatch.GetElapsedTime(animationStart).TotalSeconds, line.AnimSpeed);
                    drawing.PushOpacity(Math.Clamp(line.Alpha, 0, 1));
                    foreach (var segment in line.Trail.Segments)
                    {
                        var vertices = MapTrailGeometry(line, segment);
                        for (int i = 0; i < vertices.Length; i += 4)
                        {
                            Vector2 Screen(TrailGpuVertex v)
                            {
                                var p = basis.ProjectSide(v.Position, v.Center, ribbonWidth * .5f);
                                return new(p.X / (float)dpi.DpiScaleX, p.Y / (float)dpi.DpiScaleY);
                            }
                            Vector2 Uv(TrailGpuVertex v) => new(v.Uv.X, v.Uv.Y * uvScale + phase);
                            var a = Screen(vertices[i]);
                            var b = Screen(vertices[i + 1]);
                            var c = Screen(vertices[i + 2]);
                            var d = Screen(vertices[i + 3]);
                            var bounds = new Rect(new Point(a.X, a.Y), new Point(c.X, c.Y));
                            bounds.Union(new Point(b.X, b.Y));
                            bounds.Union(new Point(d.X, d.Y));
                            if (!bounds.IntersectsWith(area)) continue;
                            DrawTriangle(drawing, brush, a, b, c, Uv(vertices[i]), Uv(vertices[i + 1]), Uv(vertices[i + 2]));
                            DrawTriangle(drawing, brush, a, c, d, Uv(vertices[i]), Uv(vertices[i + 2]), Uv(vertices[i + 3]));
                        }
                    }
                    drawing.Pop();
                }
            }
            foreach (var marker in Markers)
            {
                if (marker.IsTrailPoint || marker.MapId != Camera.MapId || (full ? !marker.Style.MapVisible : !marker.Style.MinimapVisible)) continue;
                if (!MapProjection.TryProject(Camera, PreviewPosition(marker.Key, marker.Position), viewport, width, height, out var screen)) continue;
                double x = screen.X / dpi.DpiScaleX, y = screen.Y / dpi.DpiScaleY;
                double radius = marker.Style.MapSize * (full ? marker.Style.MapScale : marker.Style.MinimapScale) * 0.5 * MapProjection.UiScale(Camera.Compass.UiSize) / dpi.DpiScaleX;
                var rect = new Rect(x - radius, y - radius, radius * 2, radius * 2);
                if (!rect.IntersectsWith(area)) continue;
                drawing.PushOpacity(Math.Clamp(marker.Style.Alpha, 0, 1));
                drawing.DrawImage(Icon(marker.Style) ?? DefaultTint(marker.Style.Color, false), rect);
                drawing.Pop();
            }
            drawing.Pop();
        }

        private void AddTrailHitRegions(DrawingContext drawing)
        {
            if (Camera == null) return;
            var projector = new CameraProjection.Frame(Camera, (float)ActualWidth, (float)ActualHeight);
            Span<RibbonVertex> clipped = stackalloc RibbonVertex[12];
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var path = geometry.Open())
            {
                foreach (var line in Trails)
                {
                    if (!line.WorldVisible || line.Trail.MapId != Camera.MapId || line.Color.A == 0 || line.Alpha <= 0) continue;
                    foreach (var segment in line.Trail.Segments)
                    {
                        var reference = new TrailSegmentRef(line.Trail.Number, segment.Id);
                        for (int i = 1; i < segment.Points.Count; i++)
                        {
                            var a = TrailPosition(line.Trail.Number, segment.Id, segment.Points[i - 1]) + Vector3.UnitY * line.Height;
                            var b = TrailPosition(line.Trail.Number, segment.Id, segment.Points[i]) + Vector3.UnitY * line.Height;
                            var direction = b - a;
                            if (!EditableTrails.Finite(a) || !EditableTrails.Finite(b) || direction.LengthSquared() < .000000000001f
                                || MarkerPreviewMath.Opacity(line.Alpha, Vector3.Distance((a + b) / 2, Camera.CameraPosition), line.FadeNear, line.FadeFar) <= 0) continue;
                            var side = Vector3.Cross(direction, Vector3.UnitY);
                            side = (side.LengthSquared() < .000001f ? Vector3.UnitX : Vector3.Normalize(side)) * (.508f * line.GameWidth);
                            // These are the same two ribbon triangles used by GPU and WPF rendering.
                            for (int half = 0; half < 2; half++)
                            {
                                int count = RibbonProjection.Clip(projector, new(a - side, default),
                                    new(half == 0 ? a + side : b + side, default), new(half == 0 ? b + side : b - side, default), clipped);
                                for (int j = 1; j < count - 1; j++)
                                {
                                    if (!projector.TryProject(clipped[0].Position, out var x) || !projector.TryProject(clipped[j].Position, out var y)
                                        || !projector.TryProject(clipped[j + 1].Position, out var z)) continue;
                                    trailHits.Add(new(reference, x, y, z,
                                        projector.Depth(clipped[0].Position), projector.Depth(clipped[j].Position), projector.Depth(clipped[j + 1].Position)));
                                    // Consistent winding keeps overlapping transparent hit areas filled.
                                    if ((y.X - x.X) * (z.Y - x.Y) - (y.Y - x.Y) * (z.X - x.X) < 0) (y, z) = (z, y);
                                    path.BeginFigure(new(x.X, x.Y), true, true);
                                    path.LineTo(new(y.X, y.Y), true, false);
                                    path.LineTo(new(z.X, z.Y), true, false);
                                }
                            }
                        }
                    }
                }
            }
            geometry.Freeze();
            drawing.DrawGeometry(HitBrush, null, geometry);
        }

        private long lastTrailTiming;
        private int renderedRibbonTriangles;
        private void DrawTrails(DrawingContext drawing)
        {
            if (Camera == null) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int visited = 0, drawn = 0;
            renderedRibbonTriangles = 0;
            var projector = new CameraProjection.Frame(Camera, (float)ActualWidth, (float)ActualHeight);
            Span<RibbonVertex> clipped = stackalloc RibbonVertex[12];
            foreach (var line in Trails)
            {
                if (line.Trail.MapId != Camera.MapId || !line.WorldVisible) continue;
                var brush = TrailBrush(line.Texture, line.Color);
                float offset = VisualAttributeMath.AnimationOffset(System.Diagnostics.Stopwatch.GetElapsedTime(animationStart).TotalSeconds, line.AnimSpeed);
                foreach (var segment in line.Trail.Segments)
                {
                    float length = 0;
                    for (int i = 1; i < segment.Points.Count; i++)
                    {
                        visited++;
                        Vector3 a = TrailPosition(line.Trail.Number, segment.Id, segment.Points[i - 1]) + Vector3.UnitY * line.Height;
                        Vector3 b = TrailPosition(line.Trail.Number, segment.Id, segment.Points[i]) + Vector3.UnitY * line.Height;
                        var direction = b - a;
                        float distance = direction.Length();
                        var side = Vector3.Cross(direction, Vector3.UnitY);
                        if (distance < 0.000001f) continue;
                        side = side.LengthSquared() < 0.000001f ? Vector3.UnitX * 0.508f : Vector3.Normalize(side) * 0.508f;
                        side *= line.GameWidth;
                        double alpha = MarkerPreviewMath.Opacity(line.Alpha, Vector3.Distance((a + b) / 2, Camera.CameraPosition), line.FadeNear, line.FadeFar);
                        if (alpha <= 0)
                        {
                            length += distance;
                            continue;
                        }
                        float scale = Math.Max(0.001f, Math.Abs(line.Scale)) * 2 / line.GameWidth;
                        drawn++;
                        drawing.PushOpacity(alpha);
                        DrawClippedTriangle(drawing, brush, projector, new(a - side, new(0, -length * scale + offset)), new(a + side, new(1, -length * scale + offset)), new(b + side, new(1, -(length + distance) * scale + offset)), clipped);
                        DrawClippedTriangle(drawing, brush, projector, new(a - side, new(0, -length * scale + offset)), new(b + side, new(1, -(length + distance) * scale + offset)), new(b - side, new(0, -(length + distance) * scale + offset)), clipped);
                        drawing.Pop();
                        length += distance;
                    }
                }
            }
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (ms > 16.7 && Environment.TickCount64 - lastTrailTiming > 1000)
            {
                lastTrailTiming = Environment.TickCount64;
                System.Diagnostics.Debug.WriteLine($"Taco Stuffer trail draw: {ms:F1} ms; {visited} edges visited, {drawn} candidates, {renderedRibbonTriangles} triangles.");
            }
        }

        private Vector3 TrailPosition(long trail, long segment, TrailPoint point) => DragPositions == null ? point.Position : PreviewPosition($"point|{trail}|{segment}|{point.Id}", point.Position);

        private Brush TrailBrush(string texture, Color color)
        {
            string key = texture + "|" + color;
            if (trailBrushes.TryGetValue(key, out var cached)) return cached;
            var image = texture.Length > 0 ? Icon(new MarkerStyle(texture, 1, 0, 1, -1, -1, 1, 100, color)) : DefaultTint(color, true);
            image ??= DefaultTint(color, true);
            var brush = new ImageBrush(image) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), TileMode = TileMode.Tile, Stretch = Stretch.Fill };
            brush.Freeze();
            return trailBrushes[key] = brush;
        }

        private void DrawClippedTriangle(DrawingContext drawing, Brush brush, CameraProjection.Frame camera, RibbonVertex a, RibbonVertex b, RibbonVertex c, Span<RibbonVertex> clipped)
        {
            if (camera.Depth(a.Position) <= CameraProjection.NearClip && camera.Depth(b.Position) <= CameraProjection.NearClip && camera.Depth(c.Position) <= CameraProjection.NearClip) return;
            if (camera.TryProject(a.Position, out var pa) && camera.TryProject(b.Position, out var pb) && camera.TryProject(c.Position, out var pc))
            {
                if (camera.Contains(pa) && camera.Contains(pb) && camera.Contains(pc))
                {
                    DrawPerspectiveProjected(drawing, brush, camera, a, b, c, pa, pb, pc, 0);
                    return;
                }
            }
            int count = RibbonProjection.Clip(camera, a, b, c, clipped);
            for (int i = 1; i + 1 < count; i++) DrawPerspectiveTriangle(drawing, brush, camera, clipped[0], clipped[i], clipped[i + 1], 0);
        }

        private void DrawPerspectiveTriangle(DrawingContext drawing, Brush brush, CameraProjection.Frame camera, RibbonVertex a, RibbonVertex b, RibbonVertex c, int level)
        {
            if (!camera.TryProject(a.Position, out var pa) || !camera.TryProject(b.Position, out var pb) || !camera.TryProject(c.Position, out var pc)) return;
            DrawPerspectiveProjected(drawing, brush, camera, a, b, c, pa, pb, pc, level);
        }

        private void DrawPerspectiveProjected(DrawingContext drawing, Brush brush, CameraProjection.Frame camera, RibbonVertex a, RibbonVertex b, RibbonVertex c, Vector2 pa, Vector2 pb, Vector2 pc, int level)
        {
            float da = camera.Depth(a.Position), db = camera.Depth(b.Position), dc = camera.Depth(c.Position);
            if (level >= 7 || Math.Max(da, Math.Max(db, dc)) <= Math.Min(da, Math.Min(db, dc)) * 1.01f)
            {
                DrawTriangle(drawing, brush, pa, pb, pc, a.Uv, b.Uv, c.Uv);
                return;
            }
            // WPF brushes are affine. Subdivide only when a projected midpoint
            // differs by more than two pixels; cap depth to bound frame work.
            float Error(RibbonVertex x, RibbonVertex y, Vector2 px, Vector2 py) => camera.TryProject((x.Position + y.Position) * .5f, out var mid) ? Vector2.DistanceSquared(mid, (px + py) * .5f) : 0;
            float ab = Error(a, b, pa, pb), bc = Error(b, c, pb, pc), ca = Error(c, a, pc, pa);
            if (level < 7 && Math.Max(ab, Math.Max(bc, ca)) > 4)
            {
                if (ab >= bc && ab >= ca)
                {
                    var m = RibbonVertex.Midpoint(a, b);
                    DrawPerspectiveTriangle(drawing, brush, camera, a, m, c, level + 1);
                    DrawPerspectiveTriangle(drawing, brush, camera, m, b, c, level + 1);
                }
                else if (bc >= ca)
                {
                    var m = RibbonVertex.Midpoint(b, c);
                    DrawPerspectiveTriangle(drawing, brush, camera, a, b, m, level + 1);
                    DrawPerspectiveTriangle(drawing, brush, camera, a, m, c, level + 1);
                }
                else
                {
                    var m = RibbonVertex.Midpoint(c, a);
                    DrawPerspectiveTriangle(drawing, brush, camera, a, b, m, level + 1);
                    DrawPerspectiveTriangle(drawing, brush, camera, m, b, c, level + 1);
                }
                return;
            }
            DrawTriangle(drawing, brush, pa, pb, pc, a.Uv, b.Uv, c.Uv);
        }

        private void DrawTriangle(DrawingContext drawing, Brush texture, Vector2 a, Vector2 b, Vector2 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var u = ub - ua;
            var v = uc - ua;
            float det = u.X * v.Y - u.Y * v.X;
            if (Math.Abs(det) < 0.000001f) return;
            var ab = b - a;
            var ac = c - a;
            var x = (ab * v.Y - ac * u.Y) / det;
            var y = (ac * u.X - ab * v.X) / det;
            var offset = a - x * ua.X - y * ua.Y;
            // Transform the draw, not the texture. Cloning a DrawingBrush here
            // deep-copies its drawing tree twice per edge on every frame.
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(ua.X, ua.Y), true, true);
                ctx.LineTo(new Point(ub.X, ub.Y), true, false);
                ctx.LineTo(new Point(uc.X, uc.Y), true, false);
            }
            geometry.Freeze();
            drawing.PushTransform(new MatrixTransform(new Matrix(x.X, x.Y, y.X, y.Y, offset.X, offset.Y)));
            drawing.DrawGeometry(texture, null, geometry);
            drawing.Pop();
            renderedRibbonTriangles++;
        }

        private void DrawGizmo(DrawingContext drawing, string key, Vector3 world, Vector2 center, bool rotation)
        {

            for (int i = 0; i < 3; i++)
            {
                if (!CameraProjection.TryProject(Camera!, world + Axes[i] * UnitsPerPixel(world) * 75, (float)ActualWidth, (float)ActualHeight, out var end) || Vector2.Distance(center, end) < 4) continue;
                var bounds = new Rect(new Point(center.X, center.Y), new Point(end.X, end.Y));
                bounds.Inflate(8, 8);
                hits.Add(new(key, i, bounds, center, end));
                drawing.DrawLine(AxisHitPen, new Point(center.X, center.Y), new Point(end.X, end.Y));
                bool highlighted = HandleHighlighted(key, i);
                drawing.DrawLine(highlighted ? HighlightPen : AxisPens[i], new Point(center.X, center.Y), new Point(end.X, end.Y));
                var direction = Vector2.Normalize(end - center);
                var side = new Vector2(-direction.Y, direction.X);
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(end.X, end.Y), true, true);
                    var a = end - direction * 10 + side * 4;
                    var b = end - direction * 10 - side * 4;
                    ctx.LineTo(new Point(a.X, a.Y), true, false);
                    ctx.LineTo(new Point(b.X, b.Y), true, false);
                }
                drawing.DrawGeometry(highlighted ? HighlightBrush : AxisColors[i], null, triangle);
            }
            if (rotation)
            {
                float radius = UnitsPerPixel(world) * 95;
                for (int axis = 0; axis < 3; axis++)
                {
                    var ringPen = HandleHighlighted(key, axis + 3) ? HighlightPen : AxisPens[axis];
                    Vector3 a = Axes[(axis + 1) % 3], b = Axes[(axis + 2) % 3];
                    Vector2 previous = default;
                    bool hasPrevious = false;
                    for (int step = 0; step <= 48; step++)
                    {
                        float angle = step * MathF.PI * 2 / 48;
                        if (!CameraProjection.TryProject(Camera!, world + (a * MathF.Cos(angle) + b * MathF.Sin(angle)) * radius, (float)ActualWidth, (float)ActualHeight, out var p))
                        {
                            hasPrevious = false;
                            continue;
                        }
                        if (hasPrevious)
                        {
                            var bounds = new Rect(new Point(previous.X, previous.Y), new Point(p.X, p.Y));
                            bounds.Inflate(7, 7);
                            hits.Add(new(key, axis + 3, bounds, previous, p));
                            drawing.DrawLine(AxisHitPen, new Point(previous.X, previous.Y), new Point(p.X, p.Y));
                            drawing.DrawLine(ringPen, new Point(previous.X, previous.Y), new Point(p.X, p.Y));
                        }
                        previous = p;
                        hasPrevious = true;
                    }
                }
            }
            var box = new Rect(center.X - 6, center.Y - 6, 12, 12);
            drawing.DrawRectangle(LabelBackground, HandleHighlighted(key, -1) ? HighlightCenterPen : CenterPen, box);
            hits.Add(new(key, -1, box, center, center));
        }
    }
}
