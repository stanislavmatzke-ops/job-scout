using System.Net;
using JobScout.Application.Jobs;
using JobScout.Infrastructure.JobsCz;
using JobScout.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Infrastructure.JobsCz;

/// <summary>
/// ADR-0015: a profession name is turned into a jobs.cz slug and verified by one GET of the public listing page
/// (200 = exists, 404 = not). And the commute radius goes into the search URL.
/// </summary>
public class JobsCzProfessionAndRadiusTests
{
    private const string ListingHtml = "<html><head><title>Nabídka práce Lakýrník – Jobs.cz</title></head><body></body></html>";

    private static (JobsCzSource Source, StubHttpHandler Http) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new StubHttpHandler(respond);
        var client = new HttpClient(http) { BaseAddress = new Uri("https://www.jobs.cz") };
        return (new JobsCzSource(client, FakePageRenderer.Returning(""), NullLogger<JobsCzSource>.Instance), http);
    }

    private static HttpResponseMessage NotFound(HttpRequestMessage request) =>
        new(HttpStatusCode.NotFound) { RequestMessage = request };

    // ---------- ResolveCategoryAsync ----------

    [Fact]
    public async Task Existing_profession_resolves_to_its_slug()
    {
        var (source, http) = Create(req => StubHttpHandler.Html(req, ListingHtml));

        var category = await source.ResolveCategoryAsync("Lakýrník");

        Assert.NotNull(category);
        Assert.Equal("lakyrnik", category.Id);
        Assert.Equal("Lakýrník", category.Label);
        Assert.Equal("/prace/lakyrnik/", Assert.Single(http.Requests).AbsolutePath);
    }

    [Theory]
    [InlineData("Zedník", "zednik")]
    [InlineData("Stavbyvedoucí", "stavbyvedouci")]
    [InlineData("Zdravotní sestra", "zdravotni-sestra")]
    [InlineData("  Svářeč ", "svarec")]
    public async Task Profession_label_becomes_slug_without_diacritics(string label, string slug)
    {
        var (source, http) = Create(req => StubHttpHandler.Html(req, ListingHtml));

        var category = await source.ResolveCategoryAsync(label);

        Assert.Equal(slug, category?.Id);
        Assert.Equal($"/prace/{slug}/", Assert.Single(http.Requests).AbsolutePath);
    }

    [Fact]
    public async Task Unknown_profession_404_resolves_to_null()
    {
        // "autolakyrnik" is not a jobs.cz profession – the planner then falls back to free text
        var (source, _) = Create(NotFound);

        Assert.Null(await source.ResolveCategoryAsync("Autolakýrník"));
    }

    [Fact]
    public async Task Known_catalogue_category_needs_no_request()
    {
        var (source, http) = Create(_ => throw new InvalidOperationException("no request expected"));

        var category = await source.ResolveCategoryAsync("SW architekt");

        Assert.Equal("sw-architekt", category?.Id);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("–")]      // nothing left after slugging
    public async Task Empty_label_resolves_to_null_without_request(string label)
    {
        var (source, http) = Create(_ => throw new InvalidOperationException("no request expected"));

        Assert.Null(await source.ResolveCategoryAsync(label));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Redirect_to_another_page_is_not_a_category()
    {
        // If jobs.cz ever redirects an unknown slug (e.g. to /prace/), 200 alone must not count as "exists"
        var (source, _) = Create(req => StubHttpHandler.Html(req, ListingHtml, finalUrl: "https://www.jobs.cz/prace/"));

        Assert.Null(await source.ResolveCategoryAsync("Neexistující profese"));
    }

    [Fact]
    public async Task Server_error_is_reported_not_treated_as_missing()
    {
        // 500 ≠ "profession doesn't exist" – the caller must not store a wrong plan because of an outage
        var (source, _) = Create(req => new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = req });

        await Assert.ThrowsAsync<HttpRequestException>(() => source.ResolveCategoryAsync("Lakýrník"));
    }

    // ---------- Commute radius in the search URL ----------

    [Theory]
    [InlineData("lakyrnik", "Kolín", 30, "/prace/kolin/lakyrnik/?locality%5Bradius%5D=30&date=24h")]
    [InlineData("zednik", "Opava", 40, "/prace/opava/zednik/?locality%5Bradius%5D=40&date=24h")]
    [InlineData("lakyrnik", "Kolín", 0, "/prace/kolin/lakyrnik/?date=24h")]   // 0 = source default
    [InlineData("lakyrnik", null, 30, "/prace/lakyrnik/?date=24h")]           // radius without a place makes no sense
    public void BuildSearchUrl_adds_radius_only_with_a_location(string category, string? location, int radiusKm, string expected)
    {
        var query = new JobSearch.JobSearchQuery
        {
            Category = category, Location = location, RadiusKm = radiusKm, MaxAge = JobSearch.JobAge.Day,
        };

        Assert.Equal(expected, JobsCzSource.BuildSearchUrl(query));
    }

    [Fact]
    public void Radius_comes_after_keywords_and_before_date_and_page()
    {
        var query = new JobSearch.JobSearchQuery
        {
            Keywords = "Standox", Location = "Kolín", RadiusKm = 30, MaxAge = JobSearch.JobAge.ThreeDays, Page = 2,
        };

        Assert.Equal("/prace/kolin/?q%5B%5D=Standox&locality%5Bradius%5D=30&date=3d&page=2", JobsCzSource.BuildSearchUrl(query));
    }
}
