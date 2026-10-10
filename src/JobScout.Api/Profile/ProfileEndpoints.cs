using System.Security.Claims;
using JobScout.Api.Auth;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;

namespace JobScout.Api.Profile;

public sealed record UpdateProfileNotesRequest(string? Notes);
public sealed record UpdateProfileSettingsRequest(GrammaticalGender? Gender);

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var profile = app.MapGroup("/api/profile").RequireAuthorization();

        profile.MapGet("", async (ClaimsPrincipal user, GetProfile get, CancellationToken ct) =>
            Results.Ok(await get.ExecuteAsync(user.GetUserId(), ct)));

        profile.MapPut("/cv", async (IFormFile? file, ClaimsPrincipal user, UploadCv upload, GetProfile get, CancellationToken ct) =>
        {
            if (file is null)
                return Results.Problem("Chybí soubor s CV (pole „file“).", statusCode: StatusCodes.Status400BadRequest);
            if (file.Length > StoredCv.MaxBytes)                 // check before reading it into memory
                return Results.Problem("CV je příliš velké.", statusCode: StatusCodes.Status400BadRequest);

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            try
            {
                await upload.ExecuteAsync(user.GetUserId(), file.FileName, buffer.ToArray(), ct);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(await get.ExecuteAsync(user.GetUserId(), ct));
        })
        .DisableAntiforgery()    // CSRF protection for the whole API comes in 5e
        .RequireAuthorization(Policies.Active);

        profile.MapPut("/notes", async (UpdateProfileNotesRequest body, ClaimsPrincipal user,
            UpdateProfileNotes update, GetProfile get, CancellationToken ct) =>
        {
            try
            {
                return await update.ExecuteAsync(user.GetUserId(), body.Notes, ct) is null
                    ? Results.Problem("Nejdřív nahraj CV – poznámky se analyzují spolu s ním.",
                        statusCode: StatusCodes.Status409Conflict)
                    : Results.Ok(await get.ExecuteAsync(user.GetUserId(), ct));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(Policies.Active);

        profile.MapPut("/settings", async (UpdateProfileSettingsRequest body, ClaimsPrincipal user,
            UpdateProfileSettings settings, CancellationToken ct) =>
        {
            if (body.Gender is not { } gender || !Enum.IsDefined(gender))
                return Results.Problem("Rod: Unspecified, Masculine nebo Feminine.", statusCode: StatusCodes.Status400BadRequest);

            await settings.ExecuteAsync(user.GetUserId(), gender, ct);
            return Results.NoContent();
        });

        return app;
    }
}
