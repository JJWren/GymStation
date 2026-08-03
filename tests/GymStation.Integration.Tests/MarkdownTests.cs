using GymStation.Web.Rendering;

namespace GymStation.Integration.Tests;

/// <summary>The one sanitized markdown pipeline (#133): raw HTML stays text,
/// dangerous link schemes die, and the requested formatting actually renders.</summary>
public class MarkdownTests
{
    [Fact]
    public void RawHtml_RendersAsLiteralText()
    {
        var html = AppMarkdown.ToHtml("hello <script>alert(1)</script> <img src=x onerror=alert(1)>");
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JAVASCRIPT:alert(1))")]
    [InlineData("[x](vbscript:msgbox)")]
    [InlineData("![x](data:text/html;base64,PHNjcmlwdD4=)")]
    public void DangerousDestinations_AreNeutralized(string markdown)
    {
        var html = AppMarkdown.ToHtml(markdown);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[gym](https://example.com)", "href=\"https://example.com\"")]
    [InlineData("[mail](mailto:hi@example.com)", "href=\"mailto:hi@example.com\"")]
    [InlineData("[page](/legal/privacy)", "href=\"/legal/privacy\"")]
    public void BoringDestinations_Survive(string markdown, string expected)
        => Assert.Contains(expected, AppMarkdown.ToHtml(markdown));

    [Fact]
    public void RequestedFormatting_Renders()
    {
        Assert.Contains("<strong>bold</strong>", AppMarkdown.ToHtml("**bold**"));
        Assert.Contains("<em>italic</em>", AppMarkdown.ToHtml("_italic_"));
        Assert.Contains("<ins>under</ins>", AppMarkdown.ToHtml("++under++"));
        Assert.Contains("<del>gone</del>", AppMarkdown.ToHtml("~~gone~~"));
        var list = AppMarkdown.ToHtml("- one\n- two\n\n1. first\n2. second");
        Assert.Contains("<ul>", list);
        Assert.Contains("<ol>", list);
    }

    [Fact]
    public void SingleNewlines_BecomeVisibleBreaks()
        => Assert.Contains("<br", AppMarkdown.ToHtml("line one\nline two"));

    [Fact]
    public void EmptyInput_RendersNothing()
        => Assert.Equal("", AppMarkdown.ToHtml("   "));
}
