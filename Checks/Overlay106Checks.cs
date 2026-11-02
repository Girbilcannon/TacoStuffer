using System.Diagnostics;
using System.Numerics;
using TacoStuffer.Models;

internal static class Overlay106Checks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        var layers = new OverlayLayerState();
        nint[] visible = [10, 20, 30, 40]; // Controls, toolbar, Info, Copy.
        Check(layers.Changed(visible, 0, false), "Initial visible HUDs need one stacking pass");
        int repairs = 0;
        for (int poll = 0; poll < 1000; poll++) if (layers.Changed(visible, 0, false)) repairs++;
        Check(repairs == 0, "Stable polling never reorders native HUD windows");
        Check(layers.Changed([10, 20, 40], 0, false), "HUD dismissal changes presentation once");
        Check(layers.Changed([20, 40], 0, false), "Minimized/hidden controls leave toolbar and HUDs visible");
        Check(layers.Changed([20, 40], 90, false), "Editor occlusion changes the stacking band");
        Check(layers.Changed([20, 40], 90, true), "Play in App changes the stacking band");
        layers.Invalidate();
        Check(layers.Changed([20, 40], 90, true) && !layers.Changed([20, 40], 90, true), "External activation requests one repair, then settles");

        var cache = new TrailPickCache(); int builds = 0;
        var reference = new TrailSegmentRef(1, 1);
        void Build(List<TrailPickTriangle> list)
        { builds++; list.Add(new(reference, new(0, 0), new(100, 0), new(0, 100), 5, 5, 5, 1, 2)); }
        for (int frame = 0; frame < 1000; frame++) cache.Invalidate();
        Check(builds == 0, "Camera redraws without clicks perform no CPU ribbon picking projection");
        var first = cache.Get(Build);
        Check(TrailPicking.Hit(first, new(10, 10)) == reference && builds == 1, "The first click builds accurate ribbon picking data");
        Check(ReferenceEquals(first, cache.Get(Build)) && builds == 1, "Repeated picks reuse the current projection");
        cache.Invalidate();
        cache.Get(list => { builds++; list.Add(new(new(2, 2), new(200, 200), new(300, 200), new(200, 300), 5, 5, 5)); });
        Check(builds == 2 && TrailPicking.Hit(cache.Get(Build), new(10, 10)) == null &&
            TrailPicking.Hit(cache.Get(Build), new(210, 210)) == new TrailSegmentRef(2, 2),
            "Camera/geometry invalidation replaces stale pick data before another click");
        cache.Invalidate();
        try { cache.Get(_ => throw new InvalidOperationException("test")); } catch (InvalidOperationException) { }
        cache.Get(Build); Check(builds == 3, "An interrupted projection is retried rather than cached as complete");

        // Conservative CPU microbenchmark: the previous render path projected
        // ribbon triangles on every frame. No WPF tessellation cost is included.
        var camera = new MumbleSnapshot(1,1,1,1,0,"Benchmark",new(0,1,5),new(0,4,-10),Vector3.UnitZ,Vector3.UnitY,1);
        var frameProjection = new CameraProjection.Frame(camera, 1920, 1080);
        var eager = new List<TrailPickTriangle>(); int projections = 0;
        void Project(List<TrailPickTriangle> list)
        {
            projections++;
            Span<RibbonVertex> clipped = stackalloc RibbonVertex[12];
            for (int trail = 0; trail < 40; trail++)
            for (int edge = 0; edge < 500; edge++)
            {
                Vector3 a = new(trail-20, 1, 5+edge*.1f), b = a+new Vector3(.02f,0,.1f), side = new(.3f,0,0);
                int count = RibbonProjection.Clip(frameProjection, new(a-side,default), new(a+side,default), new(b+side,default), clipped);
                for(int i=1;i<count-1;i++)
                    if(frameProjection.TryProject(clipped[0].Position,out var x) && frameProjection.TryProject(clipped[i].Position,out var y) && frameProjection.TryProject(clipped[i+1].Position,out var z))
                        list.Add(new(new(trail+1,1),x,y,z,frameProjection.Depth(clipped[0].Position),frameProjection.Depth(clipped[i].Position),frameProjection.Depth(clipped[i+1].Position)));
            }
        }
        Project(eager); // Warm JIT and capacity.
        var timer=Stopwatch.StartNew();
        for(int i=0;i<60;i++){eager.Clear();Project(eager);}
        double eagerMs=timer.Elapsed.TotalMilliseconds;
        var lazy=new TrailPickCache(); projections=0;timer.Restart();
        for(int i=0;i<60;i++)lazy.Invalidate();
        double idleMs=timer.Elapsed.TotalMilliseconds;
        Check(projections==0,"Dense-scene idle redraws leave the expensive projection builder untouched");
        var clicked=lazy.Get(Project);
        Check(projections==1 && clicked.SequenceEqual(eager),"On-demand projection preserves the dense scene's exact triangle data");
        Console.WriteLine($"1.0.6 CPU picking benchmark: 20,000 edges x 60 redraws eager={eagerMs:F1} ms; idle invalidation={idleMs:F3} ms; idle projection builds=0; click triangles={clicked.Count}. Not a game FPS measurement.");
        Console.WriteLine("1.0.6 layer settling, lazy picking, invalidation and dense projection checks passed.");
    }
}
