using System.Globalization;
using System.Security.Claims;
using JobScout.Api.Auth;
using JobScout.Application.Limits;

namespace JobScout.Api.Limits;

/// <summary>AI limits for clicks (ADR-0021): what is left, and the 429 answer when nothing is.</summary>
public static class AiLimitEndpoints
{
    public static IEndpointRouteBuilder MapAiLimitEndpoints(this IEndpointRouteBuilder app)
    {
        // Read-only, so also for suspended users and the demo (they simply see their buttons switched off anyway)
        app.MapGet("/api/me/limits", async (ClaimsPrincipal user, AiQuota quota, CancellationToken ct) =>
            Results.Ok(await quota.GetStatusAsync(user.GetUserId(), ct))).RequireAuthorization();
        return app;
    }

    /// <summary>The AI buttons' endpoints: a reached limit → 429 with the reason and when to try again.</summary>
    public static RouteHandlerBuilder WithAiLimits(this RouteHandlerBuilder endpoint) =>
        endpoint.AddEndpointFilter<AiLimitFilter>();
}

/// <summary>
/// AiLimitExceededException → 429 Too Many Requests, Retry-After and a problem with "reason" and "retryAt".
/// The detail is Czech (like every API message); the UI words it in the user's language by the reason.
/// </summary>
public sealed class AiLimitFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (AiLimitExceededException ex)
        {
            var denial = ex.Denial;
            var http = context.HttpContext;
            if (denial.RetryAt is { } retryAt)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling((retryAt - DateTimeOffset.UtcNow).TotalSeconds));
                http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            }

            return Results.Problem(
                detail: Detail(denial),
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Limit funkcí AI",
                extensions: new Dictionary<string, object?>
                {
                    ["reason"] = denial.Reason.ToString(),
                    ["limit"] = denial.Limit,
                    ["retryAt"] = denial.RetryAt,
                });
        }
    }

    private static string Detail(AiLimitDenial d)
    {
        var when = d.RetryAt is { } at
            ? $" Znovu to půjde {TimeZoneInfo.ConvertTime(at, Prague).ToString("d. M. H:mm", CultureInfo.GetCultureInfo("cs-CZ"))}."
            : "";
        return d.Reason switch
        {
            AiLimitReason.Profile => $"Profil lze nechat vyhodnotit nejvýš {d.Limit}× za 24 hodin.{when}",
            AiLimitReason.CoverLetter => $"Průvodní dopis lze nechat napsat nejvýš {d.Limit}× za 24 hodin.{when}",
            AiLimitReason.Evaluation => $"Nabídky lze nechat ohodnotit nejvýš {d.Limit}× za 24 hodin.{when}",
            AiLimitReason.JobImport => $"Inzeráty lze ručně vložit nejvýš {d.Limit}× za 24 hodin.{when}",
            AiLimitReason.LetterRewrites => $"K této nabídce už jste nechali napsat nový návrh {d.Limit}×. Dopis můžete dál upravit sami.",
            AiLimitReason.UserCost => $"Dnešní limit funkcí AI je vyčerpaný.{when}",
            _ => "Funkce AI jsou dočasně pozastavené. Zkuste to prosím později – ranní hledání běží dál.",
        };
    }

    private static readonly TimeZoneInfo Prague = FindPrague();

    private static TimeZoneInfo FindPrague()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");     // IANA ids work on Windows too (.NET 6+, ICU)
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
