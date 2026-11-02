namespace TacoStuffer.Models;

public sealed record GuidResetTargets(IReadOnlyList<string> Values, IReadOnlyList<string> Invalid)
{
    public string Csv => string.Join(',', Values);
    public IReadOnlyList<string> Keys => Values.Select(PathingBehavior.GuidKey).ToList();

    // XML uses a comma-separated list. The editor also accepts one GUID per line
    // and normalizes that input to CSV, preserving each GUID's original spelling.
    public static GuidResetTargets Parse(string? text)
    {
        var values = new List<string>();
        var invalid = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string part in (text ?? "").Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string value = part.Trim();
            if (value.Length == 0) continue;
            string key = PathingBehavior.GuidKey(value);
            if (key.Length == 0) { invalid.Add(value); continue; }
            if (seen.Add(key)) values.Add(value);
        }
        return new(values, invalid);
    }
}
