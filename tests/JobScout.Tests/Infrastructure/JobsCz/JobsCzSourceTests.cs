using System.Net;
using System.Text.RegularExpressions;
using JobScout.Application.Jobs;
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

    // ---------- SearchAsync: one page ----------

    [Fact]
    public async Task Search_requests_correct_url_and_parses_cards()
    {
        var (source, http) = Create(
            req => StubHttpHandler.Html(req, Fixture.JobsCz("search-results.html")),
            FakePageRenderer.Returning(""));

        var listings = await source.SearchAsync(new JobSearch.JobSearchQuery { Keywords = ".NET architekt", Location = "Praha" });

        Assert.Equal(2, listings.Count);
        var requested = Assert.Single(http.Requests);
        Assert.Equal("www.jobs.cz", requested.Host);
        Assert.Equal("/prace/praha/?q[]=.NET architekt", Uri.UnescapeDataString(requested.PathAndQuery));
    }

    [Fact]
    public async Task Unknown_location_404_returns_empty_list_instead_of_failing()
    {
        // e.g. "Praha 4" → /prace/praha-4/ doesn't exist on jobs.cz; one bad search must not stop the morning run
        var (source, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound), FakePageRenderer.Returning(""));

        var listings = await source.SearchAsync(new JobSearch.JobSearchQuery { Category = "sw-architekt", Location = "Praha 4" });

        Assert.Empty(listings);
    }

    [Fact]
    public void Source_offers_verified_category_catalog()
    {
        var (source, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK), FakePageRenderer.Returning(""));

        Assert.Contains(source.Categories, c => c.Id == "sw-architekt");
        Assert.Contains(source.Categories, c => c.Id == "java-vyvojar");
        Assert.All(source.Categories, c => Assert.Matches("^[a-z0-9-]+$", c.Id));   // usable as URL segment
        Assert.Equal(source.Categories.Count, source.Categories.Select(c => c.Id).Distinct().Count());
    }

    // ---------- SearchAsync: paging ----------

    /// <summary>Minimal search page with the given job IDs (same structure as jobs.cz cards).</summary>
    private static string SearchPage(IEnumerable<int> ids) =>
        "<html><body>" +
        string.Concat(ids.Select(id =>
            $"""<article class="SearchResultCard"><h2><a data-link="jd-detail" data-jobad-id="{id}" href="/rpd/{id}/">Nabídka {id}</a></h2></article>""")) +
        "</body></html>";

    private static int PageOf(HttpRequestMessage request)
    {
        var match = Regex.Match(request.RequestUri!.Query, @"page=(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 1;
    }

    [Fact]
    public async Task Paging_stops_after_a_partial_page()
    {
        // page 1 = full (30), page 2 = 5 jobs → that's the end
        var (source, http) = Create(req => StubHttpHandler.Html(req, PageOf(req) switch
        {
            1 => SearchPage(Enumerable.Range(1, 30)),
            2 => SearchPage(Enumerable.Range(31, 5)),
            _ => throw new InvalidOperationException("Page 3 must not be requested"),
        }), FakePageRenderer.Returning(""));

        var listings = await source.SearchAsync(new JobSearch.JobSearchQuery { Category = "sw-architekt" }, maxPages: 10);

        Assert.Equal(35, listings.Count);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Paging_stops_when_jobs_cz_repeats_the_last_page()
    {
        // Past the end, jobs.cz returns the last page again – no new IDs means stop
        var (source, http) = Create(req => StubHttpHandler.Html(req, PageOf(req) switch
        {
            1 => SearchPage(Enumerable.Range(1, 30)),
            _ => SearchPage(Enumerable.Range(31, 30)),   // page 2, and the same again for 3, 4, …
        }), FakePageRenderer.Returning(""));

        var listings = await source.SearchAsync(new JobSearch.JobSearchQuery { Category = "sw-architekt" }, maxPages: 10);

        Assert.Equal(60, listings.Count);
        Assert.Equal(3, http.Requests.Count);   // page 3 showed nothing new → stop
    }

    [Fact]
    public async Task Paging_respects_max_pages()
    {
        // Every page full of new jobs – the limit protects the site (and us)
        var (source, http) = Create(
            req => StubHttpHandler.Html(req, SearchPage(Enumerable.Range(PageOf(req) * 100, 30))),
            FakePageRenderer.Returning(""));

        var listings = await source.SearchAsync(new JobSearch.JobSearchQuery { Category = "programator" }, maxPages: 3);

        Assert.Equal(90, listings.Count);
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Paging_removes_duplicates_and_keeps_first_order()
    {
        // A job can move between pages while we read them (new ads push others down)
        var (source, _) = Create(req => StubHttpHandler.Html(req, PageOf(req) switch
        {
            1 => SearchPage(Enumerable.Range(1, 30)),
            _ => SearchPage(Enumerable.Range(30, 3)),   // 30 again + 31, 32
        }), FakePageRenderer.Returning(""));

        var listings = await source.SearchAsync(new JobSearch.JobSearchQuery { Category = "sw-architekt" }, maxPages: 5);

        Assert.Equal(32, listings.Count);
        Assert.Equal("1", listings[0].ExternalId);
        Assert.Equal("32", listings[^1].ExternalId);
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

    // ---------- BuildSearchUrl & ToSlug ----------

    [Theory]
    [InlineData(null, ".NET architekt", "Praha", JobSearch.JobAge.Any, 1, "/prace/praha/?q%5B%5D=.NET%20architekt")]
    [InlineData(null, "C#", null, JobSearch.JobAge.Any, 1, "/prace/?q%5B%5D=C%23")]                       // '#' must be escaped
    [InlineData("sw-architekt", null, "Praha", JobSearch.JobAge.ThreeDays, 1, "/prace/praha/sw-architekt/?date=3d")]
    [InlineData("java-vyvojar", null, null, JobSearch.JobAge.Day, 2, "/prace/java-vyvojar/?date=24h&page=2")]
    [InlineData("sw-architekt", "Azure", "Hradec Králové", JobSearch.JobAge.Day, 1, "/prace/hradec-kralove/sw-architekt/?q%5B%5D=Azure&date=24h")]
    [InlineData("/net-vyvojar/", null, null, JobSearch.JobAge.Any, 1, "/prace/net-vyvojar/")]              // slashes around category tolerated
    [InlineData(null, null, null, JobSearch.JobAge.Any, 1, "/prace/")]                                     // nothing set = all jobs
    public void BuildSearchUrl_creates_jobs_cz_search_url(
        string? category, string? keywords, string? location, JobSearch.JobAge maxAge, int page, string expected)
    {
        var query = new JobSearch.JobSearchQuery { Category = category, Keywords = keywords, Location = location, MaxAge = maxAge, Page = page };

        Assert.Equal(expected, JobsCzSource.BuildSearchUrl(query));
    }

    [Theory]
    [InlineData("Praha", "praha")]
    [InlineData("Hradec Králové", "hradec-kralove")]
    [InlineData("České Budějovice", "ceske-budejovice")]
    [InlineData("  Ústí nad Labem ", "usti-nad-labem")]
    [InlineData("Frýdek-Místek", "frydek-mistek")]
    [InlineData("Praha 4", "praha-4")]       // valid slug format – whether jobs.cz knows it is another matter (404)
    [InlineData("/brno/", "brno")]
    public void ToSlug_converts_place_name_to_url_segment(string location, string expected)
    {
        Assert.Equal(expected, JobsCzSource.ToSlug(location));
    }
}
