using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Validators;

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
    public const string MicrosoftScheme = "microsoft";
    public const string GoogleScheme = "google";

    public static IServiceCollection AddJobScoutAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var devSignIn = config.GetSection(DevSignInOptions.SectionName).Get<DevSignInOptions>() ?? new DevSignInOptions();
        if (devSignIn.Enabled && env.IsProduction())
            throw new InvalidOperationException(
                "Auth:DevSignIn:Enabled je zapnuté v Production – přihlášení bez hesla je jen pro vývoj (ADR-0014).");
        services.AddSingleton(devSignIn);

        var auth = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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

        var external = new List<string>();

        if (config.GetSection("Auth:Microsoft").Get<ExternalProviderOptions>() is { IsConfigured: true } microsoft)
        {
            auth.AddOpenIdConnect(MicrosoftScheme, "Microsoft", o =>
            {
                UseProvider(o, microsoft, MicrosoftScheme);
                o.Authority = "https://login.microsoftonline.com/common/v2.0";
                // "common" = any work tenant + personal accounts: each tenant has its own issuer – validate it Microsoft's way
                o.TokenValidationParameters.IssuerValidator = AadIssuerValidator.GetAadIssuerValidator(o.Authority).Validate;
            });
            external.Add(MicrosoftScheme);
        }

        if (config.GetSection("Auth:Google").Get<ExternalProviderOptions>() is { IsConfigured: true } google)
        {
            auth.AddOpenIdConnect(GoogleScheme, "Google", o =>
            {
                UseProvider(o, google, GoogleScheme);
                o.Authority = "https://accounts.google.com";
            });
            external.Add(GoogleScheme);
        }

        services.AddSingleton(new ExternalProviders(external));
        services.AddAuthorization();

        return services;
    }

    public sealed class ExternalProviderOptions
    {
        public string? ClientId { get; init; }
        public string? ClientSecret { get; init; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    }

    /// <summary>Configured external providers = the buttons on the login page.</summary>
    public sealed record ExternalProviders(IReadOnlyList<string> Schemes);

    private static void UseProvider(OpenIdConnectOptions o, ExternalProviderOptions provider, string scheme)
    {
        o.ClientId = provider.ClientId;
        o.ClientSecret = provider.ClientSecret;
        o.ResponseType = OpenIdConnectResponseType.Code;   // authorization code flow, PKCE on by default
        o.CallbackPath = $"/signin-{scheme}";               // = the redirect URI registered at the provider
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("email");
        o.Scope.Add("profile");
        o.MapInboundClaims = false;                         // keep "sub", "email", "name" as the provider sent them
        o.SaveTokens = false;                               // BFF: we never call the provider's APIs
        o.Events.OnTicketReceived = ctx => ExternalSignIn.CompleteAsync(ctx, scheme);
    }
}
