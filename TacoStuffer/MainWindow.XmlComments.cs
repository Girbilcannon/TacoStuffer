using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private void WireXmlComments(XmlPage page)
    {
        var editor = page.Editor;
        TextPointer? clicked = null;
        editor.PreviewMouseRightButtonDown += (_, e) => clicked = editor.GetPositionFromPoint(e.GetPosition(editor), true);
        var menu = new ContextMenu();
        void Command(string label, RoutedUICommand command) => menu.Items.Add(new MenuItem { Header = label, Command = command, CommandTarget = editor });
        var undo = new MenuItem { Header = "Undo" }; var redo = new MenuItem { Header = "Redo" };
        undo.Click += (_, _) => UndoProject(); redo.Click += (_, _) => RedoProject();
        menu.Items.Add(undo); menu.Items.Add(redo);
        menu.Opened += (_, _) => { FlushHistoryCheckpoint(); undo.IsEnabled = history.CanUndo; redo.IsEnabled = history.CanRedo; };
        menu.Items.Add(new Separator());
        Command("Cut", ApplicationCommands.Cut); Command("Copy", ApplicationCommands.Copy); Command("Paste", ApplicationCommands.Paste);
        Command("Select All", ApplicationCommands.SelectAll); menu.Items.Add(new Separator());
        int offset = 0;
        foreach (bool after in new[] { false, true })
        {
            var item = new MenuItem { Header = after ? "Add Comment After" : "Add Comment Before",
                ToolTip = "Insert an XML comment beside this source line. Multiline tags/comments are kept intact." };
            item.Click += (_, _) => InsertXmlComment(page, offset, after); menu.Items.Add(item);
        }
        editor.ContextMenu = menu;
        editor.ContextMenuOpening += (_, e) =>
        {
            var pointer = e.CursorLeft < 0 ? editor.CaretPosition : clicked ?? editor.CaretPosition;
            offset = new TextRange(editor.Document.ContentStart, pointer).Text.Length;
            clicked = null;
        };
    }
    private void InsertXmlComment(XmlPage page, int offset, bool after)
    {
        // Use the same text representation for clicked offsets and source lines.
        string source = HistorySource(page.Editor);
        FlushHistoryCheckpoint();
        var edit = XmlComments.Insert(source, offset, after);
        SetPageSource(page, edit.Source);
        page.Dirty = true;
        page.ReleaseSourceIndex();
        TrySyncXml();
        CommitHistoryCheckpoint("Add XML comment");
        var first = SourcePointer(page.Editor, edit.CommentOffset);
        var last = SourcePointer(page.Editor, edit.CommentOffset + XmlComments.Placeholder.Length);
        if (first != null && last != null)
        {
            page.Editor.Focus();
            page.Editor.Selection.Select(first, last);
            WorkingLineAdorner.Show(page.Editor, first, true);
        }
        StatusText.Text = "XML comment added. Replace the selected placeholder with your note.";
    }
    private static TextPointer? SourcePointer(RichTextBox editor, int offset)
    {
        for (TextPointer? p = editor.Document.ContentStart; p != null && p.CompareTo(editor.Document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            int length = p.GetTextInRun(LogicalDirection.Forward).Length;
            if (offset <= length) return p.GetPositionAtOffset(offset);
            offset -= length;
        }
        return editor.Document.ContentEnd;
    }
}
