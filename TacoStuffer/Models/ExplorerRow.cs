using System.Windows;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record ExplorerRow(string Label, string DocumentName, XElement? Element, int Depth, string Kind, string Key)
{
    public Thickness Indent => new(Depth * 22, 0, 0, 0);
    public string RowId => DocumentName + "|" + Key;
    public string? ParentId { get; init; }
    public bool HasChildren { get; init; }
    public bool Expanded { get; init; } = true;
    public bool HasNextSibling { get; init; }
    public IReadOnlyList<bool> AncestorLines { get; init; } = [];
}

public sealed record MoveRequest(string Operation, IReadOnlyList<ExplorerRow> Selection, string Destination);
