using System.Security.Claims;
using JobScout.Api.Auth;
using JobScout.Application.UserFeedback;
using JobScout.Core.Domain;

namespace JobScout.Api.UserFeedback;

public sealed record FeedbackRequest(FeedbackKind? Kind, int? Rating, string? Message, bool ReplyWanted);

/// <summary>"Zpětná vazba": anyone inside the app (also the demo and suspended users) can write; the admins read.</summary>
public static class FeedbackEndpoints
{
    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/feedback", async (FeedbackRequest body, ClaimsPrincipal user, SendFeedback send, CancellationToken ct) =>
        {
            if (body.Kind is not { } kind || !Enum.IsDefined(kind))
                return Results.Problem("Vyberte prosím, čeho se zpráva týká (Idea, Problem, Praise, Other).",
                    statusCode: StatusCodes.Status400BadRequest);
            try
            {
                return await send.ExecuteAsync(user.GetUserId(), kind, body.Rating, body.Message, body.ReplyWanted, ct) switch
                {
                    SendFeedbackResult.Sent => Results.NoContent(),
                    _ => Results.Problem("Děkujeme, dnes už jsme od vás dostali zpráv dost. Další můžete poslat zítra.",
                        statusCode: StatusCodes.Status429TooManyRequests),
                };
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization();          // default policy: Approved or Suspended, the demo included

        app.MapGet("/api/admin/feedback", async (ListFeedback list, CancellationToken ct) => Results.Ok(await list.ExecuteAsync(ct)))
            .RequireAuthorization(Policies.Admin);

        return app;
    }
}
