using System.Security.Claims;
using JobScout.Api.Auth;
using JobScout.Application.Abstractions;
using JobScout.Application.Users;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Api.Me;

public sealed record MeResponse(
    Guid Id, string Provider, string DisplayName, string Email,
    AccessStatus Access, DateTimeOffset? AccessRequestedAt, string? SuspendedReason, bool IsAdmin,
    string Identity);

public sealed record AccessRequestBody(string? Message);

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (ClaimsPrincipal principal, IAppDbContext db, AdminOptions admins, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);

            // Cookie of a deleted user → behave as signed out
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new MeResponse(user.Id, user.Provider, user.DisplayName, user.Email, user.Access,
                    user.AccessRequestedAt, user.SuspendedReason, admins.IsAdmin(user.Provider, user.Subject),
                    $"{user.Provider}:{user.Subject}"));
        }).RequireAuthorization(Policies.SignedIn);

        app.MapPost("/api/access/request", async (AccessRequestBody body, ClaimsPrincipal principal,
            RequestAccess request, CancellationToken ct) =>
        {
            try
            {
                return await request.ExecuteAsync(principal.GetUserId(), body.Message, ct)
                    ? Results.NoContent()
                    : Results.Problem("Přístup již máte.", statusCode: StatusCodes.Status409Conflict);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(Policies.SignedIn);

        return app;
    }
}
