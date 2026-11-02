namespace TacoStuffer.Models;

// Native observation is queued, while WPF may handle the same press immediately.
// Remember consumed presses so their delayed observer callbacks cannot hit-test
// an old cursor position against a newly moved manipulator and clear selection.
public sealed class OverlayPressTracker
{
    private long latest, consumed;
    public long Current => System.Threading.Interlocked.Read(ref latest);
    public long ObservePress() => System.Threading.Interlocked.Increment(ref latest);
    public void ConsumeCurrent() => System.Threading.Interlocked.Exchange(ref consumed,System.Threading.Interlocked.Read(ref latest));
    public bool IsConsumed(long press) => press > 0 && press <= System.Threading.Interlocked.Read(ref consumed);
}
