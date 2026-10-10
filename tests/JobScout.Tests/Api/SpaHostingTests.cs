using System.Net;

namespace JobScout.Tests.Api;

/// <summary>
/// 6f – the API also serves the Angular app. An unknown /api address must stay an API error (JSON 404) –
/// never the app's index.html with 200, which would hide typos in the frontend.
/// </summary>
public sealed class SpaHostingTests : IDisposable
{
    private readonly JobScoutApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api/matches/oops/more")]
    public async Task Unknown_api_address_is_a_json_404(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_app_routes_are_not_behind_the_api_sign_in()
    {
        // No Angular build in the tests (wwwroot is produced by npm run build) – the point: no 401, the API's
        // authorization doesn't guard the app's pages; the app itself decides where to send a stranger
        var response = await _factory.CreateBrowser().GetAsync("/matches/123");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
