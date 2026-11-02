using System.Numerics;
using TacoStuffer.Models;

internal static class Overlay104Checks
{
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Invalid topology edit accepted");}
    public static void Run()
    {
        var gesture=new OverlayClickGesture();
        gesture.Begin(new(10,10),100,false);Check(gesture.End(new(11,12),180,false),"A brief click/release selects despite tiny mouse jitter");
        gesture.Begin(new(10,10),100,false);gesture.Move(new(40,10));Check(!gesture.End(new(10,10),180,false),"Camera warp-back cannot turn an orbit into a deselecting click");
        gesture.Begin(new(10,10),100,false);Check(!gesture.End(new(10,10),800,false),"A stationary held camera button is not a click");
        gesture.Begin(new(10,10),100,false);gesture.OtherButton();Check(!gesture.End(new(10,10),180,false),"A released movement chord is not a click");
        gesture.Begin(new(10,10),100,true);Check(!gesture.End(new(10,10),180,false),"An already-held second mouse button prevents deselection");
        gesture.Begin(new(10,10),100,false);gesture.Cancel();Check(!gesture.End(new(10,10),180,false),"Hidden/cancelled gestures never commit");
        Check(OverlaySelection.Combine(["a","b"],["b","c"],false,false).SequenceEqual(["b","c"]),"Unmodified selection replaces");
        Check(OverlaySelection.Combine(["a","b"],["b","c"],true,false).SequenceEqual(["a","b","c"]),"Shift adds without duplicates");
        Check(OverlaySelection.Combine(["a","b"],["b","c"],false,true).SequenceEqual(["a","c"]),"Ctrl toggles a multi-point selection");
        Check(OverlaySelection.Combine(["a"],[],true,false).SequenceEqual(["a"]),"Empty additive marquee retains the selection");

        EditableTrail Create(long number,float offset=0)
        {
            var t=new EditableTrail{Number=number,Guid="trail"+number,MapId=15};var s=t.AddSegment();
            for(int i=0;i<5;i++)s.Add(new(offset+i+1,2,3));return t;
        }
        var split=Create(1);var splitSeg=split.Segments[0];string before=EditableTrails.Serialize(split);
        splitSeg.ResampleSource=splitSeg.Points.Select(p=>new TrailPoint{Id=p.Id,Position=p.Position}).ToList();
        TrailTopology.Remove(split,new(1,splitSeg.Id,splitSeg.Points[2].Id),true);
        Check(split.Segments.Count==2 && split.Segments.All(s=>s.Points.Count==2),"Split removes one interior point and creates two disconnected runs");
        Check(split.Segments.All(s=>s.ResampleSource==null),"Split cannot resurrect deleted points through resampling");
        Check(EditableTrails.Deserialize(before).Segments.Single().Points.Count==5,"Pre-edit history snapshot restores original geometry");
        var path=Path.Combine(Path.GetTempPath(),"taco104-"+Guid.NewGuid()+".trl");
        try
        {
            File.WriteAllBytes(path,EditableTrails.Compile(split));
            var imported=EditableTrails.Import(File.ReadAllBytes(path),10,"imported");
            Check(imported.Segments.Count==2 && imported.Segments.Sum(s=>s.Points.Count)==4,"The split gap survives TRL export and import");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        var remove=Create(2);TrailTopology.Remove(remove,new(2,1,3),false);
        Check(remove.Segments.Count==1 && remove.Segments[0].Points.Select(p=>p.Id).SequenceEqual([1L,2L,4L,5L]),"Remove reconnects adjacent surviving points without renumbering them");
        string unchanged=EditableTrails.Serialize(remove);Reject(()=>TrailTopology.Remove(remove,new(2,1,1),true));
        Check(EditableTrails.Serialize(remove)==unchanged,"Splitting an endpoint rejects without mutation");
        foreach(bool firstStart in new[]{false,true})foreach(bool secondEnd in new[]{false,true})
        {
            var first=Create(3);var second=Create(4,10);
            var target=first.Segments[0];var donor=second.Segments[0];
            var a=new TrailPointRef(3,1,firstStart?1:5);var b=new TrailPointRef(4,1,secondEnd?5:1);
            var left=target.Points.Select(p=>p.Position).ToList();
            var right=donor.Points.Select(p=>p.Position).ToList();
            var seam=TrailTopology.Merge([first,second],a,b);
            Check(target.Points.Select(p=>p.Position).SequenceEqual(left.Concat(right)),"All four endpoint choices retain hierarchy flow without reversing either segment");
            Check(target.Points.Select(p=>p.Id).Distinct().Count()==10 && second.Segments.Count==0 && first.Guid=="trail3" && second.Guid=="trail4","Cross-trail merge uses unique IDs and preserves both trail identities");
            Check(seam.Point==5 && TrailPointRef.Parse(seam.Key)==seam,"Merge selects the retained seam point");
            EditableTrails.Validate(first);EditableTrails.Validate(second);
            Check(EditableTrails.Deserialize(EditableTrails.Serialize(first)).Segments.Single().Points.Count==10,"Merged trail persists without point loss");
        }
        var same=Create(5);var extra=same.AddSegment();extra.Add(new(5,2,3));extra.Add(new(6,2,3));
        TrailTopology.Merge([same],new(5,1,5),new(5,2,1));
        Check(same.Segments.Count==1 && same.Segments[0].Points.Count==6,"Same-trail merge removes a coincident duplicate seam");
        var valid=Create(6);var wrong=Create(7,10);wrong.MapId=16;string original=EditableTrails.Serialize(valid);
        Reject(()=>TrailTopology.Merge([valid,wrong],new(6,1,5),new(7,1,1)));
        Reject(()=>TrailTopology.Merge([valid],new(6,1,2),new(6,1,5)));
        Check(EditableTrails.Serialize(valid)==original,"Invalid map/segment/endpoint merges leave geometry intact");
        Console.WriteLine("1.0.4 gesture, selection and trail topology checks passed.");
    }
}
