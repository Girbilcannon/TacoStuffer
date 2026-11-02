using TacoStuffer.Models;

namespace TacoStuffer;

public partial class MainWindow
{
    private bool trailInsertToolActive;
    private uint trailInsertMap;
    private void StopTrailInsertTool()
    {
        overlayEditTool=OverlayEditTool.Select;trailInsertToolActive=false;mergeEndpoints.Clear();
        worldMarkers?.SetEditTool(OverlayEditTool.Select);UpdateOverlayToolbar();
    }
    private void InsertTrailPoint(TrailInsertionHit hit)
    {
        if (!trailInsertToolActive || workspaceBusy || !mumbleSession.IsPlayable || !TrySyncXml()) return;
        if (trailRecording) { StatusText.Text = "Stop trail recording before inserting points."; return; }
        var trail = editableTrails.FirstOrDefault(t => t.Number == hit.Trail);
        if (trail == null) { StatusText.Text = "Convert this reference-only TRL to an editable trail before inserting points."; overlay?.RecordingResult(StatusText.Text); return; }
        if (trail.MapId != gameFrame?.MapId) return;
        try
        {
            FlushHistoryCheckpoint();
            var point = TrailPointInsertion.Insert(trail,hit);
            worldMarkers?.InvalidateTrails();
            RefreshExplorer();
            CommitHistoryCheckpoint("Insert trail point");
            StatusText.Text = "Inserted point " + point.Id + " · Trail point insert tool active. Esc exits.";
        }
        catch (InvalidOperationException ex) { StatusText.Text = ex.Message; overlay?.RecordingResult(ex.Message); }
    }
}
