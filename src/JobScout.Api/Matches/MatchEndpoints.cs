using System.Security.Claims;
using JobScout.Api.Auth;
using JobScout.Api.Limits;
using JobScout.Application.ManualJobs;
using JobScout.Application.Matches;
using JobScout.Core.Domain;
using Microsoft.AspNetCore.Mvc;

namespace JobScout.Api.Matches;

public sealed record ChangeStatusRequest(MatchStatus? Status, string? Note);   // nullable: a missing status must be 400, not "New"
public sealed record BulkStatusRequest(List<Guid>? Ids, MatchStatus? Status, string? Note);
public sealed record BulkStatusResult(int Changed);
public sealed record UpdateNotesRequest(string? Notes);
public sealed record AddJobResponse(Guid Id, bool Evaluated, bool AlreadyInList);
public sealed record UpdateLetterRequest(string? Text);

public static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var matches = app.MapGroup("/api/matches").RequireAuthorization();

        matches.MapGet("", async (string? view, ClaimsPrincipal user, ListMatches list, CancellationToken ct) =>
            TryParseView(view, out var parsed)
                ? Results.Ok(await list.ExecuteAsync(user.GetUserId(), parsed, ct))
                : Results.Problem("Neznámý pohled. Povolené: Open, Saved, InProgress, Closed, TriagedOut.",
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

        // Bulk action on the list: one status (and note) for the selected jobs
        matches.MapPost("/status", async (BulkStatusRequest body, ClaimsPrincipal user,
            ChangeMatchStatus change, CancellationToken ct) =>
        {
            if (body.Status is not { } status || !Enum.IsDefined(status))
                return Results.Problem("Chybí stav nebo je neznámý.", statusCode: StatusCodes.Status400BadRequest);

            try
            {
                var changed = await change.ExecuteManyAsync(user.GetUserId(), body.Ids ?? [], status, body.Note, ct);
                return Results.Ok(new BulkStatusResult(changed));
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

        // ---------- a job ad from elsewhere (ADR-0013): text, link or file → private ad (+ evaluation) ----------

        matches.MapPost("/manual", async ([FromForm] string? text, [FromForm] string? url, [FromForm] string? status,
            [FromForm] string? evaluate, IFormFile? file, ClaimsPrincipal user, AddJobManually add, CancellationToken ct) =>
        {
            var initial = MatchStatus.New;
            if (status is not null && !TryParseName(status, out initial))
                return Results.Problem("Neznámý stav.", statusCode: StatusCodes.Status400BadRequest);
            if (file?.Length > AddJobManually.MaxFileBytes)          // check before reading it into memory
                return Results.Problem("Soubor je příliš velký.", statusCode: StatusCodes.Status400BadRequest);

            ManualJobFile? upload = null;
            if (file is not null)
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                upload = new ManualJobFile(file.FileName, buffer.ToArray());
            }

            AddJobResult result;
            try
            {
                result = await add.ExecuteAsync(user.GetUserId(),
                    new AddJobInput(text, url, upload, initial, Evaluate: !bool.TryParse(evaluate, out var now) || now), ct);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            return result.Status switch
            {
                AddJobStatus.Added => Results.Ok(new AddJobResponse(result.MatchId!.Value, result.Evaluated, AlreadyInList: false)),
                AddJobStatus.AlreadyInList => Results.Ok(new AddJobResponse(result.MatchId!.Value, Evaluated: false, AlreadyInList: true)),
                AddJobStatus.NoProfile => Results.Problem("Nejprve nahrajte své CV – bez profilu nelze nabídku ohodnotit.",
                    statusCode: StatusCodes.Status409Conflict),
                AddJobStatus.NotAJobAd => Results.Problem("V podkladech jsme nenašli nabídku práce. Zkontrolujte prosím, co jste vložili.",
                    statusCode: StatusCodes.Status422UnprocessableEntity, extensions: new Dictionary<string, object?> { ["reason"] = "NotAJobAd" }),
                AddJobStatus.PageUnavailable => Results.Problem(result.Message ?? "Stránku se nepodařilo načíst.",
                    statusCode: StatusCodes.Status422UnprocessableEntity, extensions: new Dictionary<string, object?> { ["reason"] = "PageUnavailable" }),
                AddJobStatus.AiFailed => Results.Problem("Inzerát se nepodařilo přečíst. Zkuste to prosím znovu.",
                    statusCode: StatusCodes.Status502BadGateway),
                _ => Results.Problem("Portál s nabídkami momentálně neodpovídá. Zkuste to prosím za chvíli.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
            };
        })
        .DisableAntiforgery()    // CSRF: the X-CSRF header for the whole API (5e)
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(AddJobManually.MaxFileBytes + 1024 * 1024))
        .WithFormOptions(multipartBodyLengthLimit: AddJobManually.MaxFileBytes + 1024 * 1024)
        .RequireAuthorization(Policies.Active)
        .WithAiLimits();

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
                EvaluateNowStatus.NoProfile => Results.Problem("Nejprve nahrajte své CV – bez profilu nelze nabídku ohodnotit.",
                    statusCode: StatusCodes.Status409Conflict),
                EvaluateNowStatus.AdRemoved => Results.Problem("Inzerát již na portálu není k dispozici.",
                    statusCode: StatusCodes.Status410Gone),
                _ => Results.Problem("Portál s nabídkami momentálně neodpovídá. Zkuste to prosím za chvíli.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
            };
        }).RequireAuthorization(Policies.Active).WithAiLimits();

        matches.MapPost("/{id:guid}/letter", async (Guid id, bool? replace, ClaimsPrincipal user,
            WriteCoverLetterNow write, GetMatchDetail get, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var result = await write.ExecuteAsync(userId, id, replace ?? false, ct);
            return result.Status switch
            {
                WriteLetterStatus.Written => Results.Ok(await get.ExecuteAsync(userId, id, ct)),
                WriteLetterStatus.NotFound => Results.NotFound(),
                WriteLetterStatus.NotEvaluated => Results.Problem("Nejprve nechte nabídku ohodnotit – dopis vychází z hodnocení.",
                    statusCode: StatusCodes.Status409Conflict),
                WriteLetterStatus.AlreadyWritten => Results.Problem("K nabídce již existuje dopis. Nový návrh přepíše ten stávající (replace=true).",
                    statusCode: StatusCodes.Status409Conflict),
                WriteLetterStatus.NoProfile => Results.Problem("Nejprve nahrajte své CV.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem("Dopis se nepodařilo dokončit. Zkuste to prosím znovu.",
                    statusCode: StatusCodes.Status502BadGateway)
            };
        }).RequireAuthorization(Policies.Active).WithAiLimits();

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
        }).RequireAuthorization(Policies.Editor);        // the demo's letters stay as the AI wrote them

        return app;
    }

    /// <summary>A status by name only ("Applied") – Enum.TryParse alone would accept "5" or "Applied,Offer".</summary>
    private static bool TryParseName(string text, out MatchStatus status) =>
        Enum.TryParse(text, ignoreCase: true, out status) && !int.TryParse(text, out _) && Enum.IsDefined(status)
        && !text.Contains(',');

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
