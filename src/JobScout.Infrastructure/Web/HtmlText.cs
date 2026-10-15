using AngleSharp.Html.Parser;
using JobScout.Infrastructure.JobsCz;

namespace JobScout.Infrastructure.Web;

/// <summary>Any web page (or an HTML e-mail) → its title and readable text, without menus, scripts and footers.</summary>
internal static class HtmlText
{
    /// <summary>Never part of the ad itself.</summary>
    private const string Noise =
        "script, style, noscript, template, svg, iframe, nav, header, footer, form, button, [aria-hidden=true], [hidden]";

    public static (string? Title, string Text) Extract(Stream html) => Extract(new HtmlParser().ParseDocument(html));

    public static (string? Title, string Text) Extract(string html) => Extract(new HtmlParser().ParseDocument(html));

    private static (string? Title, string Text) Extract(AngleSharp.Html.Dom.IHtmlDocument doc)
    {
        var title = doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content") ?? doc.Title;
        foreach (var element in doc.QuerySelectorAll(Noise).ToList())
            element.Remove();

        // The main content when the page marks it; otherwise the whole body
        var root = doc.QuerySelector("main, [role=main], article") ?? doc.Body;
        var text = JobsCzParser.ToPlainText(root);            // keeps paragraphs and bullet points
        return (string.IsNullOrWhiteSpace(title) ? null : JobsCzParser.Clean(title), text);
    }
}
