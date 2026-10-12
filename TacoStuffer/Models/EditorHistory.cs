using System.Text.Json;

namespace TacoStuffer.Models;

public sealed record HistoryXml(string Name, string Xml, string Source, bool Open);

public sealed record EditorState(List<HistoryXml> Documents, List<string> Trails, List<string> CategoryOrder,
    Dictionary<string, string> TextureLabels, long NextMarker, long NextTrail)
{
    // View-only tab changes do not create an edit.
    public string Fingerprint() => JsonSerializer.Serialize(new
    {
        Documents = Documents.Select(d => new { d.Name, d.Xml, d.Source }), Trails, CategoryOrder,
        TextureLabels = TextureLabels.OrderBy(p => p.Key).ToArray(), NextMarker, NextTrail
    });
}

public sealed record HistoryEntry<T>(string Label, T Before, T After, long Weight);

public sealed class HistoryJournal<T>(Func<T, string> fingerprint, int limit = 100, long byteLimit = 64 * 1024 * 1024)
{
    private readonly List<HistoryEntry<T>> undo = [], redo = [];
    private T current = default!;
    private string currentKey = "";
    public bool Initialized { get; private set; }
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public IReadOnlyList<HistoryEntry<T>> UndoEntries => undo;
    public IReadOnlyList<HistoryEntry<T>> RedoEntries => redo;
    public void Reset(T state)
    {
        undo.Clear();
        redo.Clear();
        current = state;
        currentKey = fingerprint(state);
        Initialized = true;
    }

    public bool Commit(T state, string label)
    {
        if (!Initialized)
        {
            Reset(state);
            return false;
        }
        string key = fingerprint(state);
        if (key == currentKey) return false;
        undo.Add(new(label, current, state, ((long)currentKey.Length + key.Length) * 2));
        redo.Clear();
        current = state;
        currentKey = key;
        while (undo.Count > 1 && (undo.Count > Math.Max(1, limit) || undo.Sum(e => e.Weight) > byteLimit)) undo.RemoveAt(0);
        return true;
    }

    public T Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("Nothing to undo.");
        var entry = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(entry);
        current = entry.Before;
        currentKey = fingerprint(current);
        return current;
    }

    public T Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("Nothing to redo.");
        var entry = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(entry);
        current = entry.After;
        currentKey = fingerprint(current);
        return current;
    }
}
