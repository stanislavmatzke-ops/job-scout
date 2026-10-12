using System.Security.Claims;
using System.Text.RegularExpressions;
using JobScout.Application.Users;
using JobScout.Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace JobScout.Api.Auth;

public static partial class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        // The login page asks which buttons to show
        auth.MapGet("/providers", (AuthSetup.ExternalProviders providers, DevSignInOptions dev, Demo.DemoOptions demo) =>
            Results.Ok(new
            {
                external = providers.Schemes,
                devSignIn = dev.Enabled,
                demo = demo.Enabled ? Application.Demo.DemoAccount.DisplayName : null,   // the "try it" button
            }));

        // Start of the round trip: redirect to Microsoft/Google; the invitation code travels in the protected state
        auth.MapGet("/login/{provider}", (string provider, string? returnUrl, string? invite, AuthSetup.ExternalProviders providers) =>
        {
            if (!providers.Schemes.Contains(provider))
                return Results.NotFound();

            var properties = new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) };
            if (!string.IsNullOrWhiteSpace(invite))
                properties.Items[ExternalSignIn.InviteKey] = invite.Trim();

            return Results.Challenge(properties, [provider]);
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync();
            return Results.NoContent();
        });

        // Not just "forbidden" when disabled – the endpoint doesn't exist at all (404)
        if (app.ServiceProvider.GetRequiredService<DevSignInOptions>().Enabled)
            auth.MapPost("/dev/{persona}", DevSignInAsync);

        return app;
    }

    /// <summary>Sign in as a test persona without a password (ADR-0014). Same user as the Worker's "dev:{persona}".</summary>
    private static async Task<IResult> DevSignInAsync(string persona, HttpContext http, SignInUser signIn, CancellationToken ct)
    {
        if (!PersonaName().IsMatch(persona))
            return Results.Problem("Persona: jen malá písmena, číslice a pomlčka, nejvýš 40 znaků.",
                statusCode: StatusCodes.Status400BadRequest);

        var result = await signIn.ExecuteAsync(
            new SignInRequest(SignInUser.DevProvider, persona, Email: "", DisplayName: persona), ct);
        await http.SignInAsync(CreatePrincipal(result.User!));
        return Results.NoContent();
    }

    /// <summary>Our session for any provider (dev, Microsoft, Google): the cookie carries our user id.</summary>
    internal static ClaimsPrincipal CreatePrincipal(User user) => new(new ClaimsIdentity(
        [
            new Claim(JobScoutClaims.UserId, user.Id.ToString()),
            new Claim(JobScoutClaims.Provider, user.Provider),
            new Claim(ClaimTypes.Name, user.DisplayName),
        ],
        CookieAuthenticationDefaults.AuthenticationScheme));

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex PersonaName();
}
