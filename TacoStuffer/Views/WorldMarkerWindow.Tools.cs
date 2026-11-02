using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TacoStuffer.Models;
using TacoStuffer.Services;
using Point = System.Windows.Point;

namespace TacoStuffer.Views;

public sealed partial class WorldMarkerWindow
{
    public event Action<IReadOnlyList<string>, bool, bool>? SelectionRequested;
    public event Action<TrailPointRef>? ToolPointRequested;
    public event Action? ToolAcceptRequested;
    public event Action<IReadOnlyDictionary<string, Vector3>, bool>? PositionsMoved;
    public nint PresentationAnchor { get; set; }
    public nint ToolbarHandle { get; set; }
    private OverlayToolKeys? toolKeys;
    private Point? marqueeOrigin;
    private bool marqueeShift, marqueeControl, previousEnter;
    private readonly OverlayClickGesture fallbackGesture = new();
    private NativeGameWindow.Point fallbackOrigin;
    private nint fallbackRoot;
    public void SetEditTool(OverlayEditTool tool)
    {
        CancelMarquee();
        if (drag != null) FinishMove();
        surface.EditTool = tool; surface.InsertToolActive = tool == OverlayEditTool.InsertPoint;
        surface.InsertCursor = null;
        surface.ToolStatus = tool switch
        {
            OverlayEditTool.Marquee => "Marquee select tool active",
            OverlayEditTool.InsertPoint => "Trail point insert tool active",
            OverlayEditTool.RemovePoint => "Trail point remove tool active",
            OverlayEditTool.SplitSegment => "Split trail segment tool active",
            OverlayEditTool.ReverseSegment => "Reverse segment: click a point; Esc exits",
            OverlayEditTool.MergeSegments => "Merge segments: choose two endpoints, then Enter",
            _ => ""
        };
        Cursor = tool == OverlayEditTool.InsertPoint ? Cursors.Cross : Cursors.Arrow;
        surface.InvalidateVisual();
    }
    private bool Excluded(NativeGameWindow.Point point)
    {
        foreach (nint window in new[] { currentPanel, InteractiveExclusionHandle, ToolbarHandle })
            if (window != 0 && NativeGameWindow.IsWindowVisible(window) && NativeGameWindow.GetWindowRect(window, out var b) && Contains(b, point)) return true;
        return false;
    }
    private bool WorldInput(NativeGameWindow.Point p, nint root)
    {
        bool gpu = gpuTrails != null && root == gpuTrails.Handle;
        return IsVisible && (root == Handle || gpu || NativeGameWindow.IsGameRoot(currentGame, root))
            && currentGame.TryBounds(out var bounds) && Contains(bounds, p) && !Excluded(p);
    }
    private void ObserveClick(OverlayClick click, long press)
    {
        if (closed || pressTracker.IsConsumed(press)) return;
        var start = new NativeGameWindow.Point { X = click.X, Y = click.Y };
        var end = new NativeGameWindow.Point { X = click.EndX, Y = click.EndY };
        if (!WorldInput(start, click.Root) || Excluded(end) || !currentGame.TryBounds(out var bounds) || !Contains(bounds, end)) return;
        var mouse = PointFromScreen(new Point(click.X, click.Y));
        if (surface.Camera == null || (surface.Camera.UiState & 1) != 0 || surface.MapArea().Contains(mouse)) return;
        if (surface.EditTool == OverlayEditTool.InsertPoint) { if (surface.InsertionHit(mouse) is { } hit) InsertPointRequested?.Invoke(hit); return; }
        if (surface.EditTool is OverlayEditTool.RemovePoint or OverlayEditTool.SplitSegment or OverlayEditTool.MergeSegments or OverlayEditTool.ReverseSegment)
        { if (surface.PointAt(mouse, surface.EditTool == OverlayEditTool.MergeSegments) is { } point) ToolPointRequested?.Invoke(point); return; }
        var selected = surface.Hit(mouse);
        if (selected?.Trail is { } segment)
        {
            if (surface.NearestTrailPoint(segment, mouse) is { } point)
            {
                if (click.Shift || click.Control) SelectionRequested?.Invoke([point.Key], click.Shift, click.Control);
                else TrailSelected?.Invoke(point);
            }
            return;
        }
        if (selected != null) { SelectionRequested?.Invoke([selected.Key], click.Shift, click.Control); return; }
        if (surface.EditTool == OverlayEditTool.Marquee) return;
        if (!click.Shift && !click.Control) SelectionCleared?.Invoke();
    }
    private bool BeginMarquee(MouseButtonEventArgs e)
    {
        if (surface.EditTool != OverlayEditTool.Marquee || surface.Camera == null || (surface.Camera.UiState & 1) != 0 || NativeGameWindow.OtherButtonDown) return false;
        var p = e.GetPosition(surface); if (surface.MapArea().Contains(p)) return false;
        pressTracker.ConsumeCurrent(); previousButtonDown = true;
        marqueeOrigin = p; marqueeShift = NativeGameWindow.ShiftDown; marqueeControl = NativeGameWindow.ControlDown;
        surface.Marquee = new Rect(p, p); SetPassThrough(false);
        if (!CaptureMouse()) CancelMarquee(); e.Handled = true; return true;
    }
    private void UpdateMarquee(Point mouse)
    {
        if (marqueeOrigin is not { } origin) return;
        mouse = new(Math.Clamp(mouse.X, 0, surface.ActualWidth), Math.Clamp(mouse.Y, 0, surface.ActualHeight));
        var box = new Rect(origin, mouse);
        if (surface.Marquee == box) return;
        surface.Marquee = box; surface.InvalidateVisual();
    }
    private void FinishMarquee()
    {
        if (marqueeOrigin == null) return;
        var box = surface.Marquee; var keys = box.HasValue ? surface.BoxSelection(box.Value) : [];
        bool shift = marqueeShift, control = marqueeControl; CancelMarquee();
        SelectionRequested?.Invoke(keys, shift, control);
    }
    private void CancelMarquee()
    {
        marqueeOrigin = null; surface.Marquee = null;
        if (drag == null && IsMouseCaptured) ReleaseMouseCapture();
        surface.InvalidateVisual();
    }
    private sealed partial class MarkerSurface
    {
        public OverlayEditTool EditTool;
        public string ToolStatus = "";
        public Rect? Marquee;
        private readonly List<(long Trail, long Segment, long Id, Vector2 Screen, float Depth)> projectedPoints = [];
        private readonly List<(long Trail, long Segment, long Id, Vector2 Screen, float Depth)> projectedEndpoints = [];
        private readonly HashSet<(int X, int Y)> drawnPointCells = [];
        private bool pointsValid, endpointsValid;
        private IReadOnlyList<(long Trail, long Segment, long Id, Vector2 Screen, float Depth)> VisiblePoints(bool endpointsOnly = false)
        {
            var result = endpointsOnly ? projectedEndpoints : projectedPoints;
            if (endpointsOnly ? endpointsValid : pointsValid) return result;
            result.Clear();
            if (Camera == null) return result;
            var mapArea = MapArea();
            var frame = new CameraProjection.Frame(Camera, (float)ActualWidth, (float)ActualHeight);
            foreach (var line in Trails)
            {
                if (!line.Editable || !line.WorldVisible || line.Trail.MapId != Camera.MapId || line.Color.A == 0 || line.Alpha <= 0) continue;
                foreach (var segment in line.Trail.Segments)
                    for (int i = 0; i < segment.Points.Count; i += endpointsOnly ? Math.Max(1, segment.Points.Count - 1) : 1)
                    {
                        var point = segment.Points[i];
                        var world = TrailPosition(line.Trail.Number, segment.Id, point) + Vector3.UnitY * line.Height;
                        if (MarkerPreviewMath.Opacity(line.Alpha, Vector3.Distance(world, Camera.CameraPosition), line.FadeNear, line.FadeFar) <= 0) continue;
                        if (frame.TryProject(world, out var screen) && frame.Contains(screen) && !mapArea.Contains(new Point(screen.X, screen.Y)))
                            result.Add((line.Trail.Number, segment.Id, point.Id, screen, frame.Depth(world)));
                    }
            }
            if (endpointsOnly) endpointsValid = true; else pointsValid = true;
            return result;
        }
        public TrailPointRef? PointAt(Point mouse, bool endpointsOnly = false)
        {
            var cursor = new Vector2((float)mouse.X, (float)mouse.Y);
            float distance = 100, depth = float.PositiveInfinity;
            TrailPointRef? nearest = null;
            foreach (var point in VisiblePoints(endpointsOnly))
            {
                float d = Vector2.DistanceSquared(point.Screen, cursor);
                if (d > distance || d == distance && point.Depth >= depth) continue;
                distance = d; depth = point.Depth;
                nearest = new(point.Trail, point.Segment, point.Id);
            }
            return nearest;
        }
        public List<string> BoxSelection(Rect box)
        {
            var keys = hits.Where(h => h.Axis == -2 && box.IntersectsWith(h.Bounds)).Select(h => h.Key);
            return keys.Concat(VisiblePoints().Where(p => box.Contains(new Point(p.Screen.X, p.Screen.Y))).Select(p => new TrailPointRef(p.Trail, p.Segment, p.Id).Key)).Distinct().ToList();
        }
        private void DrawToolVisuals(DrawingContext drawing)
        {
            if (EditTool == OverlayEditTool.Marquee) drawing.DrawRectangle(HitBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (EditTool is OverlayEditTool.Marquee or OverlayEditTool.RemovePoint or OverlayEditTool.SplitSegment or OverlayEditTool.MergeSegments or OverlayEditTool.ReverseSegment)
            {
                drawnPointCells.Clear();
                foreach (var point in VisiblePoints(EditTool == OverlayEditTool.MergeSegments))
                    // Dense recordings often project hundreds of points onto the
                    // same pixels. Draw one hint per cell; picking retains all points.
                    if (drawnPointCells.Add(((int)(point.Screen.X / 5), (int)(point.Screen.Y / 5))))
                        drawing.DrawEllipse(Brushes.Gold, null, new Point(point.Screen.X, point.Screen.Y), 3, 3);
            }
        }
        private void DrawToolCursor(DrawingContext drawing)
        {
            if (Marquee is { } box) drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, 100, 217, 140)), new Pen(Brushes.LightGreen, 1) { DashStyle = DashStyles.Dash }, box);
            if (InsertCursor is not { } mouse) return;
            if (EditTool == OverlayEditTool.Marquee)
                drawing.DrawRectangle(null, new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dot }, new Rect(mouse.X + 13, mouse.Y + 4, 13, 10));
            else if (EditTool != OverlayEditTool.Select)
            {
                string glyph = EditTool switch { OverlayEditTool.InsertPoint => "+", OverlayEditTool.RemovePoint => "−", OverlayEditTool.SplitSegment => "⋮", OverlayEditTool.ReverseSegment => "⇄", _ => "↔" };
                var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 22, Brushes.Yellow, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawing.DrawText(text, new Point(mouse.X + 14, mouse.Y - 8));
            }
        }
    }
}
