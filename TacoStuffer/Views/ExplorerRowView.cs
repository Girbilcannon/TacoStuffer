using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

public sealed class ExplorerRowView : FrameworkElement
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(ExplorerRow), typeof(ExplorerRowView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public ExplorerRow? Row { get => (ExplorerRow?)GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public static readonly RoutedEvent ToggleEvent = EventManager.RegisterRoutedEvent("Toggle", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ExplorerRowView));
    private const double Step = 22, HeightPerRow = 24, BoxSize = 11;
    private Rect Box => new((Row?.Depth ?? 0) * Step + 3, (HeightPerRow - BoxSize) / 2, BoxSize, BoxSize);
    public bool IsToggleHit(Point point) => Row?.HasChildren == true && Box.Contains(point);

    private static readonly Typeface RowTypeface = new("Segoe UI");
    private static readonly Pen HierarchyPen = FrozenPen(105, .65), BoxPen = FrozenPen(180, .8);
    private static Pen FrozenPen(byte alpha, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 227, 190, 123)), thickness);
        pen.Freeze();
        return pen;
    }

    private ExplorerRow? textRow;
    private FormattedText? cachedText;
    private double textDpi;
    private FormattedText Text()
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (cachedText != null && ReferenceEquals(textRow, Row) && textDpi == dpi) return cachedText;
        textRow = Row;
        textDpi = dpi;
        string label = Row?.Label ?? "";
        if (label.StartsWith("▾ ", StringComparison.Ordinal)) label = label[2..];
        return cachedText = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, RowTypeface, 13, Row?.Kind is "POI" or "Trail" or "segment" ? (Brush)FindResource("AccentBrush") : Brushes.WhiteSmoke, dpi);
    }

    protected override Size MeasureOverride(Size availableSize) => new((Row?.Depth ?? 0) * Step + 22 + Text().WidthIncludingTrailingWhitespace, HeightPerRow);

    protected override void OnRender(DrawingContext drawing)
    {
        if (Row is not { } row) return;
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var lines = HierarchyPen;
        double mid = HeightPerRow / 2;
        for (int i = 0; i < row.AncestorLines.Count; i++) if (row.AncestorLines[i]) drawing.DrawLine(lines, new Point(i * Step + 8.5, 0), new Point(i * Step + 8.5, HeightPerRow));
        if (row.Depth > 0)
        {
            double x = (row.Depth - 1) * Step + 8.5;
            drawing.DrawLine(lines, new Point(x, 0), new Point(x, row.HasNextSibling ? HeightPerRow : mid));
            drawing.DrawLine(lines, new Point(x, mid), new Point(row.Depth * Step + (row.HasChildren ? 3 : 17), mid));
        }
        if (row.HasChildren)
        {
            drawing.DrawRectangle(null, BoxPen, Box);
            double center = row.Depth * Step + 8.5;
            drawing.DrawLine(lines, new Point(center - 3, mid), new Point(center + 3, mid));
            if (!row.Expanded) drawing.DrawLine(lines, new Point(center, mid - 3), new Point(center, mid + 3));
            else drawing.DrawLine(lines, new Point(center, Box.Bottom), new Point(center, HeightPerRow));
        }
        var text = Text();
        drawing.DrawText(text, new Point(row.Depth * Step + 21, (HeightPerRow - text.Height) / 2));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && IsToggleHit(e.GetPosition(this)))
        {
            e.Handled = true;
            RaiseEvent(new RoutedEventArgs(ToggleEvent, this));
            return;
        }
        base.OnMouseDown(e);
    }
}
