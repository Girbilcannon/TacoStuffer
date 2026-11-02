namespace TacoStuffer.Models;

public sealed record OutlinerBranch(string Id, int Depth, bool HasChildren, bool ExpandRecursively = true);

public static class OutlinerExpansion
{
    public static void Toggle(IReadOnlyList<OutlinerBranch> rows, string id, ISet<string> collapsed, bool recursive)
    {
        int index = -1;
        for (int i = 0; i < rows.Count; i++) if (rows[i].Id == id) { index = i; break; }
        if (index < 0 || !rows[index].HasChildren) return;
        var root = rows[index];
        bool opening = collapsed.Contains(id);
        // Shift-expand stops at segment rows; points remain an explicit single-row action.
        if (recursive && opening && !root.ExpandRecursively) return;
        if (opening) collapsed.Remove(id); else collapsed.Add(id);
        for (int i = index + 1; i < rows.Count && rows[i].Depth > root.Depth; i++)
        {
            var child = rows[i];
            if (!child.HasChildren) continue;
            if (recursive)
            {
                if (opening && child.ExpandRecursively) collapsed.Remove(child.Id);
                else collapsed.Add(child.Id);
            }
            // A normal open exposes one level, even after a previous recursive open.
            else if (opening && child.Depth == root.Depth + 1) collapsed.Add(child.Id);
        }
    }
}
