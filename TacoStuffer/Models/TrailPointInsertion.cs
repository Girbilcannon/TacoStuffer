using System.Numerics;

namespace TacoStuffer.Models;

public sealed record TrailInsertionHit(long Trail,long Segment,long First,long Second,float Fraction);

public static class TrailPointInsertion
{
    // Project the centerline after clipping it, then undo perspective interpolation.
    // Linear screen interpolation alone would place points incorrectly at an angle.
    public static float? Fraction(CameraProjection.Frame camera,Vector3 a,Vector3 b,Vector2 mouse)
    {
        float start = 0, end = 1;
        for (int plane=0;plane<5;plane++)
        {
            float da = camera.PlaneDistance(a,plane), db = camera.PlaneDistance(b,plane);
            if (da < 0 && db < 0) return null;
            if ((da < 0) != (db < 0))
            {
                float t = da/(da-db);
                if (da < 0) start = Math.Max(start,t); else end = Math.Min(end,t);
            }
        }
        if (start > end) return null;
        var ca = Vector3.Lerp(a,b,start); var cb = Vector3.Lerp(a,b,end);
        if (!camera.TryProject(ca,out var sa) || !camera.TryProject(cb,out var sb)) return null;
        float length = Vector2.DistanceSquared(sa,sb);
        if (length < .000001f) return null;
        float s = Math.Clamp(Vector2.Dot(mouse-sa,sb-sa)/length,0,1);
        float za = camera.Depth(ca), zb = camera.Depth(cb);
        float tWorld = ((1-s)*start/za+s*end/zb)/((1-s)/za+s/zb);
        return float.IsFinite(tWorld) ? tWorld : null;
    }
    public static TrailPoint Insert(EditableTrail trail,TrailInsertionHit hit)
    {
        if (trail.Number != hit.Trail || !float.IsFinite(hit.Fraction) || hit.Fraction <= .00001f || hit.Fraction >= .99999f)
            throw new InvalidOperationException("Click between existing trail points, away from their endpoints.");
        var segment = trail.Segments.SingleOrDefault(s => s.Id == hit.Segment) ?? throw new InvalidOperationException("The segment no longer exists.");
        int first = segment.Points.FindIndex(p => p.Id == hit.First);
        if (first < 0 || first+1 >= segment.Points.Count || segment.Points[first+1].Id != hit.Second)
            throw new InvalidOperationException("The trail edge changed; click it again.");
        Vector3 position = Vector3.Lerp(segment.Points[first].Position,segment.Points[first+1].Position,hit.Fraction);
        if (!EditableTrails.Finite(position) || position == Vector3.Zero || position == segment.Points[first].Position || position == segment.Points[first+1].Position)
            throw new InvalidOperationException("This edge cannot accept another distinct point at that location.");
        if (segment.NextPoint <= segment.Points.Select(p => p.Id).DefaultIfEmpty(0).Max() || segment.NextPoint == long.MaxValue)
            throw new InvalidOperationException("The segment point counter is invalid or exhausted.");
        var point = new TrailPoint { Id = segment.NextPoint++,Position = position };
        segment.ResetResampling(); segment.Points.Insert(first+1,point); return point;
    }
}
