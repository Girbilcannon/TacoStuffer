using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private string inheritedPaneKey = "";
    private bool inheritedPaneExpanded;
    private readonly List<Action> attributeAlertUpdates = [];
    private AttributeRequirementContext lastAttributeAlertContext;
    private AttributeRequirementContext AttributeAlertContext => new(InteractionPreferences.ShowMarkerInformation,
        InteractionPreferences.MarkerClipboard != MarkerClipboardMode.Never, overlay?.IsRenderingActive == true, interactionTestMode,
        InteractionPreferences.MarkerClipboard == MarkerClipboardMode.Always, checkpointTestActive);

    private void RefreshAttributeAlerts()
    {
        var context = AttributeAlertContext;
        if (context == lastAttributeAlertContext) return;
        lastAttributeAlertContext = context;
        foreach (var update in attributeAlertUpdates) update();
    }

    private void AttributeSection(XElement element, AttributeDefinition definition)
    {
        var heading = new TextBlock { Style = (Style)FindResource("SectionTitle"), TextWrapping = TextWrapping.Wrap };
        void Update()
        {
            heading.Inlines.Clear();
            heading.Inlines.Add(new Run(definition.DisplayName.ToUpperInvariant() + (definition.IsCustom ? " · CUSTOM" : "")));
            var alerts = AttributeRequirements.Evaluate(element, definition, CategoryValues(), AttributeAlertContext);
            foreach (var alert in alerts)
                heading.Inlines.Add(new Run("  ⚠ " + alert.Message) { Foreground = (Brush)FindResource("TortillaBrush"), FontSize = 11, FontWeight = FontWeights.Normal });
            heading.ToolTip = alerts.Count == 0 ? null : string.Join("\n\n", alerts.Select(a => a.Detail));
        }
        attributeAlertUpdates.Add(Update);
        Update();
        AttributesHost.Children.Add(heading);
    }
    private void ShowInheritedAttributes(XElement element)
    {
        string key = selected?.DocumentName + "|" + selected?.Key;
        if (key != inheritedPaneKey) { inheritedPaneKey = key; inheritedPaneExpanded = false; }
        var inherited = AttributeRules.Inherited(element, CategoryValues()).Where(a => AttributeApplies(element, a.Name)).ToList();
        if (inherited.Count == 0) return;
        var list = new StackPanel();
        var labels = CategoryDisplayNames();
        foreach (var attribute in inherited)
        {
            var row = new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 4),
                TextWrapping = TextWrapping.Wrap,
                ToolTip = attribute.Name + " = " + attribute.Value + "\nFrom " + attribute.Category + (attribute.Overridden ? "\nOverridden locally" : "")
            };
            row.Inlines.Add(new Run(attribute.Name) { TextDecorations = attribute.Overridden ? TextDecorations.Strikethrough : null });
            string value = attribute.Value.Replace("\r", " ").Replace("\n", " ");
            if (value.Length > 80) value = value[..80] + "…";
            row.Inlines.Add(new Run("  " + value) { Foreground = (Brush)FindResource("MutedBrush") });
            row.Inlines.Add(new Run("\n" + labels.GetValueOrDefault(attribute.Category, attribute.Category)) { Foreground = (Brush)FindResource("MutedBrush"), FontSize = 11 });
            var menu = new ContextMenu();
            var action = new MenuItem { Header = attribute.Overridden ? "Remove override" : "Override" };
            action.Click += (_, _) => { if (attribute.Overridden) ChangeAttribute(attribute.Name, null); else OverrideInheritedAttribute(attribute.Name); };
            menu.Items.Add(action); row.ContextMenu = menu;
            list.Children.Add(row);
        }
        var expander = new Expander
        {
            Header = $"Inherited attributes ({inherited.Count})",
            IsExpanded = inheritedPaneExpanded,
            Margin = new Thickness(0, 4, 0, 12),
            ToolTip = "Right-click an inherited attribute to override it. Crossed-out names have a local override.",
            Content = new ScrollViewer
            {
                Content = list,
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(8, 4, 4, 0)
            }
        };
        expander.Expanded += (_, _) => inheritedPaneExpanded = true;
        expander.Collapsed += (_, _) => inheritedPaneExpanded = false;
        AttributesHost.Children.Add(expander);
    }

    private void OverrideInheritedAttribute(string name)
    {
        if (!TrySyncXml()) return;
        var categories = CategoryValues();
        foreach (var target in AttributeTargets().Where(t => AttributeApplies(t, name)))
        {
            var inherited = AttributeRules.Inherited(target, categories).FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (inherited is { Overridden: false })
                AttributeRules.Apply([target], new Dictionary<string, string?> { [inherited.Name] = inherited.Value }, AttributeApplies);
        }
        WritePages();
    }

    private void ShowLocalAttribute(XElement element, AttributeMember member)
    {
        int first = AttributesHost.Children.Count;
        if (member.Type == "bool")
        {
            var check = new CheckBox { Content = InteractionFieldLabel(member.Name), IsChecked = VisualAttributeMath.Enabled(Value(element, member.Name)) };
            check.Click += (_, _) => ChangeAttribute(member.Name, member.Name is "isCheckpoint" or "cpFinish" ? (check.IsChecked == true ? "true" : "false") : (check.IsChecked == true ? "1" : "0"));
            AttributesHost.Children.Add(check);
        }
        else if (ColorAttributeField(element, member)) { }
        else if (AdvancedAttributeField(element, member)) { }
        else if (member.Name is "iconFile" or "texture") AssetField(member.Name, Value(element, member.Name));
        else Field(member.Name, Value(element, member.Name), v => ChangeAttribute(member.Name, v));

        // Put removal on the field label/control rather than replacing a text
        // editor's own Cut/Copy/Paste menu. Removing a local value restores its fallback.
        if (AttributesHost.Children.Count > first && AttributesHost.Children[first] is FrameworkElement label)
        {
            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "Remove local value" };
            remove.Click += (_, _) => ChangeAttribute(member.Name, null);
            menu.Items.Add(remove); label.ContextMenu = menu;
        }
    }

    private void ShowIndependentAttribute(XElement element, AttributeDefinition group, AttributeMember member)
    {
        var control = group with { DisplayName = AttributePickerOptions.Label(member.Name), Members = [member] };
        AttributeSection(element, control);
        ShowLocalAttribute(element, member);
        var remove = new Button { Content = "Remove " + control.DisplayName, Margin = new Thickness(0, 8, 0, 4) };
        remove.Click += (_, _) =>
        {
            if (!TrySyncXml()) return;
            ApplyAttributeValues(AttributeTargets(), new Dictionary<string, string?> { [member.Name] = null });
        };
        AttributesHost.Children.Add(remove);
    }

    private void AddAttributeOption(XElement element, AttributeDefinition definition)
    {
        var missing = definition.Members.Where(m => m.Name != "resetMessage" && !AttributePaneRules.HasLocal(element, m.Name)).ToList();
        if (missing.Count == 0) return;
        var choices = new List<PathChoice> { new("", "Add option…", IsSelectable: false) };
        choices.AddRange(missing.Select(m => new PathChoice(m.Name, InteractionFieldLabel(m.Name))));
        var picker = PathChoicePresentation.Combo(choices, "");
        picker.Margin = new Thickness(0, 8, 0, 0);
        picker.SelectionChanged += (_, _) =>
        {
            if (rebuildingAttributes || picker.SelectedValue is not string name || name.Length == 0) return;
            var member = missing.First(m => m.Name == name);
            ChangeAttribute(name, EffectiveValue(element, name, InteractionFieldDefault(element, name, member.DefaultValue)));
        };
        AttributesHost.Children.Add(picker);
    }
}
