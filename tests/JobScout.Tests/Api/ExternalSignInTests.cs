using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using JobScout.Api.Auth;

namespace JobScout.Tests.Api;

/// <summary>
/// Microsoft/Google sign-in plumbing that can be tested without the real providers:
/// reading the identity from the provider's claims, safe return URLs, the provider list for the login page, CSRF header.
/// The redirect round trip itself is tried by hand (it needs the real Microsoft/Google).
/// </summary>
public sealed class ExternalSignInTests
{
    private sealed record Providers(List<string> External, bool DevSignIn);

    // ---------- claims from the provider ----------

    [Fact]
    public void Identity_is_read_from_standard_oidc_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "abc-123"), new Claim("email", "jana@example.com"), new Claim("name", "Jana Ukázková")], "oidc"));

        var identity = ExternalSignIn.Read(principal);

        Assert.Equal(new ExternalIdentity("abc-123", "jana@example.com", "Jana Ukázková"), identity);
    }

    [Fact]
    public void Microsoft_without_email_falls_back_to_preferred_username_and_name_to_email()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "m-1"), new Claim("preferred_username", "jana@outlook.com")], "oidc"));

        Assert.Equal(new ExternalIdentity("m-1", "jana@outlook.com", "jana@outlook.com"), ExternalSignIn.Read(principal));
    }

    [Fact]
    public void No_subject_means_no_identity()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", "jana@example.com")], "oidc"));

        Assert.Null(ExternalSignIn.Read(principal));
    }

    // ---------- return URL (no open redirect) ----------

    [Theory]
    [InlineData("/matches", "/matches")]
    [InlineData("/matches?view=open", "/matches?view=open")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example.com", "/")]   // somewhere else
    [InlineData("//evil.example.com", "/")]         // protocol-relative = somewhere else
    [InlineData("/\\evil.example.com", "/")]        // browsers treat \ like /
    public void Only_local_return_urls_are_followed(string? returnUrl, string expected)
    {
        Assert.Equal(expected, ReturnUrl.Sanitize(returnUrl));
    }

    // ---------- endpoints ----------

    [Fact]
    public async Task Login_page_learns_which_providers_are_configured()
    {
        using var factory = new JobScoutApiFactory(settings: new Dictionary<string, string>
        {
            ["Auth:Google:ClientId"] = "test-client-id",
            ["Auth:Google:ClientSecret"] = "test-secret",
        });

        var providers = await factory.CreateBrowser().GetFromJsonAsync<Providers>("/api/auth/providers");

        Assert.Equal(new[] { "google" }, providers!.External.ToArray());
        Assert.True(providers.DevSignIn);
    }

    [Fact]
    public async Task Login_with_a_provider_that_is_not_configured_is_404()
    {
        using var factory = new JobScoutApiFactory();

        var response = await factory.CreateBrowser().GetAsync("/api/auth/login/google");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- CSRF ----------

    [Fact]
    public async Task State_changing_call_without_the_csrf_header_is_403()
    {
        using var factory = new JobScoutApiFactory();

        var response = await factory.CreateBrowser(csrfHeader: false).PostAsync("/api/auth/dev/zednik", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reading_does_not_need_the_csrf_header()
    {
        using var factory = new JobScoutApiFactory();

        var response = await factory.CreateBrowser(csrfHeader: false).GetAsync("/api/auth/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
