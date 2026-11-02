using System.Windows;
using System.Windows.Controls;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

public sealed record GuidTargetChoice(string Guid, string Label, string Detail);

public sealed class ResetGuidPickerWindow : PreviewDialog
{
    private readonly List<(CheckBox Check, GuidTargetChoice Choice)> rows = [];
    public IReadOnlyList<string> SelectedGuids => rows.Where(r => r.Check.IsChecked == true).Select(r => r.Choice.Guid).ToList();

    public ResetGuidPickerWindow(IReadOnlyList<GuidTargetChoice> choices, IReadOnlySet<string> selected, string title = "Choose reset targets", bool single = false) : base(title, 620)
    {
        SizeToContent = SizeToContent.Manual;
        Height = Math.Min(580, MaxHeight); MinHeight = Math.Min(320, Height); MinWidth = 430;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0)
        });
        var dock = (DockPanel)((Border)Content).Child;
        dock.Children.RemoveAt(1);
        var layout = new Grid { Margin = new Thickness(20) };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        dock.Children.Add(layout);
        var search = new TextBox { ToolTip = "Filter markers by name, category, document or GUID", Margin = new Thickness(0, 0, 0, 10) };
        layout.Children.Add(search);
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        foreach (var choice in choices)
        {
            var label = new TextBlock { Text = choice.Label + "\n" + choice.Detail, TextWrapping = TextWrapping.Wrap };
            var check = new CheckBox { Content = label, IsChecked = selected.Contains(PathingBehavior.GuidKey(choice.Guid)),
                Margin = new Thickness(0, 5, 0, 5), ToolTip = choice.Guid };
            check.Click += (_, _) => { if (single && check.IsChecked == true) foreach (var row in rows) if (row.Check != check) row.Check.IsChecked = false; };
            rows.Add((check, choice)); list.Children.Add(check);
        }
        if (choices.Count == 0) list.Children.Add(new TextBlock { Text = "No project markers with valid GUIDs are available.", TextWrapping = TextWrapping.Wrap });
        search.TextChanged += (_, _) =>
        {
            foreach (var row in rows)
                row.Check.Visibility = (row.Choice.Label + " " + row.Choice.Detail + " " + row.Choice.Guid).Contains(search.Text, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        };
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        footer.Children.Add(new TextBlock { Text = single ? "Select the first checkpoint of the sequence." : "Shared GUIDs are listed once. References outside this list are kept.", TextWrapping = TextWrapping.Wrap });
        var apply = new Button { Content = "Use selected markers", Margin = new Thickness(0, 8, 0, 0), IsDefault = true };
        apply.Click += (_, _) => DialogResult = true;
        footer.Children.Add(apply);
    }
}
