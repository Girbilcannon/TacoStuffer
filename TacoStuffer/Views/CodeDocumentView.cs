using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

public sealed class CodeDocumentView : UserControl
{
    public TextEditor Editor { get; }
    private string name;
    public string DocumentName => name;
    private readonly Func<string?> projectFolder;
    private Window? previewWindow;
    private WebBrowser? browser;

    public CodeDocumentView(string name, string source, Func<string?> projectFolder)
    {
        this.name = name;
        this.projectFolder = projectFolder;
        var kind = ProjectDocuments.Kind(name);
        Editor = new TextEditor {
            FontFamily = new FontFamily("Consolas"), FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(21, 26, 34)),
            Foreground = new SolidColorBrush(Color.FromRgb(219, 228, 240)),
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(120, 135, 150)),
            ShowLineNumbers = kind != ProjectDocumentKind.PlainText,
            WordWrap = kind != ProjectDocumentKind.Lua,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14), Text = source
        };
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Document.UndoStack.ClearAll();
        if (kind == ProjectDocumentKind.Lua)
        {
            using var reader = XmlReader.Create(new StringReader(LuaHighlighting));
            Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        var layout = new DockPanel();
        if (kind == ProjectDocumentKind.Markdown)
        {
            var toolbar = new WrapPanel { Margin = new Thickness(6) };
            void Add(string label, string tooltip, Action action)
            {
                var button = new Button { Content = label, ToolTip = tooltip, Margin = new Thickness(2), Padding = new Thickness(8, 4, 8, 4), Focusable = false };
                button.Click += (_, _) => action();
                toolbar.Children.Add(button);
            }
            Add("H1", "Heading 1", () => PrefixLines("# "));
            Add("H2", "Heading 2", () => PrefixLines("## "));
            Add("B", "Bold", () => Wrap("**", "**", "bold text"));
            Add("I", "Italic", () => Wrap("*", "*", "italic text"));
            Add("S̶", "Strikethrough", () => Wrap("~~", "~~", "text"));
            Add("Link", "Insert link", () => Wrap("[", "](https://example.com)", "link text"));
            Add("Image", "Insert image", () => Wrap("![", "](https://example.com/image.png)", "image description"));
            Add("• List", "Bullet list", () => PrefixLines("- "));
            Add("1. List", "Numbered list", () => PrefixLines("1. "));
            Add("☐", "Task list", () => PrefixLines("- [ ] "));
            Add("Quote", "Blockquote", () => PrefixLines("> "));
            Add("Code", "Inline code", () => Wrap("`", "`", "code"));
            Add("Block", "Fenced code block", () => Wrap("\n```\n", "\n```\n", "code"));
            Add("Table", "Insert table", () => Insert("\n| Heading | Heading |\n| --- | --- |\n| Cell | Cell |\n"));
            Add("Preview", "Preview the current Markdown source", ShowPreview);
            DockPanel.SetDock(toolbar, Dock.Top);
            layout.Children.Add(toolbar);
        }
        layout.Children.Add(Editor);
        Content = layout;
    }

    private void Insert(string text)
    {
        Editor.Document.Replace(Editor.SelectionStart, Editor.SelectionLength, text);
        Editor.Focus();
    }

    private void Wrap(string before, string after, string placeholder)
    {
        int offset = Editor.SelectionStart;
        string text = Editor.SelectionLength == 0 ? placeholder : Editor.SelectedText;
        Editor.Document.Replace(offset, Editor.SelectionLength, before + text + after);
        Editor.Select(offset + before.Length, text.Length);
        Editor.Focus();
    }

    private void PrefixLines(string prefix)
    {
        int start = Editor.Document.GetLineByOffset(Editor.SelectionStart).LineNumber;
        int endOffset = Editor.SelectionStart + Editor.SelectionLength;
        if (Editor.SelectionLength > 0 && endOffset > 0 && Editor.Document.GetLineByOffset(endOffset).Offset == endOffset) endOffset--;
        int end = Editor.Document.GetLineByOffset(endOffset).LineNumber;
        using (Editor.Document.RunUpdate())
            for (int i = end; i >= start; i--) Editor.Document.Insert(Editor.Document.GetLineByNumber(i).Offset, prefix);
        Editor.Focus();
    }

    private void ShowPreview()
    {
        try { ShowPreviewCore(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ClosePreview();
            MessageBox.Show(Window.GetWindow(this), "The Markdown source is still available for editing. Preview could not open: " + ex.Message,
                "Markdown Preview", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ShowPreviewCore()
    {
        if (previewWindow == null)
        {
            browser = new WebBrowser();
            browser.Navigating += (_, e) =>
            {
                if (MarkdownPreview.IsInternalNavigation(e.Uri)) return;
                e.Cancel = true;
                if (e.Uri?.Scheme is "https" or "http" or "mailto")
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Debug.WriteLine(ex); }
                }
            };
            previewWindow = new Window { Title = name + " — Markdown Preview", Width = 900, Height = 700,
                Owner = Window.GetWindow(this), Content = browser, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            previewWindow.Closed += (_, _) =>
            {
                (browser as IDisposable)?.Dispose();
                browser = null;
                previewWindow = null;
            };
        }
        previewWindow.Show();
        browser!.NavigateToString(MarkdownPreview.Html(Editor.Text, name, projectFolder()));
        previewWindow.Activate();
    }

    public void RenameDocument(string newName)
    {
        ClosePreview();
        name = newName;
    }

    public void ClosePreview() => previewWindow?.Close();

    // Highlight only; no Lua parser/runtime or pack/XML mutation is involved.
    private const string LuaHighlighting = """
        <SyntaxDefinition name="Lua" extensions=".lua" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#788796" />
          <Color name="String" foreground="#E3BE7B" />
          <Color name="Keyword" foreground="#C792EA" fontWeight="bold" />
          <Color name="Number" foreground="#F2A97F" />
          <Color name="Builtin" foreground="#88C7BB" />
          <RuleSet>
            <Span color="Comment" multiline="true"><Begin>--\[\[</Begin><End>\]\]</End></Span>
            <Span color="Comment" multiline="true"><Begin>--\[=\[</Begin><End>\]=\]</End></Span>
            <Span color="Comment" multiline="true"><Begin>--\[==\[</Begin><End>\]==\]</End></Span>
            <Span color="Comment"><Begin>--</Begin></Span>
            <Span color="String" multiline="true"><Begin>\[\[</Begin><End>\]\]</End></Span>
            <Span color="String" multiline="true"><Begin>\[=\[</Begin><End>\]=\]</End></Span>
            <Span color="String" multiline="true"><Begin>\[==\[</Begin><End>\]==\]</End></Span>
            <Span color="String"><Begin>"</Begin><End>"</End><RuleSet><Span begin="\\" end="." /></RuleSet></Span>
            <Span color="String"><Begin>'</Begin><End>'</End><RuleSet><Span begin="\\" end="." /></RuleSet></Span>
            <Keywords color="Keyword">
              <Word>and</Word><Word>break</Word><Word>do</Word><Word>else</Word><Word>elseif</Word><Word>end</Word>
              <Word>false</Word><Word>for</Word><Word>function</Word><Word>goto</Word><Word>if</Word><Word>in</Word>
              <Word>local</Word><Word>nil</Word><Word>not</Word><Word>or</Word><Word>repeat</Word><Word>return</Word>
              <Word>then</Word><Word>true</Word><Word>until</Word><Word>while</Word>
            </Keywords>
            <Keywords color="Builtin"><Word>assert</Word><Word>error</Word><Word>ipairs</Word><Word>pairs</Word><Word>pcall</Word><Word>print</Word><Word>require</Word><Word>tonumber</Word><Word>tostring</Word><Word>type</Word><Word>xpcall</Word><Word>math</Word><Word>string</Word><Word>table</Word><Word>coroutine</Word><Word>utf8</Word></Keywords>
            <Rule color="Number">\b(0[xX][0-9a-fA-F]+(\.[0-9a-fA-F]*)?([pP][+-]?[0-9]+)?|[0-9]+(\.[0-9]*)?([eE][+-]?[0-9]+)?)\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;
}
