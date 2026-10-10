using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace JobScout.Api.Auth;

public static class JobScoutClaims
{
    /// <summary>Our own user id – put into the cookie at sign-in, so endpoints never look up provider + subject again.</summary>
    public const string UserId = "jobscout:user_id";
    public const string Provider = "jobscout:provider";
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JobScoutClaims.UserId), out var id)
            ? id
            : throw new InvalidOperationException("Přihlášený uživatel nemá claim jobscout:user_id.");
}

public sealed class DevSignInOptions
{
    public const string SectionName = "Auth:DevSignIn";
    public bool Enabled { get; init; }
}

public static class AuthSetup
{
    /// <summary>__Host- prefix: the browser accepts it only with Secure, Path=/ and no Domain – can't be overwritten by a subdomain.</summary>
    public const string CookieName = "__Host-jobscout";

    public static IServiceCollection AddJobScoutAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var devSignIn = config.GetSection(DevSignInOptions.SectionName).Get<DevSignInOptions>() ?? new DevSignInOptions();
        if (devSignIn.Enabled && env.IsProduction())
            throw new InvalidOperationException(
                "Auth:DevSignIn:Enabled je zapnuté v Production – přihlášení bez hesla je jen pro vývoj (ADR-0014).");
        services.AddSingleton(devSignIn);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;                              // JavaScript can't read it (XSS)
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;     // https only
                o.Cookie.SameSite = SameSiteMode.Lax;                  // not sent with cross-site POSTs (CSRF); Lax survives the OIDC redirect back
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;

                // An API answers with status codes – a redirect to a login page would be swallowed by fetch()
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });
        services.AddAuthorization();

        return services;
    }
}
