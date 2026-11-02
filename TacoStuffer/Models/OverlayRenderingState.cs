namespace TacoStuffer.Models;

// Reapply native stacking only when visible windows/bands change or an external
// activation explicitly requests repair. Polling an unchanged HUD does no work.
public sealed class OverlayLayerState
{
    private nint[] windows = [];
    private nint occlusion;
    private bool hosted, valid;
    public void Invalidate() => valid = false;
    public bool Changed(IReadOnlyList<nint> order, nint editor, bool inApp)
    {
        if (valid && occlusion == editor && hosted == inApp && windows.SequenceEqual(order)) return false;
        windows = order.ToArray(); occlusion = editor; hosted = inApp; valid = true;
        return true;
    }
}

// Camera/geometry changes invalidate picking, but ordinary rendering never
// projects the ribbon on the CPU. A click pays that cost once for its frame.
public sealed class TrailPickCache
{
    private readonly List<TrailPickTriangle> triangles = [];
    private bool valid;
    public void Invalidate() => valid = false;
    public IReadOnlyList<TrailPickTriangle> Get(Action<List<TrailPickTriangle>> build)
    {
        if (valid) return triangles;
        triangles.Clear();
        build(triangles);
        valid = true;
        return triangles;
    }
}

// Windows can briefly report no foreground HWND while switching applications.
// Retain a known target for at most 100 ms; an observed unrelated HWND wins
// immediately so the overlay never waits to yield to another application.
public sealed class OverlayForegroundState
{
    private nint last;
    private long observedAt;
    public nint Observe(nint current, long now)
    {
        if (current != 0) { last = current; observedAt = now; return current; }
        return now >= observedAt && now - observedAt <= 100 ? last : 0;
    }
}
