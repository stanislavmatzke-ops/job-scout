using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Users;

public sealed record SignInRequest(string Provider, string Subject, string Email, string DisplayName);

/// <summary>
/// Sign-in: find the user by provider + subject (ADR-0011 – never by e-mail), create on the first visit,
/// refresh e-mail and name on later visits. Invitations come in 5e.
/// </summary>
public sealed class SignInUser(IAppDbContext db, TimeProvider time)
{
    public async Task<User> ExecuteAsync(SignInRequest request, CancellationToken ct = default)
    {
        var user = await db.Users
            .SingleOrDefaultAsync(u => u.Provider == request.Provider && u.Subject == request.Subject, ct);

        if (user is null)
        {
            user = new User(request.Provider, request.Subject, request.Email, request.DisplayName, time.GetUtcNow());
            db.Users.Add(user);
        }
        else
            user.UpdateContact(request.Email, request.DisplayName);

        await db.SaveChangesAsync(ct);
        return user;
    }
}
