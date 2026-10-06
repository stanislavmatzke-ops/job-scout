using JobScout.Infrastructure.JobsCz;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Infrastructure.JobsCz;

/// <summary>Company career page (firma.jobs.cz) after the browser rendered it.</summary>
public class JobsCzParserCareerPageTests
{
    private static readonly Uri PageUrl = new("https://ukazka.jobs.cz/detail-pozice?r=detail&id=1000000002");

    [Fact]
    public void Rendered_page_is_complete_with_title_company_and_location()
    {
        var detail = JobsCzParser.ParseCareerPage(Fixture.JobsCz("career-rendered.html"), "1000000002", PageUrl);

        Assert.NotNull(detail);
        Assert.True(detail.IsComplete);
        Assert.Equal("Tech Lead .NET", detail.Title);   // the decorative "." after the hero title is removed
        Assert.Equal("Ukázka", detail.Company);          // from og:title "… | Ukázka"
        Assert.Equal("https://www.jobs.cz/rpd/1000000002/", detail.Url);
        Assert.Contains("Lokalita: Vymyšlená 1, 100 00 Praha – Vršovice", detail.Text);
        Assert.Contains("Zdroj: kariérní stránka ukazka.jobs.cz", detail.Text);
    }

    [Fact]
    public void Rendered_page_contains_full_job_text_with_bullets()
    {
        var text = JobsCzParser.ParseCareerPage(Fixture.JobsCz("career-rendered.html"), "1000000002", PageUrl)!.Text;

        Assert.Contains("Věnovat se budeš hlavně:", text);
        Assert.Contains("- Vedení a mentoringu týmu vývojářů.", text);
        Assert.Contains("- Hluboké znalosti .NET, REST, gRPC a SQL.", text);
    }

    [Fact]
    public void Shell_before_rendering_returns_null()
    {
        // JavaScript hasn't run → no job text → caller must fall back, not pretend it's complete
        var detail = JobsCzParser.ParseCareerPage(Fixture.JobsCz("career-shell.html"), "1000000002", PageUrl);

        Assert.Null(detail);
    }

    [Fact]
    public void Unknown_template_returns_null()
    {
        var detail = JobsCzParser.ParseCareerPage(Fixture.JobsCz("career-unknown-template.html"), "1000000002", PageUrl);

        Assert.Null(detail);
    }

    [Fact]
    public void Without_hero_title_uses_og_title()
    {
        var longText = string.Join(" ", Enumerable.Repeat("Hledáme vývojáře do vymyšleného týmu.", 10));
        var html = $"""
            <html><head><meta property="og:title" content="Senior vývojář C# - Detail pozice | Firma X"></head>
            <body><div id="vacancy-detail"><div class="cp-detail__content"><p>{longText}</p></div></div></body></html>
            """;

        var detail = JobsCzParser.ParseCareerPage(html, "1000000005", PageUrl);

        Assert.NotNull(detail);
        Assert.Equal("Senior vývojář C#", detail.Title);
        Assert.Equal("Firma X", detail.Company);
    }
}
