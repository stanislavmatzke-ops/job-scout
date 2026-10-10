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

        var user = await signIn.ExecuteAsync(new SignInRequest("dev", persona, Email: "", DisplayName: persona), ct);
        await SignInCookieAsync(http, user);
        return Results.NoContent();
    }

    /// <summary>The same for every provider (dev now, Microsoft/Google in 5e): the cookie carries our user id.</summary>
    internal static Task SignInCookieAsync(HttpContext http, User user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(JobScoutClaims.UserId, user.Id.ToString()),
                new Claim(JobScoutClaims.Provider, user.Provider),
                new Claim(ClaimTypes.Name, user.DisplayName),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return http.SignInAsync(new ClaimsPrincipal(identity));
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex PersonaName();
}
