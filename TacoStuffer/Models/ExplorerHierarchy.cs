namespace TacoStuffer.Models;

public static class ExplorerHierarchy
{
    public static List<ExplorerRow> Prepare(IReadOnlyList<ExplorerRow> rows, ISet<string> collapsed)
    {
        var result = new List<ExplorerRow>();
        var stack = new List<ExplorerRow>();
        // Nearest following row at this depth or shallower, in a single reverse pass.
        var nextAtLevel = new int[rows.Count];
        var following = new Stack<int>();
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            while (following.Count > 0 && rows[following.Peek()].Depth > rows[i].Depth) following.Pop();
            nextAtLevel[i] = following.Count > 0 ? following.Peek() : rows.Count;
            following.Push(i);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            while (stack.Count > row.Depth) stack.RemoveAt(stack.Count - 1);
            int next = nextAtLevel[i];
            bool sibling = next < rows.Count && rows[next].Depth == row.Depth && row.Depth > 0;
            var prepared = row with
            {
            ParentId = row.Depth > 0 ? stack[^1].RowId : null,
            HasChildren = row.HasChildren || i + 1 < rows.Count && rows[i + 1].Depth > row.Depth,
            Expanded = !collapsed.Contains(row.RowId), HasNextSibling = sibling,
            AncestorLines = stack.Skip(1).Select(p => p.HasNextSibling).ToList()
            };
            result.Add(prepared);
            stack.Add(prepared);
        }
        return result;
    }

    public static List<ExplorerRow> Visible(IReadOnlyList<ExplorerRow> rows)
    {
        var result = new List<ExplorerRow>();
        int hiddenBelow = int.MaxValue;
        foreach (var row in rows)
        {
            if (row.Depth > hiddenBelow) continue;
            hiddenBelow = int.MaxValue;
            result.Add(row);
            if (row.HasChildren && !row.Expanded) hiddenBelow = row.Depth;
        }
        return result;
    }
}
