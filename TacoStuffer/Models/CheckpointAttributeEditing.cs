using System.Xml.Linq;

namespace TacoStuffer.Models;

// Authoring-only draft: disabled settings never leak into exported marker XML.
public sealed class CheckpointAttributeDraft
{
    public Dictionary<string,string>? Fields { get; set; }
    public string? ResetGuid { get; set; }
    public string? ResetMessage { get; set; }
}

public static class CheckpointAttributeEditing
{
    public static readonly string[] Fields = ["cpColor", "cpPreColor", "nextCheckCount", "nextCheck", "cpRadius"];
    public static Dictionary<string,string?> Toggle(XElement element, bool enabled, CheckpointAttributeDraft draft,
        IReadOnlyDictionary<string,string> defaults)
    {
        var values = new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase) { ["isCheckpoint"] = enabled ? "true" : "false" };
        if (!enabled)
        {
            draft.Fields = element.Attributes().Where(a => Fields.Contains(a.Name.LocalName,StringComparer.OrdinalIgnoreCase))
                .ToDictionary(a=>a.Name.LocalName,a=>a.Value,StringComparer.OrdinalIgnoreCase);
            foreach(string field in Fields) values[field] = null;
        }
        else
        {
            foreach(var pair in (IReadOnlyDictionary<string,string>?)draft.Fields ?? defaults)
                values[pair.Key] = element.Attributes().FirstOrDefault(a=>a.Name.LocalName.Equals(pair.Key,StringComparison.OrdinalIgnoreCase))?.Value ?? pair.Value;
        }
        // cpReset, resetMessage, ordinary triggers and independent cpFinish are untouched.
        return values;
    }
}
