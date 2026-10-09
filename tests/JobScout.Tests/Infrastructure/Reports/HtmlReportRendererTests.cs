using JobScout.Application.Reports;
using JobScout.Infrastructure.Reports;

namespace JobScout.Tests.Infrastructure.Reports;

/// <summary>Self-contained HTML file: readable offline, copy buttons for letters, everything from ads HTML-encoded.</summary>
public class HtmlReportRendererTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    private static ReportJob Job(string title, int? score = 8, string? letter = null, string? url = null, int? relevance = 4) =>
        new(title, "Firma s.r.o.", "Praha", url ?? "https://www.jobs.cz/rpd/2001000001/", score, relevance,
            "Shrnutí.", ["Důvod"], ["Riziko"], ["Otázka?"], "", letter, DetailIncomplete: false);

    private static MorningReport Report(
        IReadOnlyList<ReportJob>? recommended = null, IReadOnlyList<ReportJob>? other = null,
        IReadOnlyList<ReportJob>? earlier = null, IReadOnlyList<ReportJob>? waiting = null) =>
        new("private", T0, new ReportStats(157, 50, 15, 5, 0.36m),
            recommended ?? [], other ?? [], earlier ?? [], waiting ?? []);

    [Fact]
    public void Report_is_a_complete_utf8_html_document()
    {
        var html = HtmlReportRenderer.Render(Report());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.EndsWith("</html>", html.TrimEnd());
    }

    [Fact]
    public void Recommended_job_shows_score_link_and_letter_with_a_copy_button()
    {
        var html = HtmlReportRenderer.Render(Report(recommended: [Job("Solution Architect", letter: "Dobrý den,\nreaguji…")]));

        Assert.Contains("Solution Architect", html);
        Assert.Contains("8/10", html);
        Assert.Contains("href=\"https://www.jobs.cz/rpd/2001000001/\"", html);
        Assert.Contains("Dobrý den,", html);
        Assert.Contains("Kopírovat", html);
        Assert.Contains("navigator.clipboard", html);
    }

    [Fact]
    public void Header_shows_the_run_statistics_and_cost()
    {
        var html = HtmlReportRenderer.Render(Report());

        Assert.Contains("157", html);
        Assert.Contains("0,36", html);          // Czech decimal comma
    }

    [Fact]
    public void Text_from_job_ads_is_html_encoded()
    {
        // Job ads are untrusted input – a title must never become markup or script in the report
        var html = HtmlReportRenderer.Render(Report(recommended: [Job("<script>alert(1)</script> & spol.", letter: "<b>x</b>")]));

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<b>x</b>", html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    public void Only_http_links_are_rendered(string url)
    {
        var html = HtmlReportRenderer.Render(Report(waiting: [Job("Analytik", score: null, url: url, relevance: 2)]));

        Assert.DoesNotContain(url, html);
    }

    [Fact]
    public void Empty_sections_say_so_instead_of_disappearing()
    {
        var html = HtmlReportRenderer.Render(Report());

        Assert.Contains("Dnes nic nového", html);
    }

    [Fact]
    public void Each_letter_has_its_own_copy_target()
    {
        var html = HtmlReportRenderer.Render(Report(recommended: [Job("A", letter: "Dopis A"), Job("B", letter: "Dopis B")]));

        Assert.Contains("id=\"letter-1\"", html);
        Assert.Contains("id=\"letter-2\"", html);
    }
}
