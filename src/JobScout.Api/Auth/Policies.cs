using System.Security.Claims;
using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Api.Auth;

/// <summary>
/// ADR-0017. Default policy (RequireAuthorization() without a name) = Approved or Suspended: reading and own decisions, no AI.
/// </summary>
public static class Policies
{
    public const string SignedIn = "SignedIn";   // also Pending – "who am I", "ask for access"
    public const string Active = "Active";       // Approved only – anything that calls the AI (costs money); never the demo
    public const string Editor = "Editor";       // Approved or Suspended, not the demo – changes visitors must not make (letter, settings)
    public const string Admin = "Admin";
}

public sealed class AccessRequirement(params AccessStatus[] allowed) : IAuthorizationRequirement
{
    public IReadOnlyCollection<AccessStatus> Allowed { get; } = allowed;

    /// <summary>The shared demo account (ADR-0020) is refused – it must not spend AI tokens or change shared content.</summary>
    public bool DenyDemo { get; init; }
}

/// <summary>The status is read from the DB on every request – approving or suspending takes effect immediately.</summary>
public sealed class AccessHandler(IAppDbContext db) : AuthorizationHandler<AccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AccessRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(JobScoutClaims.UserId), out var userId))
            return;

        var user = await db.Users.Where(u => u.Id == userId).Select(u => new { u.Access, u.Provider }).SingleOrDefaultAsync();
        if (user is null || !requirement.Allowed.Contains(user.Access))
            return;
        if (requirement.DenyDemo && user.Provider == Application.Demo.DemoAccount.Provider)
            return;
        context.Succeed(requirement);
    }
}

/// <summary>Admins come from configuration, not from the DB – a compromised database can't make anybody an admin.</summary>
public sealed class AdminOptions : IAdminDirectory
{
    /// <summary>"provider:subject" – copy yours from GET /api/me ("identity"). In Azure: Key Vault / app settings.</summary>
    public List<string> Admins { get; init; } = [];

    public bool IsAdmin(string provider, string subject) => Admins.Contains($"{provider}:{subject}");
}

public sealed class AdminRequirement : IAuthorizationRequirement { }

public sealed class AdminHandler(IAppDbContext db, AdminOptions admins) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(JobScoutClaims.UserId), out var userId))
            return;

        var identity = await db.Users.Where(u => u.Id == userId)
            .Select(u => new { u.Provider, u.Subject })
            .SingleOrDefaultAsync();
        if (identity is not null && admins.IsAdmin(identity.Provider, identity.Subject))
            context.Succeed(requirement);
    }
}
