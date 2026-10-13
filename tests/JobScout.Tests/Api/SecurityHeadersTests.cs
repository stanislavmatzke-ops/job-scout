using System.Net;

namespace JobScout.Tests.Api;

/// <summary>13 Oct – security headers on the app and on the API (clickjacking, MIME sniffing, CSP report-only).</summary>
public sealed class SecurityHeadersTests : IDisposable
{
    private readonly JobScoutApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/api/auth/providers")]
    [InlineData("/health")]
    public async Task Every_answer_carries_the_security_headers(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy-Report-Only").Single());
    }

    [Fact]
    public async Task A_cv_far_over_the_limit_is_refused_before_it_is_read()
    {
        var browser = _factory.CreateBrowser();
        await browser.PostAsync("/api/auth/dev/private", null);
        var huge = new MultipartFormDataContent { { new ByteArrayContent(new byte[7 * 1024 * 1024]), "file", "cv.pdf" } };

        var response = await browser.PutAsync("/api/profile/cv", huge);

        Assert.True(response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest,
            $"Expected 413 or 400, got {(int)response.StatusCode}");
    }
}
