using System.Net;

namespace JobScout.Tests.Api;

/// <summary>13 Oct – a plain request limit in front of the API (floods, scripts); the app's own files are never limited.</summary>
public sealed class RateLimitTests : IDisposable
{
    private readonly JobScoutApiFactory _factory = new(settings: new Dictionary<string, string>
    {
        ["RateLimits:AuthPerMinute"] = "3",
        ["RateLimits:ApiPerMinute"] = "5",
    });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Too_many_sign_in_requests_are_429()
    {
        var browser = _factory.CreateBrowser();

        for (var i = 0; i < 3; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await browser.GetAsync("/api/auth/providers")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await browser.GetAsync("/api/auth/providers")).StatusCode);
    }

    [Fact]
    public async Task Health_is_never_limited()
    {
        var browser = _factory.CreateBrowser();

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/health")).StatusCode);
    }
}
