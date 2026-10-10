using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Users;

public enum SignInStatus { SignedIn, InvitationInvalid }

public sealed record SignInResult(SignInStatus Status, User? User = null);

public sealed record SignInRequest(
    string Provider, string Subject, string Email, string DisplayName, string? InviteCode = null, bool IsAdmin = false);

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
            user.UpdateContact(request.Email, request.DisplayName);
            user.RecordSignIn(now);
            if (request.IsAdmin && user.Access != AccessStatus.Approved)
                user.Approve(now);                                   // the owner is never locked out
            await db.SaveChangesAsync(ct);
            return new SignInResult(SignInStatus.SignedIn, user);    // a spare invitation is NOT used up
        }

        Invitation? invitation = null;
        if (!string.IsNullOrWhiteSpace(request.InviteCode))
        {
            var hash = Invitation.HashCode(request.InviteCode);
            invitation = await db.Invitations.SingleOrDefaultAsync(i => i.CodeHash == hash, ct);
            if (invitation is null || !invitation.IsUsable(now))
                return new SignInResult(SignInStatus.InvitationInvalid);
        }

        user = new User(request.Provider, request.Subject, request.Email, request.DisplayName, now);
        user.RecordSignIn(now);
        // Approved straight away: invited, a test persona, or the admin – everyone else asks for access (ADR-0017)
        if (invitation is not null || request.Provider == DevProvider || request.IsAdmin)
            user.Approve(now);

        db.Users.Add(user);
        invitation?.Use(user.Id, now);
        await db.SaveChangesAsync(ct);
        return new SignInResult(SignInStatus.SignedIn, user);
    }
}
