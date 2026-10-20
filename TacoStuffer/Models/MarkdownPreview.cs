using System.IO;
using System.Net;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Renderers;

namespace TacoStuffer.Models;

// CommonMark with GitHub-style tables, checklists, autolinks and strikethrough.
// The preview is a view of the source; it never writes HTML back into the editor.
public static class MarkdownPreview
{
    // WPF NavigateToString reports a null URI for its in-memory document.
    // It must not be mistaken for an external link and canceled.
    public static bool IsInternalNavigation(Uri? target) => target == null
        || target.IsAbsoluteUri && target.Scheme == "about" && target.AbsolutePath == "blank";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).DisableHtml().Build();

    public static string Html(string source, string name, string? projectFolder)
    {
        string SafeUrl(string url)
        {
            if (url.StartsWith('#')) return url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
                return absolute.Scheme is "https" or "http" or "mailto" ? absolute.AbsoluteUri : "#";
            if (projectFolder == null) return "#";
            try
            {
                string root = Path.GetFullPath(projectFolder) + Path.DirectorySeparatorChar;
                var resolved = new Uri(new Uri(Path.GetFullPath(Path.Combine(root, name))), url);
                return resolved.IsFile && Path.GetFullPath(resolved.LocalPath).StartsWith(root, StringComparison.OrdinalIgnoreCase) ? resolved.AbsoluteUri : "#";
            }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException) { return "#"; }
        }
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer) { LinkRewriter = SafeUrl };
        Pipeline.Setup(renderer);
        renderer.Render(Markdown.Parse(source, Pipeline));
        writer.Flush();
        return "<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='X-UA-Compatible' content='IE=edge'><title>" + WebUtility.HtmlEncode(name) + "</title><style>" +
            "html{background:#25282d;min-height:100%}body{background:#25282d;color:#ffffff;font:16px/1.6 Segoe UI,Arial,sans-serif;padding:24px;max-width:980px;margin:auto;word-wrap:break-word}" +
            "h1,h2{border-bottom:1px solid #4b525c;padding-bottom:.3em}h1{font-size:2em}h2{font-size:1.5em}" +
            "a{color:#8fc7ff;text-decoration:none}a:hover{color:#b6dcff;text-decoration:underline}blockquote{border-left:4px solid #4b525c;color:#d0d5dc;margin-left:0;padding-left:16px}" +
            "pre,code{font-family:Consolas,monospace;background:#1b1e23;border-radius:6px}code{padding:2px 5px}pre{padding:16px;overflow:auto}pre code{padding:0}" +
            "table{border-collapse:collapse;display:block;overflow:auto}td,th{border:1px solid #4b525c;padding:6px 13px}th{background:#343a43}tr:nth-child(2n){background:#2e333a}" +
            "img{max-width:100%}hr{border:0;height:2px;background:#4b525c}li+li{margin-top:4px}" +
            "</style></head><body>" + writer + "</body></html>";
    }
}
