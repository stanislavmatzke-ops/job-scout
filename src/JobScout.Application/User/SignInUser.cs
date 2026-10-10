using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Users;

public enum SignInStatus { SignedIn, InvitationRequired, InvitationInvalid }

public sealed record SignInResult(SignInStatus Status, User? User = null);

public sealed record SignInRequest(string Provider, string Subject, string Email, string DisplayName, string? InviteCode = null);

/// <summary>
/// Sign-in: find the user by provider + subject (ADR-0011 – never by e-mail). A known user always gets in and their
/// contact is refreshed. A NEW identity needs a valid invitation – except dev personas (ADR-0014).
/// </summary>
public sealed class SignInUser(IAppDbContext db, TimeProvider time)
{
    /// <summary>Test personas – dev sign-in exists only outside Production, so they need no invitation.</summary>
    public const string DevProvider = "dev";

    public async Task<SignInResult> ExecuteAsync(SignInRequest request, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var user = await db.Users
            .SingleOrDefaultAsync(u => u.Provider == request.Provider && u.Subject == request.Subject, ct);

        if (user is not null)
        {
            user.UpdateContact(request.Email, request.DisplayName);   // a spare invitation is NOT used up
            await db.SaveChangesAsync(ct);
            return new SignInResult(SignInStatus.SignedIn, user);
        }

        Invitation? invitation = null;
        if (request.Provider != DevProvider)
        {
            if (string.IsNullOrWhiteSpace(request.InviteCode))
                return new SignInResult(SignInStatus.InvitationRequired);

            var hash = Invitation.HashCode(request.InviteCode);
            invitation = await db.Invitations.SingleOrDefaultAsync(i => i.CodeHash == hash, ct);
            if (invitation is null || !invitation.IsUsable(now))
                return new SignInResult(SignInStatus.InvitationInvalid);
        }

        user = new User(request.Provider, request.Subject, request.Email, request.DisplayName, now);
        db.Users.Add(user);
        invitation?.Use(user.Id, now);
        await db.SaveChangesAsync(ct);
        return new SignInResult(SignInStatus.SignedIn, user);
    }
}
