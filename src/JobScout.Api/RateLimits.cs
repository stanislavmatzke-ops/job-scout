using System.Security.Claims;
using System.Threading.RateLimiting;
using JobScout.Api.Auth;

namespace JobScout.Api;

/// <summary>
/// Configuration "RateLimits" (13 Oct, review against "vibe coding" risks): a plain request limit in front of the API,
/// against floods and scripts. Per signed-in user (or per IP address when signed out), a fixed one-minute window.
/// The sign-in endpoints get a tighter limit per IP. Generous on purpose – a person clicking never hits it;
/// what AI may cost is limited separately and more finely (ADR-0021).
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public bool Enabled { get; init; } = true;
    public int ApiPerMinute { get; init; } = 300;
    public int AuthPerMinute { get; init; } = 30;
}

public static class RateLimits
{
    public static IServiceCollection AddJobScoutRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
        if (!options.Enabled)
            return services;

        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                if (!path.StartsWithSegments("/api"))
                    return RateLimitPartition.GetNoLimiter("app");          // the Angular files, /health

                // RemoteIpAddress is the browser's address – UseForwardedHeaders runs first (Program.cs)
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (path.StartsWithSegments("/api/auth"))
                    return RateLimitPartition.GetFixedWindowLimiter("auth:" + ip, _ => Window(options.AuthPerMinute));

                var who = context.User.FindFirstValue(JobScoutClaims.UserId) is { } userId ? "user:" + userId : "ip:" + ip;
                return RateLimitPartition.GetFixedWindowLimiter("api:" + who, _ => Window(options.ApiPerMinute));
            });
        });
    }

    /// <summary>After authentication – the limit is per user, not per shared office IP.</summary>
    public static IApplicationBuilder UseJobScoutRateLimits(this WebApplication app)
    {
        var options = app.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
        return options.Enabled ? app.UseRateLimiter() : app;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    };
}
