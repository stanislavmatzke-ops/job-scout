using JobScout.Api.Auth;
using JobScout.Application.Admin;

namespace JobScout.Api.Admin;

public sealed record SuspendRequest(string? Reason);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Policies.Admin);

        admin.MapGet("/users", async (ListUsers list, CancellationToken ct) => Results.Ok(await list.ExecuteAsync(ct)));

        admin.MapPost("/users/{id:guid}/approve", async (Guid id, ApproveUser approve, CancellationToken ct) =>
            await approve.ExecuteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        admin.MapPost("/users/{id:guid}/suspend", async (Guid id, SuspendRequest body, SuspendUser suspend, CancellationToken ct) =>
        {
            try
            {
                return await suspend.ExecuteAsync(id, body.Reason, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return app;
    }
}
