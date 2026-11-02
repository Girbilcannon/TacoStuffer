using System.Globalization;

namespace TacoStuffer.Models;

public static class AttributeColor
{
    public static bool IsColor(string name, string type) => type.Equals("rgba", StringComparison.OrdinalIgnoreCase)
        || type.Equals("argb", StringComparison.OrdinalIgnoreCase) || type.Equals("color", StringComparison.OrdinalIgnoreCase)
        || name.Equals("color", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Color", StringComparison.OrdinalIgnoreCase);
    public static uint ToArgb(uint rgba) => (rgba & 255) << 24 | rgba >> 8;
    public static uint ToRgba(uint argb) => argb << 8 | argb >> 24;
    public static bool Parse(string value, bool rgba, out uint argb)
    {
        if (!rgba)
        {
            value = value.Trim().TrimStart('#');
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
            if (value.Length == 3) value = string.Concat(value.Select(c => new string(c, 2)));
            if (value.Length == 6) value = "FF" + value;
        }
        bool valid = value.Length == 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        argb = valid ? uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0xFFFFFFFF;
        if (valid && rgba) argb = ToArgb(argb);
        return valid;
    }
    public static string Format(uint argb, bool rgba) => (rgba ? ToRgba(argb) : argb).ToString("X8", CultureInfo.InvariantCulture);
}
