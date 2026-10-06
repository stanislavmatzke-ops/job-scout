using System.Net;
using JobScout.Core.Jobs;
using JobScout.Infrastructure.JobsCz;
using JobScout.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Infrastructure.JobsCz;

/// <summary>
/// The whole JobsCzSource with fake network and fake browser:
/// which path is taken (fast HTML / browser / snippet) and that a job never silently disappears.
/// </summary>
public class JobsCzSourceTests
{
    private const string CareerPageUrl = "https://ukazka.jobs.cz/detail-pozice?r=detail&id=1000000002";

    private static (JobsCzSource Source, StubHttpHandler Http) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, FakePageRenderer renderer)
    {
        var http = new StubHttpHandler(respond);
        var client = new HttpClient(http) { BaseAddress = new Uri("https://www.jobs.cz") };
        return (new JobsCzSource(client, renderer, NullLogger<JobsCzSource>.Instance), http);
    }

    // ---------- SearchAsync ----------

    [Fact]
    public async Task Search_requests_correct_url_and_parses_cards()
    {
        var (source, http) = Create(
            req => StubHttpHandler.Html(req, Fixture.JobsCz("search-results.html")),
            FakePageRenderer.Returning(""));

        var listings = await source.SearchAsync(new JobSearchQuery(".NET architekt", "praha"));

        Assert.Equal(2, listings.Count);
        var requested = Assert.Single(http.Requests);
        Assert.Equal("www.jobs.cz", requested.Host);
        Assert.Equal("/prace/praha/?q[]=.NET architekt", Uri.UnescapeDataString(requested.PathAndQuery));
    }

    // ---------- GetDetailAsync: the three paths ----------

    [Fact]
    public async Task Detail_on_jobs_cz_is_parsed_directly_without_browser()
    {
        var renderer = FakePageRenderer.Returning("should not be used");
        var (source, http) = Create(req => StubHttpHandler.Html(req, Fixture.JobsCz("detail-full.html")), renderer);

        var detail = await source.GetDetailAsync("1000000001");

        Assert.True(detail.IsComplete);
        Assert.Equal("Senior .NET vývojář (m/ž)", detail.Title);
        Assert.Equal("/rpd/1000000001/", Assert.Single(http.Requests).AbsolutePath);
        Assert.Empty(renderer.Calls);   // the expensive browser was not started
    }

    [Fact]
    public async Task Redirected_detail_is_rendered_by_browser_and_complete()
    {
        var renderer = FakePageRenderer.Returning(Fixture.JobsCz("career-rendered.html"));
        var (source, _) = Create(
            req => StubHttpHandler.Html(req, Fixture.JobsCz("career-shell.html"), finalUrl: CareerPageUrl),
            renderer);

        var detail = await source.GetDetailAsync("1000000002");

        Assert.True(detail.IsComplete);
        Assert.Contains("- Vedení a mentoringu týmu vývojářů.", detail.Text);
        Assert.Equal("ukazka.jobs.cz", Assert.Single(renderer.Calls).Host);   // browser got the career page URL
    }

    [Fact]
    public async Task Browser_failure_falls_back_to_snippet_instead_of_losing_the_job()
    {
        var renderer = FakePageRenderer.Failing(new TimeoutException("Page did not load in time"));
        var (source, _) = Create(
            req => StubHttpHandler.Html(req, Fixture.JobsCz("career-shell.html"), finalUrl: CareerPageUrl),
            renderer);

        var detail = await source.GetDetailAsync("1000000002");

        Assert.False(detail.IsComplete);
        Assert.Contains("Hledáme tech leada pro vymyšlené projekty.", detail.Text);
        Assert.Single(renderer.Calls);
    }

    [Fact]
    public async Task Unknown_career_page_template_falls_back_to_snippet()
    {
        var renderer = FakePageRenderer.Returning(Fixture.JobsCz("career-unknown-template.html"));
        var (source, _) = Create(
            req => StubHttpHandler.Html(req, Fixture.JobsCz("career-shell.html"), finalUrl: CareerPageUrl),
            renderer);

        var detail = await source.GetDetailAsync("1000000002");

        Assert.False(detail.IsComplete);
        Assert.Contains("ukazka.jobs.cz", detail.Text);
    }

    // ---------- GetDetailAsync: errors that must NOT be swallowed ----------

    [Fact]
    public async Task Cancellation_is_not_swallowed_by_the_fallback()
    {
        // Ctrl+C / app shutdown must stop the run, not turn into a "snippet"
        var renderer = FakePageRenderer.Failing(new OperationCanceledException());
        var (source, _) = Create(
            req => StubHttpHandler.Html(req, Fixture.JobsCz("career-shell.html"), finalUrl: CareerPageUrl),
            renderer);

        await Assert.ThrowsAsync<OperationCanceledException>(() => source.GetDetailAsync("1000000002"));
    }

    [Fact]
    public async Task Http_error_is_reported()
    {
        // e.g. the job ad was removed – the caller decides what to do, we don't invent a detail
        var (source, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound), FakePageRenderer.Returning(""));

        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetDetailAsync("1000000404"));
    }

    // ---------- BuildSearchUrl ----------

    [Theory]
    [InlineData(".NET architekt", "praha", 1, "/prace/praha/?q%5B%5D=.NET%20architekt")]
    [InlineData("C#", null, 1, "/prace/?q%5B%5D=C%23")]                    // no location, '#' must be escaped
    [InlineData("tech lead", "/brno/", 1, "/prace/brno/?q%5B%5D=tech%20lead")] // slashes around slug are tolerated
    [InlineData("java", "praha", 3, "/prace/praha/?q%5B%5D=java&page=3")]
    public void BuildSearchUrl_creates_jobs_cz_search_url(string keywords, string? location, int page, string expected)
    {
        Assert.Equal(expected, JobsCzSource.BuildSearchUrl(new JobSearchQuery(keywords, location, page)));
    }
}
