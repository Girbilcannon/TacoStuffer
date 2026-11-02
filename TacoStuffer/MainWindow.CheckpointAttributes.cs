using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using TacoStuffer.Models;

namespace TacoStuffer;

public partial class MainWindow
{
    private readonly Dictionary<string,CheckpointAttributeDraft> checkpointAttributeDrafts = [];
    private readonly HashSet<string> pendingCheckpointResets = [];
    private string CheckpointDraftKey(XElement element) => (projectFile ?? ProjectTitle.Text) + "|" + pages.FirstOrDefault(p=>p.Xml==element.Document)?.Name + "|" + ElementKey(element);
    private CheckpointAttributeDraft CheckpointDraft(XElement element)
    {
        string key = CheckpointDraftKey(element);
        if (!checkpointAttributeDrafts.TryGetValue(key,out var draft)) checkpointAttributeDrafts[key] = draft = new();
        return draft;
    }
    private void SetCheckpointEnabled(bool enabled)
    {
        if (!TrySyncXml()) return;
        var targets = AttributeTargets().Where(t=>AttributeApplies(t,"isCheckpoint")).ToList();
        var group = catalog.Groups.First(g=>g.Key=="checkpoint");
        FlushHistoryCheckpoint();
        foreach(var target in targets)
        {
            var defaults = group.Members.Where(m=>CheckpointAttributeEditing.Fields.Contains(m.Name))
                .ToDictionary(m=>m.Name,m=>EffectiveValue(target,m.Name,m.DefaultValue));
            var values = CheckpointAttributeEditing.Toggle(target,enabled,CheckpointDraft(target),defaults);
            AttributeRules.Apply([target],values,AttributeApplies);
        }
        WritePages();
    }
    private void SetCheckpointResetEnabled(bool enabled)
    {
        if (!TrySyncXml()) return;
        FlushHistoryCheckpoint();
        foreach(var target in AttributeTargets().Where(t=>AttributeApplies(t,"cpReset")))
        {
            var draft=CheckpointDraft(target); string key=CheckpointDraftKey(target);
            if (enabled)
            {
                pendingCheckpointResets.Add(key);
                SetValue(target,"cpReset",draft.ResetGuid ?? EffectiveValue(target,"cpReset",""));
                if (draft.ResetMessage != null && !AttributePaneRules.HasLocal(target,"resetMessage")) SetValue(target,"resetMessage",draft.ResetMessage);
            }
            else
            {
                draft.ResetGuid=EffectiveValue(target,"cpReset","");
                string message = EffectiveValue(target,"resetMessage","\0");
                draft.ResetMessage = message == "\0" ? null : message;
                pendingCheckpointResets.Remove(key);
                // An empty local value masks an inherited reset action, if present.
                var inherited=AttributeRules.Inherited(target,CategoryValues()).Any(a=>a.Name.Equals("cpReset",StringComparison.OrdinalIgnoreCase));
                SetValue(target,"cpReset",inherited ? "" : null);
                if (EffectiveValue(target,"resetguid","").Length == 0) SetValue(target,"resetMessage",null);
            }
        }
        WritePages();
    }
    private void ShowCheckpointAttributes(XElement element, AttributeDefinition definition)
    {
        AttributeSection(element,definition);
        bool enabled=VisualAttributeMath.Enabled(EffectiveValue(element,"isCheckpoint","false"));
        var checkpoint=new CheckBox { Content="Is Checkpoint",IsChecked=enabled };
        checkpoint.Click+=(_,_)=>SetCheckpointEnabled(checkpoint.IsChecked==true);
        AttributesHost.Children.Add(checkpoint);
        if (enabled)
        {
            foreach(var member in definition.Members.Where(m=>CheckpointAttributeEditing.Fields.Contains(m.Name)))
            {
                string value=EffectiveValue(element,member.Name,member.DefaultValue);
                if (AttributeColor.IsColor(member.Name,member.Type))
                    ColorAttributeField(new XElement(element.Name,new XAttribute(member.Name,value)),member);
                else Field(member.Name,value,text=>ChangeAttribute(member.Name,text));
                if (member.Name=="nextCheck")
                {
                    var choose=new Button { Content="Choose next checkpoints…" };
                    choose.Click+=(_,_)=>ChooseResetTargets(element,"nextCheck"); AttributesHost.Children.Add(choose);
                }
            }
        }
        bool resetEnabled=EffectiveValue(element,"cpReset","").Length>0 || (AttributePaneRules.HasLocal(element,"cpReset") && pendingCheckpointResets.Contains(CheckpointDraftKey(element)));
        var reset=new CheckBox { Content="Reset",IsChecked=resetEnabled,Margin=new Thickness(0,10,0,4),
            ToolTip="Enable a reset action targeting the first checkpoint GUID. This can be used without Is Checkpoint." };
        reset.Click+=(_,_)=>SetCheckpointResetEnabled(reset.IsChecked==true); AttributesHost.Children.Add(reset);
        if (resetEnabled)
        {
            Field("cpReset",EffectiveValue(element,"cpReset",""),text=>ChangeAttribute("cpReset",text));
            var choose=new Button { Content="Choose first checkpoint…" };
            choose.Click+=(_,_)=>ChooseResetTargets(element,"cpReset"); AttributesHost.Children.Add(choose);
            AddResetMessageControls(element);
        }
        var remove=new Button { Content="Remove Checkpoint / Reset controls",Margin=new Thickness(0,8,0,4) };
        remove.Click+=(_,_)=>
        {
            if (!TrySyncXml()) return;
            foreach(var target in AttributeTargets().Where(t=>definition.Applies(t)))
            {
                pendingCheckpointResets.Remove(CheckpointDraftKey(target));
                var values=definition.Members.ToDictionary(m=>m.Name,_=>(string?)null);
                if (EffectiveValue(target,"resetguid","").Length == 0) values["resetMessage"]=null;
                AttributeRules.Apply([target],values,AttributeApplies);
            }
            WritePages();
        };
        AttributesHost.Children.Add(remove);
    }
}
