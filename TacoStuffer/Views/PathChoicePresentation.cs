using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

internal static class PathChoicePresentation
{
    public static ComboBox Combo(List<PathChoice> choices, string? selected = null)
    {
        var template = new DataTemplate(typeof(PathChoice));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(PathChoice.Text)));
        text.SetBinding(TextBlock.ToolTipProperty, new Binding(nameof(PathChoice.Value)));
        template.VisualTree = text;
        var container = new Style(typeof(ComboBoxItem)) { BasedOn = (Style)Application.Current.FindResource(typeof(ComboBoxItem)) };
        container.Setters.Add(new Setter(UIElement.IsEnabledProperty, new Binding(nameof(PathChoice.IsSelectable))));
        var combo = new ComboBox
        {
            ItemsSource = choices, ItemTemplate = template, ItemContainerStyle = container,
            SelectedValuePath = nameof(PathChoice.Value), IsTextSearchEnabled = true
        };
        TextSearch.SetTextPath(combo, nameof(PathChoice.Label));
        combo.SetBinding(FrameworkElement.ToolTipProperty, new Binding("SelectedValue") { Source = combo });
        if (selected != null) combo.SelectedValue = selected;
        return combo;
    }
}
