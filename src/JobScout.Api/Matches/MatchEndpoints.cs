using System.Security.Claims;
using JobScout.Api.Auth;
using JobScout.Application.Matches;
using JobScout.Core.Domain;

namespace JobScout.Api.Matches;

public sealed record ChangeStatusRequest(MatchStatus? Status, string? Note);   // nullable: a missing status must be 400, not "New"
public sealed record UpdateNotesRequest(string? Notes);
public sealed record UpdateLetterRequest(string? Text);

public static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var matches = app.MapGroup("/api/matches").RequireAuthorization();

        matches.MapGet("", async (string? view, ClaimsPrincipal user, ListMatches list, CancellationToken ct) =>
            TryParseView(view, out var parsed)
                ? Results.Ok(await list.ExecuteAsync(user.GetUserId(), parsed, ct))
                : Results.Problem("Neznámý pohled. Povolené: Open, InProgress, Closed, TriagedOut.",
                    statusCode: StatusCodes.Status400BadRequest));

        matches.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, GetMatchDetail get, CancellationToken ct) =>
            await get.ExecuteAsync(user.GetUserId(), id, ct) is { } detail ? Results.Ok(detail) : Results.NotFound());

        matches.MapPost("/{id:guid}/status", async (Guid id, ChangeStatusRequest body, ClaimsPrincipal user,
            ChangeMatchStatus change, CancellationToken ct) =>
        {
            if (body.Status is not { } status || !Enum.IsDefined(status))
                return Results.Problem("Chybí stav nebo je neznámý.", statusCode: StatusCodes.Status400BadRequest);

            try
            {
                return await change.ExecuteAsync(user.GetUserId(), id, status, body.Note, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        matches.MapPut("/{id:guid}/notes", async (Guid id, UpdateNotesRequest body, ClaimsPrincipal user,
            UpdateMatchNotes update, CancellationToken ct) =>
        {
            try
            {
                return await update.ExecuteAsync(user.GetUserId(), id, body.Notes, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

                // ---------- on demand (5c): each answer tells the UI what to show ----------

        matches.MapPost("/{id:guid}/evaluate", async (Guid id, ClaimsPrincipal user, EvaluateMatchNow evaluate,
            GetMatchDetail get, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var result = await evaluate.ExecuteAsync(userId, id, ct);
            return result.Status switch
            {
                EvaluateNowStatus.Evaluated => Results.Ok(await get.ExecuteAsync(userId, id, ct)),
                EvaluateNowStatus.NotFound => Results.NotFound(),
                EvaluateNowStatus.NoProfile => Results.Problem("Nejdřív nahraj CV – bez profilu není podle čeho hodnotit.",
                    statusCode: StatusCodes.Status409Conflict),
                EvaluateNowStatus.AdRemoved => Results.Problem("Inzerát už na portálu není.",
                    statusCode: StatusCodes.Status410Gone),
                _ => Results.Problem("Portál teď neodpovídá, zkus to za chvíli.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
            };
        }).RequireAuthorization(Policies.Active);

        matches.MapPost("/{id:guid}/letter", async (Guid id, bool? replace, ClaimsPrincipal user,
            WriteCoverLetterNow write, GetMatchDetail get, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var result = await write.ExecuteAsync(userId, id, replace ?? false, ct);
            return result.Status switch
            {
                WriteLetterStatus.Written => Results.Ok(await get.ExecuteAsync(userId, id, ct)),
                WriteLetterStatus.NotFound => Results.NotFound(),
                WriteLetterStatus.NotEvaluated => Results.Problem("Nejdřív nabídku ohodnoť – dopis staví na hodnocení.",
                    statusCode: StatusCodes.Status409Conflict),
                WriteLetterStatus.AlreadyWritten => Results.Problem("Dopis už existuje. Pro nový pošli ?replace=true.",
                    statusCode: StatusCodes.Status409Conflict),
                WriteLetterStatus.NoProfile => Results.Problem("Nejdřív nahraj CV.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem("AI dopis nedopsala, zkus to znovu.",
                    statusCode: StatusCodes.Status502BadGateway)
            };
        }).RequireAuthorization(Policies.Active);

        matches.MapPut("/{id:guid}/letter", async (Guid id, UpdateLetterRequest body, ClaimsPrincipal user,
            UpdateCoverLetter update, CancellationToken ct) =>
        {
            try
            {
                return await update.ExecuteAsync(user.GetUserId(), id, body.Text, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return app;
    }

    /// <summary>Names only, case-insensitive; no view = Open. Enum.TryParse alone would accept "5" or "Open,Closed".</summary>
    private static bool TryParseView(string? text, out MatchView view)
    {
        view = MatchView.Open;
        if (text is null)
            return true;

        return !int.TryParse(text, out _)
               && Enum.TryParse(text, ignoreCase: true, out view)
               && Enum.IsDefined(view);
    }
}
