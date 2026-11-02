namespace TacoStuffer.Models;

internal sealed record XmlCommentEdit(string Source, int CommentOffset);

internal static class XmlComments
{
    public const string Placeholder = "Insert Comment Here";
    public static XmlCommentEdit Insert(string source, int offset, bool after)
    {
        offset = Math.Clamp(offset, 0, source.Length);
        int LineStart(int at) => at <= 0 ? 0 : source.LastIndexOf('\n', Math.Min(at - 1, source.Length - 1)) + 1;
        int LineEnd(int at) { int end = source.IndexOf('\n', at); return end < 0 ? source.Length : end + 1; }
        int start = LineStart(offset), end = LineEnd(offset);
        // A comment cannot live inside a tag, existing comment, CDATA or PI.
        // For a multiline construct, use its outer lines instead of corrupting XML.
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != '<') continue;
            int close;
            if (source.AsSpan(i).StartsWith("<!--")) { close = source.IndexOf("-->", i + 4, StringComparison.Ordinal); close = close < 0 ? source.Length : close + 3; }
            else if (source.AsSpan(i).StartsWith("<![CDATA[")) { close = source.IndexOf("]]>", i + 9, StringComparison.Ordinal); close = close < 0 ? source.Length : close + 3; }
            else
            {
                char quote = '\0'; close = i + 1;
                for (; close < source.Length; close++)
                {
                    char c = source[close];
                    if (quote != '\0') { if (c == quote) quote = '\0'; }
                    else if (c is '\'' or '"') quote = c;
                    else if (c == '>') { close++; break; }
                }
            }
            int boundary = after ? end : start;
            if (boundary > i && boundary < close) { start = LineStart(i); end = LineEnd(Math.Max(i, close - 1)); }
            if (!after && start == 0 && source.AsSpan(i).StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) after = true;
            i = close - 1;
        }
        int indentEnd = start;
        while (indentEnd < source.Length && source[indentEnd] is ' ' or '\t') indentEnd++;
        string indent = source[start..indentEnd];
        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        int insertion = after ? end : start;
        string prefix = after && end > 0 && source[end - 1] != '\n' ? newline : "";
        string comment = indent + "<!-- " + Placeholder + " -->";
        string suffix = insertion < source.Length || !after || source.EndsWith('\n') ? newline : "";
        return new(source.Insert(insertion, prefix + comment + suffix), insertion + prefix.Length + indent.Length + 5);
    }
}
