using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using JobScout.Core.Jobs;

namespace JobScout.Infrastructure.JobsCz;

/// <summary>Pure HTML → records. No network, so it's testable on saved pages.</summary>
internal static partial class JobsCzParser
{
    public const string SourceName = "jobs.cz";
    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// jobs.cz says so when a search really found nothing. A page with no job cards and without this message is something
    /// else – a bot check, an error page, a new layout – and must not be taken for "no new jobs" (20 Oct 2026).
    /// </summary>
    private const string NoResultsMessage = "Nenašli jsme";    // the rest of the sentence is split by markup

    public static bool IsNoResultsPage(string html) => html.Contains(NoResultsMessage, StringComparison.Ordinal);

    /// <summary>The page's &lt;title&gt; – enough to tell from the log what jobs.cz sent instead of results.</summary>
    public static string PageTitle(string html) => Clean(Parser.ParseDocument(html).Title);

    public static IReadOnlyList<JobListing> ParseSearchResults(string html)
    {
        var doc = Parser.ParseDocument(html);
        var result = new List<JobListing>();

        foreach (var card in doc.QuerySelectorAll("article.SearchResultCard"))
        {
            // Some <article>s are ads/promo – a real job has a detail link with an ID
            var link = card.QuerySelector("a[data-link='jd-detail']");
            var id = link?.GetAttribute("data-jobad-id");
            if (link is null || string.IsNullOrEmpty(id))
                continue;

            result.Add(new JobListing(
                Source: SourceName,
                ExternalId: id,
                Title: Clean(link.TextContent),
                Company: Clean(card.QuerySelector("footer li span[translate='no']")?.TextContent),
                Location: Clean(card.QuerySelector("[data-test='serp-locality']")?.TextContent),
                Url: $"https://www.jobs.cz/rpd/{id}/",
                Tags: card.QuerySelectorAll(".Tag").Select(t => Clean(t.TextContent)).ToList()));
        }

        return result;
    }

    public static JobDetail ParseDetail(string html, string externalId, Uri finalUrl)
    {
        var doc = Parser.ParseDocument(html);
        var url = $"https://www.jobs.cz/rpd/{externalId}/";
        var body = doc.QuerySelector("[data-test='jd-body-richtext']");

        // Redirect to a JS-rendered company page → no body; use what the <meta> tags give us
        if (body is null)
        {
            var ogTitle = doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content");
            var ogDesc = doc.QuerySelector("meta[property='og:description']")?.GetAttribute("content");
            return new JobDetail(SourceName, externalId, url,
                Title: Clean(ogTitle ?? doc.Title),
                Company: "",
                Text:
                $"(Úplný text inzerátu není dostupný – stránka {finalUrl.Host} se načítá JavaScriptem.)\n{Clean(ogDesc)}",
                IsComplete: false);
        }

        var title = Clean(doc.QuerySelector("h1")?.TextContent);
        var infoItems = doc.QuerySelectorAll("[data-test='jd-info-item']")
            .Select(e => (
                Label: Clean(e.QuerySelector(".accessibility-hidden")?.TextContent),
                Value: Clean(e.QuerySelector("p")?.TextContent ?? e.TextContent)))
            .ToList();

        var company = infoItems.FirstOrDefault(i => i.Label == "Společnost").Value ?? "";
        var location = Clean(doc.QuerySelector("[data-test='jd-info-location']")?.TextContent);
        var benefits = doc.QuerySelectorAll("[data-test='jd-benefits']").Select(e => Clean(e.TextContent));

        var sections = new[]
        {
            $"# {title}\nLokalita: {location}\n" +
            string.Join('\n', infoItems.Select(i => i.Label.Length > 0 ? $"{i.Label}: {i.Value}" : i.Value)),
            benefits.Any() ? $"Benefity: {string.Join(", ", benefits)}" : "",
            ToPlainText(doc.QuerySelector("[data-test='jd-header-text']")),
            ToPlainText(body),
        };
        var text = string.Join("\n\n", sections.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new JobDetail(SourceName, externalId, url, title, company, text, IsComplete: true);
    }
    
    /// <summary>
    /// Company career page (firma.jobs.cz, "Capybara" template) rendered by a browser.
    /// Returns null when the expected content isn't there (unknown template) → caller falls back to a snippet.
    /// </summary>
    public static JobDetail? ParseCareerPage(string html, string externalId, Uri pageUrl)
    {
        var doc = Parser.ParseDocument(html);
        var root = doc.QuerySelector("#vacancy-detail") ?? doc.QuerySelector(".cp-detail__content");
        var body = ToPlainText(root);
        if (body.Length < 200)
            return null;

        // og:title looks like "C#/.NET Tech Lead - Detail pozice | NAKIT"
        var ogTitle = Clean(doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content"));
        var title = Clean(doc.QuerySelector(".hero__title")?.TextContent).TrimEnd('.');
        if (title.Length == 0)
            title = ogTitle.Split(" - ")[0];
        var company = ogTitle.Contains('|') ? ogTitle[(ogTitle.LastIndexOf('|') + 1)..].Trim() : "";
        var location = Clean(doc.QuerySelector(".hero__locality")?.TextContent);

        var text = $"""
                    # {title}
                    Společnost: {company}
                    Lokalita: {location}
                    Zdroj: kariérní stránka {pageUrl.Host}

                    {body}
                    """;

        return new JobDetail(SourceName, externalId, $"https://www.jobs.cz/rpd/{externalId}/",
            title, company, text, IsComplete: true);
    }    

    // HTML → text that keeps paragraphs and bullet points (TextContent would glue everything together)
    internal static string ToPlainText(IElement? root)
    {
        if (root is null) return "";
        var sb = new StringBuilder();
        AppendText(root, sb);

        var lines = sb.ToString().Split('\n').Select(Clean).Where(l => l.Length > 0 && l != "-");
        return string.Join('\n', lines);
    }

    private static readonly HashSet<string> BlockTags =
        ["P", "DIV", "UL", "OL", "LI", "H1", "H2", "H3", "H4", "H5", "H6", "BR", "SECTION", "TR"];

    private static void AppendText(INode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IText t)
            {
                sb.Append(t.Data);
            }
            else if (child is IElement e)
            {
                var isBlock = BlockTags.Contains(e.TagName);
                sb.Append(e.TagName == "LI" ? "\n- " : isBlock ? "\n" : "");
                AppendText(e, sb);
                if (isBlock) sb.Append('\n');
            }
        }
    }

    // Removes invisible characters (jobs.cz puts zero-width joiners around "–") and collapses whitespace
    internal static string Clean(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : Whitespace().Replace(text.Replace("\u200D", "").Replace("\u00A0", " "), " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}