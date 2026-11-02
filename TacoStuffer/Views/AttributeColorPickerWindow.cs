using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TacoStuffer.Models;

namespace TacoStuffer.Views;

public sealed class AttributeColorPickerWindow : PreviewDialog
{
    public string Value { get; private set; }
    public AttributeColorPickerWindow(string initial, bool rgba) : base("Choose color", 470)
    {
        AttributeColor.Parse(initial, rgba, out uint color);
        Value = AttributeColor.Format(color, rgba);
        var preview = new Border { Height = 65, Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
        Body.Children.Add(preview);
        var channels = new List<Slider>();
        var hex = new TextBox { Margin = new Thickness(0, 10, 0, 0) };
        bool updating = false;
        void Update()
        {
            if (updating || channels.Count != 4) return;
            uint argb = (uint)channels[3].Value << 24 | (uint)channels[0].Value << 16 | (uint)channels[1].Value << 8 | (uint)channels[2].Value;
            Value = AttributeColor.Format(argb, rgba); hex.Text = Value;
            preview.Background = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        }
        string[] names = ["Red", "Green", "Blue", "Alpha / Opacity"];
        int[] values = [(int)(color >> 16 & 255), (int)(color >> 8 & 255), (int)(color & 255), (int)(color >> 24)];
        for (int i = 0; i < 4; i++)
        {
            Label(names[i]);
            var slider = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, Value = values[i] };
            channels.Add(slider); slider.ValueChanged += (_, _) => Update(); Body.Children.Add(slider);
        }
        Label(rgba ? "HEX · RRGGBBAA (alpha last)" : "HEX · AARRGGBB (alpha first)"); Body.Children.Add(hex);
        hex.LostKeyboardFocus += (_, _) =>
        {
            if (!AttributeColor.Parse(hex.Text.Trim(), rgba, out uint entered)) { hex.Text = Value; return; }
            updating = true; channels[0].Value = entered >> 16 & 255; channels[1].Value = entered >> 8 & 255;
            channels[2].Value = entered & 255; channels[3].Value = entered >> 24; updating = false; Update();
        };
        Update();
        Action("Use color", (_, _) => { Update(); DialogResult = true; });
        Action("Cancel", (_, _) => DialogResult = false);
    }
}
