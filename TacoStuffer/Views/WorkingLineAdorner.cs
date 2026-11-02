using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TacoStuffer.Views;

// Paint above the editor without changing selection, formatting, or undo history.
internal sealed class WorkingLineAdorner : Adorner
{
    private static readonly ConditionalWeakTable<RichTextBox, WorkingLineAdorner> indicators = new();
    private readonly RichTextBox editor;
    private TextPointer? position;
    private Rect highlight = Rect.Empty;
    private Size extent;
    private WorkingLineAdorner(RichTextBox editor) : base(editor)
    {
        this.editor = editor;
        IsHitTestVisible = false;
        editor.PreviewMouseLeftButtonUp += (_, _) => editor.Dispatcher.BeginInvoke(new Action(() => Show(editor, editor.CaretPosition)));
        editor.LayoutUpdated += (_, _) => UpdateGeometry();
        editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateGeometry()));
        editor.SelectionChanged += (_, _) => { if (editor.IsKeyboardFocusWithin) Show(editor, editor.CaretPosition); };
    }
    public static void Attach(RichTextBox editor)
    {
        if (indicators.TryGetValue(editor, out _)) return;
        var indicator = new WorkingLineAdorner(editor);
        indicators.Add(editor, indicator);
        void Mount()
        {
            var layer = AdornerLayer.GetAdornerLayer(editor);
            var previous = VisualTreeHelper.GetParent(indicator) as AdornerLayer;
            if (layer == previous) return;
            previous?.Remove(indicator);
            layer?.Add(indicator);
        }
        editor.Loaded += (_, _) => Mount();
        editor.Unloaded += (_, _) => (VisualTreeHelper.GetParent(indicator) as AdornerLayer)?.Remove(indicator);
        Mount();
    }
    public static void Show(RichTextBox editor, TextPointer pointer, bool reveal = false)
    {
        Attach(editor);
        var indicator = indicators.GetValue(editor, e => new WorkingLineAdorner(e));
        indicator.position = pointer;
        if (reveal && pointer.Parent is FrameworkContentElement element) element.BringIntoView();
        indicator.UpdateGeometry();
    }
    private void UpdateGeometry()
    {
        // Invalidate only when geometry changes. Invalidating on every
        // LayoutUpdated event would itself schedule another layout indefinitely.
        Rect next = Rect.Empty;
        if (position != null && position.IsInSameDocument(editor.Document.ContentStart))
        {
            if (!position.HasValidLayout) return;
            next = position.GetCharacterRect(LogicalDirection.Forward);
        }
        if (next == highlight && extent == editor.RenderSize) return;
        highlight = next; extent = editor.RenderSize;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (highlight.IsEmpty || highlight.Height <= 0 || highlight.Bottom < 0 || highlight.Top > ActualHeight) return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(32, 145, 180, 220)), null,
            new Rect(2, highlight.Top, Math.Max(0, ActualWidth - 4), highlight.Height));
        dc.Pop();
    }
}
