using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

public sealed class DropGuideAdorner : Adorner
{
    private readonly CategoryDropPosition position;
    public DropGuideAdorner(UIElement target, CategoryDropPosition position) : base(target)
    {
        this.position = position;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var size = AdornedElement.RenderSize;
        var brush = new SolidColorBrush(Color.FromRgb(139, 216, 205));
        var pen = new Pen(brush, 2);
        if (position == CategoryDropPosition.Child)
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(35, 139, 216, 205)), pen, new Rect(1, 1, Math.Max(0, size.Width - 2), Math.Max(0, size.Height - 2)), 3, 3);
        else
        {
            double y = position == CategoryDropPosition.Before ? 1 : Math.Max(1, size.Height - 1);
            drawing.DrawLine(pen, new Point(1, y), new Point(Math.Max(1, size.Width - 1), y));
            drawing.DrawRectangle(brush, null, new Rect(0, y - 3, 6, 6));
        }
    }
}
