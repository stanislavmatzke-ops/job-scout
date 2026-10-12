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

        // The user starts the first search when the profile is complete (CV + notes) – not on the CV upload,
        // otherwise notes added a minute later would leave the first jobs rated against an older profile.
        // Never started? The morning run picks the user up anyway.
        app.MapPost("/api/scout/first", async (ClaimsPrincipal user, StartFirstScout start, CancellationToken ct) =>
                await start.ExecuteAsync(user.GetUserId(), ct)
                    ? Results.Accepted()
                    : Results.Problem("První hledání už proběhlo, právě běží, nebo ještě nemáte profil.",
                        statusCode: StatusCodes.Status409Conflict))
            .RequireAuthorization(Policies.Active);

        return app;
    }
}
