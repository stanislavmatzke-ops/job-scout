using System.Security.Claims;
using JobScout.Api.Auth;
using JobScout.Application.Scouting;

namespace JobScout.Api.Scouting;

public static class ScoutEndpoints
{
    public static IEndpointRouteBuilder MapScoutEndpoints(this IEndpointRouteBuilder app)
    {
        // The jobs page asks while the first search runs, then reloads the list (6h)
        app.MapGet("/api/scout/status", async (ClaimsPrincipal user, GetScoutStatus status, CancellationToken ct) =>
                Results.Ok(await status.ExecuteAsync(user.GetUserId(), ct)))
            .RequireAuthorization();

        return app;
    }
}
