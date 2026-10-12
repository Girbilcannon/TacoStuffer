using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace TacoStuffer.Models;

// Indexes exact source positions, including multi-line start tags and nested closing tags.
public sealed record XmlSourceMap(Func<int, XElement?> ElementAt, IReadOnlyDictionary<string, int> Starts);

public static class XmlSourceIndex
{
    public static XElement? FindElement(string source, int caretOffset) => Build(source)(caretOffset);

    // Reuse a parsed source map while moving the caret through unchanged XML.
    public static Func<int, XElement?> Build(string source) => BuildMap(source).ElementAt;

    public static XmlSourceMap BuildMap(string source)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        XDocument document;
        using (var input = new StringReader(source))
            using (var parser = XmlReader.Create(input, settings)) document = XDocument.Load(parser, LoadOptions.SetLineInfo);
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++) if (source[i] == '\n') starts.Add(i + 1);
        int Offset(IXmlLineInfo info) => starts[Math.Clamp(info.LineNumber - 1, 0, starts.Count - 1)] + Math.Max(0, info.LinePosition - 1);
        int TagEnd(int offset)
        {
            char quote = '\0';
            for (int i = Math.Clamp(offset, 0, source.Length); i < source.Length; i++)
            {
                char c = source[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c is '\'' or '"') quote = c;
                else if (c == '>') return i + 1;
            }
            return source.Length;
        }
        var locations = document.Descendants().ToDictionary(e =>
        {
            var p = (IXmlLineInfo)e;
            return (p.LineNumber, p.LinePosition);
        });
        var stack = new Stack<(XElement Element, int Start)>();
        var spans = new List<(XElement Element, int Start, int End)>();
        using var text = new StringReader(source);
        using var reader = XmlReader.Create(text, settings);
        while (reader.Read())
        {
            var info = (IXmlLineInfo)reader;
            if (reader.NodeType == XmlNodeType.Element && locations.TryGetValue((info.LineNumber, info.LinePosition), out var element))
            {
                int start = Math.Max(0, Offset(info) - 1);
                if (reader.IsEmptyElement) spans.Add((element, start, TagEnd(start)));
                else stack.Push((element, start));
            }
            else if (reader.NodeType == XmlNodeType.EndElement && stack.Count > 0)
            {
                var start = stack.Pop();
                spans.Add((start.Element, start.Start, TagEnd(Math.Max(0, Offset(info) - 2))));
            }
        }
        var elementStarts = spans.Where(s => s.Element.Name.LocalName is "POI" or "Trail" or "MarkerCategory").GroupBy(s => ElementIdentity.Key(s.Element)).ToDictionary(g => g.Key, g => g.First().Start);
        return new XmlSourceMap(caretOffset =>
        {
            caretOffset = Math.Clamp(caretOffset, 0, source.Length);
            if (caretOffset > 0 && source[caretOffset - 1] == '>') caretOffset--;
            // A whole-line selection often starts in indentation before the opening tag.
            int token = caretOffset;
            while (token < source.Length && source[token] is ' ' or '\t') token++;
            if (token < source.Length && source[token] == '<') caretOffset = token;
            // A blank line inside a category selects that category; POIs wrapper lines select nothing.
            return spans.Where(s => s.Start <= caretOffset && caretOffset < s.End && s.Element.Name.LocalName is "POI" or "Trail" or "MarkerCategory")
            .OrderBy(s => s.End - s.Start).Select(s => s.Element).FirstOrDefault();
        }, elementStarts);
    }
}
