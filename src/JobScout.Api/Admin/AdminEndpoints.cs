using JobScout.Api.Auth;
using JobScout.Application.Admin;
using JobScout.Application.Users;

namespace JobScout.Api.Admin;

public sealed record SuspendRequest(string? Reason);
public sealed record InvitationRequest(string? Note);

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

        admin.MapGet("/invitations", async (ListInvitations list, CancellationToken ct) => Results.Ok(await list.ExecuteAsync(ct)));

        // The code is in the answer only this once – the database keeps just its hash (ADR-0011).
        // The browser builds the sign-in links from it (it knows its own origin, the API behind a proxy may not).
        admin.MapPost("/invitations", async (InvitationRequest body, CreateInvitation create, CancellationToken ct) =>
        {
            var note = body.Note?.Trim() ?? "";
            if (note.Length == 0)
                return Results.Problem("Napište prosím, pro koho pozvánka je – v přehledu pak poznáte, kdo ji použil.",
                    statusCode: StatusCodes.Status400BadRequest);
            if (note.Length > MaxInvitationNoteLength)
                return Results.Problem($"Poznámka může mít nejvýš {MaxInvitationNoteLength} znaků.",
                    statusCode: StatusCodes.Status400BadRequest);

            return Results.Ok(await create.ExecuteAsync(note, ct));
        });

        admin.MapPost("/users/{id:guid}/reject", async (Guid id, SuspendRequest body, RejectUser reject, CancellationToken ct) =>
        {
            try
            {
                return await reject.ExecuteAsync(id, body.Reason, ct) switch
                {
                    RejectResult.Rejected => Results.NoContent(),
                    RejectResult.NotPending => Results.Problem("Uživatel už přístup má – lze ho jen pozastavit.",
                        statusCode: StatusCodes.Status409Conflict),
                    _ => Results.NotFound(),
                };
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return app;
    }

    public const int MaxInvitationNoteLength = 200;
}
