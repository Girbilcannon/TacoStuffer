using System.Xml.Linq;
using TacoStuffer.Models;

internal static class CheckpointPane023Checks
{
    private static void Check(bool value,string reason) { if(!value) throw new Exception(reason); }
    public static void Run()
    {
        var element=XElement.Parse("<POI isCheckpoint='true' cpColor='00FF0080' cpPreColor='80808040' nextCheckCount='4' nextCheck='next-guid' cpRadius='4.5' cpReset='root-guid' resetMessage='Again!' cpFinish='true' triggerRange='3' autoTrigger='1' name='race'/>");
        var draft=new CheckpointAttributeDraft();
        var defaults=new Dictionary<string,string> { ["cpColor"]="FFFFFFFF",["cpPreColor"]="FFFFFFFF",["nextCheckCount"]="2",["nextCheck"]="",["cpRadius"]="2" };
        var original=element.Attributes().ToDictionary(a=>a.Name.LocalName,a=>a.Value);
        var off=CheckpointAttributeEditing.Toggle(element,false,draft,defaults);
        AttributeRules.Apply([element],off,(_,_)=>true);
        Check((string?)element.Attribute("isCheckpoint")=="false" && CheckpointAttributeEditing.Fields.All(f=>element.Attribute(f)==null),"Unchecking Is Checkpoint removes its authored fields rather than leaving unused XML");
        Check((string?)element.Attribute("cpReset")=="root-guid" && (string?)element.Attribute("resetMessage")=="Again!","Turning off checkpoint participation preserves reset-only behavior and message");
        Check((string?)element.Attribute("cpFinish")=="true" && (string?)element.Attribute("triggerRange")=="3" && (string?)element.Attribute("autoTrigger")=="1","Independent finish and ordinary trigger attributes are not stripped");
        var serialized=element.ToString(); element=XElement.Parse(serialized);
        AttributeRules.Apply([element],CheckpointAttributeEditing.Toggle(element,true,draft,defaults),(_,_)=>true);
        Check(element.Attributes().All(a=>original[a.Name.LocalName]==a.Value) && element.Attributes().Count()==original.Count,"Rechecking restores every cached value exactly, even after XML element reconstruction");
        element.SetAttributeValue("cpRadius","9");
        Check(CheckpointAttributeEditing.Toggle(element,true,draft,defaults)["cpRadius"]=="9","Re-adding an active checkpoint does not replace newer edits with cached values");
        var fresh=XElement.Parse("<POI cpReset='root-guid'/>");
        AttributeRules.Apply([fresh],CheckpointAttributeEditing.Toggle(fresh,true,new(),defaults),(_,_)=>true);
        Check((string?)fresh.Attribute("isCheckpoint")=="true" && (string?)fresh.Attribute("cpRadius")=="2" && (string?)fresh.Attribute("cpReset")=="root-guid","Enabling a reset-only marker creates checkpoint defaults without losing its root");
        var partial=XElement.Parse("<POI isCheckpoint='true' cpRadius='6'/>"); var partialDraft=new CheckpointAttributeDraft();
        AttributeRules.Apply([partial],CheckpointAttributeEditing.Toggle(partial,false,partialDraft,defaults),(_,_)=>true);
        AttributeRules.Apply([partial],CheckpointAttributeEditing.Toggle(partial,true,partialDraft,defaults),(_,_)=>true);
        Check((string?)partial.Attribute("cpRadius")=="6" && partial.Attribute("cpColor")==null,"An authored partial group stays partial after off/on; omitted inherited/default values aren't fabricated");
        Check(HostedCodeLayout.DockedWidth(1200,1.0/3)==400,"Code editor starts at one third of hosted viewport");
        double expanded=HostedCodeLayout.ResizeFraction(1.0/3,-200,1,1200);
        Check(HostedCodeLayout.DockedWidth(1200,expanded)==600,"Dragging left increases width while the right edge stays anchored");
        Check(Math.Abs(HostedCodeLayout.ResizeFraction(.5,100,2,1200)-1.0/3)<.00001,"Dock resizing converts DIP drag to physical pixels at high DPI");
        Check(HostedCodeLayout.ResizeFraction(.5,-10000,1,1200)==.95 && HostedCodeLayout.DockedWidth(1200,.01)==240,"Docked width is bounded and maintains a usable minimum");
        Check(HostedCodeLayout.DockedWidth(100,.3)==100 && HostedCodeLayout.DockedWidth(0,.3)==1,"Small or transitional viewport bounds remain valid");
    }
}
