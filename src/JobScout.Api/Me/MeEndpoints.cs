using System.Security.Claims;
using JobScout.Api.Auth;
using JobScout.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Api.Me;

public sealed record MeResponse(Guid Id, string Provider, string DisplayName, string Email);

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (ClaimsPrincipal principal, IAppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);

            // Cookie of a deleted user → behave as signed out
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new MeResponse(user.Id, user.Provider, user.DisplayName, user.Email));
        }).RequireAuthorization();

        return app;
    }
}
