using System.Numerics;
using System.Xml.Linq;
using TacoStuffer.Models;

internal static class Launch100Checks
{
    private static void Check(bool value,string reason) { if (!value) throw new Exception(reason); }
    private static void Reject(Action action,string reason) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception(reason); }
    public static void Run()
    {
        var presses = new OverlayPressTracker();
        long dragPress = presses.ObservePress();
        presses.ConsumeCurrent();
        Check(presses.IsConsumed(dragPress),"Delayed observer delivery of a manipulator press remains consumed after the drag finishes");
        long outsidePress = presses.ObservePress();
        Check(!presses.IsConsumed(outsidePress) && presses.IsConsumed(dragPress),"A later deliberate click outside can still deselect");
        presses.ConsumeCurrent();
        Check(presses.IsConsumed(outsidePress) && !presses.IsConsumed(presses.ObservePress()),"Consecutive manipulations do not block future presses");
        var category = XElement.Parse("<MarkerCategory name='pack' triggerRange='4' info='Instructions' cpRadius='6' isCheckpoint='true' fadeNear='20' fadeFar='30'/>");
        var marker = XElement.Parse("<POI type='pack' triggerRange='3'/>");
        var categories = new Dictionary<string,XElement> { ["pack"] = category };
        var ranges = MarkerRanges.Effective(n => AttributeRules.Effective(marker,n,"",categories));
        Check(ranges.Single(r => r.Attribute == "triggerRange").Radius == 3 && ranges.Single(r => r.Attribute == "infoRange").Radius == 3 && ranges.Single(r => r.Attribute == "cpRadius").Radius == 6,"Range rings use local overrides, inherited attributes and Info trigger fallback");
        Check(ranges.Where(r => r.Attribute.StartsWith("fade")).All(r => r.Reference == "camera"),"Fade rings identify camera-distance semantics");
        var fadeRanges = MarkerRanges.Effective(n => n == "fadeNear" ? "3000" : n == "fadeFar" ? "3500" : "");
        var fadeNear = fadeRanges.Single(r => r.Attribute == "fadeNear");
        var fadeFar = fadeRanges.Single(r => r.Attribute == "fadeFar");
        Check(Math.Abs(fadeNear.Radius-76.2f)<.0001f && Math.Abs(fadeFar.Radius-88.9f)<.0001f
            && fadeNear.GameUnits == 3000 && fadeFar.GameUnits == 3500,"Fade rings convert game inches into meters and preserve XML units for labels");
        Check(Math.Abs(MarkerPreviewMath.Opacity(1,fadeNear.Radius,3000,3500)-1)<.0001f
            && MarkerPreviewMath.Opacity(1,fadeFar.Radius,3000,3500)<.0001f
            && Math.Abs(MarkerPreviewMath.Opacity(1,(fadeNear.Radius+fadeFar.Radius)/2,3000,3500)-.5f)<.0001f,
            "Ring radii align with the actual fade start, midpoint and disappearance boundaries");
        Check(ranges.Single(r => r.Attribute == "triggerRange").Radius == 3 && ranges.Single(r => r.Attribute == "cpRadius").Radius == 6,
            "Meter-based interaction/checkpoint ranges are not converted as inches");
        var values = new Dictionary<string,string> { ["info"] = "Read",["isCheckpoint"] = "true",["screenSticky"] = "true" };
        var defaults = MarkerRanges.Effective(n => values.GetValueOrDefault(n,""));
        Check(defaults.Single(r => r.Attribute == "infoRange").Radius == 2 && defaults.Single(r => r.Attribute == "cpRadius").Radius == 2 && defaults.Single(r => r.Attribute == "stickyDistance").Radius == 300,"Consumer defaults appear without requiring explicit range attributes");
        Check(MarkerRanges.Effective(_ => "").Count == 0,"Markers without range attributes/consumers have no rings");
        Check(MarkerRanges.Effective(n => n == "triggerRange" ? "NaN" : "").Count == 0,"Nonfinite range values are excluded");
        var preferences = new EditorPreferences(); Check(!preferences.ShowMarkerRange,"Range display defaults off");
        preferences.ShowMarkerRange = true;
        string prefFile = Path.Combine(Path.GetTempPath(),"TacoStuffer100-" + Guid.NewGuid().ToString("N") + ".json");
        try { preferences.Save(prefFile); Check(preferences.Copy().ShowMarkerRange && EditorPreferences.Load(prefFile).ShowMarkerRange,"Range preference copies and persists"); }
        finally { if (File.Exists(prefFile)) File.Delete(prefFile); }
        preferences.ResetDefaults(); Check(!preferences.ShowMarkerRange,"Visual defaults disable range rings");

        var camera = new MumbleSnapshot(1,1,1,1,0,"Test",new(0,0,2),Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,1);
        var frame = new CameraProjection.Frame(camera,1000,800);
        Vector3 a = new(-2,0,4), b = new(4,0,16);
        Vector3 desired = Vector3.Lerp(a,b,.35f);
        Check(frame.TryProject(desired,out var click),"Test point projects");
        float? fraction = TrailPointInsertion.Fraction(frame,a,b,click);
        Check(fraction.HasValue && Math.Abs(fraction.Value-.35f)<.0001f,"Insertion undoes perspective distortion on sloped/depth-varying edges");
        foreach (uint ui in new uint[] { 0,4,1 })
        {
            var mapCamera = camera with { UiState = ui,Compass = new(300,300,.7f,Vector2.Zero,Vector2.Zero,2,1) };
            Check(MapRibbonProjection.TryBasis(mapCamera,1200,900,out var basis),"Map radius projection basis is available");
            float pixels = basis.X.Length()*fadeNear.Radius;
            Check(Math.Abs(pixels-62.5f)<.01f,"3000 game-inch fade ring projects to the same scale on full/rotating/nonrotating maps");
            var zoomed = mapCamera with { Compass = mapCamera.Compass! with { Scale = 4 } };
            Check(MapRibbonProjection.TryBasis(zoomed,1200,900,out var zoomBasis) && Math.Abs(zoomBasis.X.Length()*fadeNear.Radius-pixels/2)<.01f,"Map ring radius follows zoom rather than icon-size multipliers");
        }
        var acrossNear = TrailPointInsertion.Fraction(frame,new(-1,0,-1),new(1,0,5),new(500,400));
        Check(acrossNear.HasValue && float.IsFinite(acrossNear.Value),"Edges crossing the near plane are clipped safely");
        Check(TrailPointInsertion.Fraction(frame,new(0,0,-4),new(1,0,-2),new(500,400)) == null,"Edges behind the camera cannot be inserted into");
        var reference = new TrailSegmentRef(1,1);
        var far = new TrailPickTriangle(reference,new(0,0),new(100,0),new(0,100),20,20,20,1,2,a,b);
        var near = far with { Segment = new(2,3),DepthA = 5,DepthB = 5,DepthC = 5,EdgeStart = 8,EdgeEnd = 9 };
        Check(TrailPicking.HitTriangle([near,far],new(10,10))?.EdgeStart == 8,"Overlapping ribbons pick the frontmost edge rather than the last segment");
        var trail = new EditableTrail { Number = 7,Guid = "test",MapId = 1 }; var segment = trail.AddSegment();
        segment.Add(new(1,2,3)); segment.Add(new(5,2,3)); segment.Add(new(9,2,3));
        var separate = trail.AddSegment(); separate.Add(new(20,2,3)); separate.Add(new(25,2,3));
        segment.ResampleSource = segment.Points.Select(p => new TrailPoint { Id = p.Id,Position = p.Position }).ToList();
        string before = EditableTrails.Serialize(trail);
        var point = TrailPointInsertion.Insert(trail,new(7,segment.Id,1,2,.25f));
        Check(segment.Points[1] == point && point.Position == new Vector3(2,2,3) && segment.Points.Select(p => p.Id).SequenceEqual(new long[]{1,4,2,3}),"Insertion preserves endpoint IDs, position, point order and fresh identity");
        Check(segment.ResampleSource == null && separate.Points.Count == 2,"Insertion invalidates density source without modifying adjacent segments");
        string after = EditableTrails.Serialize(trail);
        var imported = EditableTrails.Import(EditableTrails.Compile(EditableTrails.Deserialize(after)),7,"test");
        Check(imported.Segments.Select(s => s.Points.Count).SequenceEqual(new[]{4,2}),"Save/compile retains inserted points and segment gaps");
        Check(EditableTrails.Deserialize(before).Segments[0].Points.Count == 3 && EditableTrails.Deserialize(after).Segments[0].Points.Count == 4,"History snapshots can restore either side of insertion");
        Reject(() => TrailPointInsertion.Insert(trail,new(7,segment.Id,1,2,.5f)),"Stale non-adjacent endpoint references were accepted");
        Reject(() => TrailPointInsertion.Insert(trail,new(7,segment.Id,4,2,0)),"Endpoint duplicates were accepted");
        Reject(() => TrailPointInsertion.Insert(trail,new(7,segment.Id,3,separate.Points[0].Id,.5f)),"Insertion bridged separate segments");
        Console.WriteLine("1.0.0 range inheritance/preferences, visible-edge picking, perspective insertion and trail persistence checks passed.");
    }
}
