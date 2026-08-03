using Markdig;
using Markdig.Syntax;

namespace GymStation.Web.Rendering;

/// <summary>
/// The ONE markdown pipeline in the app (#133). Every long-text field renders
/// through this — nothing else in the repo may produce a MarkupString.
///
/// Safety posture:
/// - Raw HTML is DISABLED: tags in the source render as literal text, so the
///   repo's zero-raw-HTML XSS stance survives markdown.
/// - Link/image destinations pass an http(s)/mailto/tel/relative allow-list;
///   anything else (javascript:, data:, vbscript:) rewrites to "#".
/// - Soft line breaks render as hard breaks — authors' single newlines finally
///   show (they collapsed entirely before this).
/// - ++text++ renders as inserted/underline and ~~text~~ as strikethrough
///   (EmphasisExtras): CommonMark has no underline, and Joshua asked for one.
/// </summary>
public static class AppMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseEmphasisExtras()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        var document = Markdig.Parsers.MarkdownParser.Parse(markdown, Pipeline);
        foreach (var node in document.Descendants())
        {
            switch (node)
            {
                case Markdig.Syntax.Inlines.LinkInline link:
                    link.Url = SafeUrl(link.Url);
                    break;
                case Markdig.Syntax.Inlines.AutolinkInline auto:
                    auto.Url = SafeUrl(auto.Url) == "#" ? "#" : auto.Url;
                    break;
            }
        }

        var writer = new System.IO.StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    // Relative paths and the boring schemes only — a stored "javascript:" URL
    // must never become a live href.
    private static string SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "#";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme is "http" or "https" or "mailto" or "tel" ? url : "#";
        }

        // Relative is fine; a scheme-ish prefix that failed absolute parsing is not.
        return url.Contains(':') ? "#" : url;
    }
}
