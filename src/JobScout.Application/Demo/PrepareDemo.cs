using System.Text;
using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Application.Scouting;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Demo;

public sealed record DemoPrepared(bool ProfileCreated, bool SearchStarted);

/// <summary>
/// Admin action "Připravit ukázkový účet": creates the demo user (approved), gives it the fictional CV + notes
/// (one AI analysis, only when they changed) and starts its first search. Afterwards the morning run keeps it fresh.
/// </summary>
public sealed class PrepareDemo(IAppDbContext db, AnalyzeProfile analyze, StartFirstScout startFirst, TimeProvider time)
{
    public async Task<DemoPrepared> ExecuteAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(
            u => u.Provider == DemoAccount.Provider && u.Subject == DemoAccount.Subject, ct);
        if (user is null)
        {
            user = new User(DemoAccount.Provider, DemoAccount.Subject, "", DemoAccount.DisplayName, now);
            user.Approve(now);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        var analyzed = await analyze.ExecuteAsync(new ProfileInput(
            DemoAccount.Provider, DemoAccount.Subject, DemoAccount.DisplayName, DemoAccount.CvFileName,
            new LlmDocument("CV", "text/plain", Encoding.UTF8.GetBytes(DemoAccount.Cv)), DemoAccount.Notes), ct);

        var started = await startFirst.ExecuteAsync(user.Id, ct);
        return new DemoPrepared(analyzed.IsNew, started);
    }
}
