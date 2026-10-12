using JobScout.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Demo;

/// <summary>
/// Every morning before the search: the demo account forgets what yesterday's visitors clicked (statuses, notes),
/// so every visitor starts from the same clean state. The jobs and the AI's work stay (ADR-0020).
/// </summary>
public sealed class ResetDemo(IAppDbContext db)
{
    /// <returns>how many jobs were changed back</returns>
    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        var demoId = await db.Users
            .Where(u => u.Provider == DemoAccount.Provider && u.Subject == DemoAccount.Subject)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(ct);
        if (demoId is null)
            return 0;

        var matches = await db.JobMatches.Include(m => m.Events).Where(m => m.UserId == demoId).ToListAsync(ct);
        var reset = matches.Count(m => m.ResetVisitorChanges());
        await db.SaveChangesAsync(ct);
        return reset;
    }
}
