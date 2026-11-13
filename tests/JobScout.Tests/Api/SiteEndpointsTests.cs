using System.Net;
using System.Net.Http.Json;

namespace JobScout.Tests.Api;

/// <summary>
/// The privacy page (GDPR) and the footer are public: who runs the app and how to reach them come from configuration
/// (Operator:*), so a fork runs under its own name.
/// </summary>
public sealed class SiteEndpointsTests : IDisposable
{
    private readonly JobScoutApiFactory _factory = new(settings: new Dictionary<string, string>
    {
        ["Operator:Name"] = "Jana Provozovatelka",
        ["Operator:Email"] = "privacy@example.com",
        ["Operator:AboutUrl"] = "https://example.com/about",
    });

    public void Dispose() => _factory.Dispose();

    private sealed record Site(string OperatorName, string OperatorEmail, string AboutUrl);

    [Fact]
    public async Task Operator_comes_from_configuration_without_sign_in()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/site");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Site("Jana Provozovatelka", "privacy@example.com", "https://example.com/about"),
            await response.Content.ReadFromJsonAsync<Site>());
    }

    [Fact]
    public async Task The_privacy_page_is_not_behind_the_sign_in()
    {
        // No Angular build in the tests – the point: no 401, anybody can read the privacy page
        var response = await _factory.CreateBrowser().GetAsync("/privacy");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void The_default_configuration_names_the_operator()
    {
        // The real values live in appsettings.json – the page must never go out without a controller and a contact
        var settings = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"))).RootElement.GetProperty("Operator");

        Assert.False(string.IsNullOrWhiteSpace(settings.GetProperty("Name").GetString()));
        Assert.Contains("@", settings.GetProperty("Email").GetString());
        Assert.StartsWith("https://", settings.GetProperty("AboutUrl").GetString());
    }
}
