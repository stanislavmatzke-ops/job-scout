using System.Security.Claims;
using JobScout.Application.Users;
using Microsoft.AspNetCore.Authentication;

namespace JobScout.Api.Auth;

public sealed record ExternalIdentity(string Subject, string Email, string DisplayName);

/// <summary>Microsoft/Google sent the user back: find our user (or "not invited") and issue OUR cookie.</summary>
public static class ExternalSignIn
{
    /// <summary>The invitation code travels to the provider and back inside the protected OIDC state.</summary>
    public const string InviteKey = "jobscout:invite";

    /// <summary>Standard OIDC claims (MapInboundClaims = false keeps their original names).</summary>
    public static ExternalIdentity? Read(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        // Personal Microsoft accounts don't always send "email"
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue("preferred_username") ?? "";
        var name = principal.FindFirstValue("name");
        return new ExternalIdentity(subject, email, string.IsNullOrWhiteSpace(name) ? email : name);
    }

    internal static async Task CompleteAsync(TicketReceivedContext context, string provider)
    {
        var identity = context.Principal is null ? null : Read(context.Principal);
        string? invite = null;
        context.Properties?.Items.TryGetValue(InviteKey, out invite);

        SignInResult? result = null;
        if (identity is not null)
        {
            var services = context.HttpContext.RequestServices;
            var signIn = services.GetRequiredService<SignInUser>();
            var admins = services.GetRequiredService<AdminOptions>();
            result = await signIn.ExecuteAsync(
                new SignInRequest(provider, identity.Subject, identity.Email, identity.DisplayName, invite,
                    IsAdmin: admins.IsAdmin(provider, identity.Subject)),
                context.HttpContext.RequestAborted);
        }

        if (result is not { Status: SignInStatus.SignedIn, User: { } user })
        {
            var error = result?.Status switch
            {
                SignInStatus.InvitationInvalid => "invitation-invalid",
                _ => "no-identity"
            };
            context.Response.Redirect($"/login?error={error}");
            context.HandleResponse();                              // nothing is signed in
            return;
        }

        // From here on the cookie carries OUR user id – the provider's claims and tokens are dropped
        context.Principal = AuthEndpoints.CreatePrincipal(user);
    }
}
