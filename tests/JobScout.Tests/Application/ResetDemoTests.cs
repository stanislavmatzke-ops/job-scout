using JobScout.Application.Demo;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application;

/// <summary>ADR-0020 – the demo account starts every morning clean: visitors' statuses, history and notes go away, the AI's work stays.</summary>
public sealed class ResetDemoTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private Guid AddUser(string provider, string subject)
    {
        using var db = _database.CreateContext();
        var user = new User(provider, subject, "", subject, T0);
        user.Approve(T0);
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task Visitors_changes_go_away_the_ai_work_stays()
    {
        var demo = AddUser(DemoAccount.Provider, DemoAccount.Subject);
        var real = AddUser("google", "jana");
        Guid demoJob, realJob;
        await using (var db = _database.CreateContext())
        {
            demoJob = MatchSeeder.Add(db, demo, ".NET vývojář", T0, letter: "Dobrý den, …");
            realJob = MatchSeeder.Add(db, real, "Architekt", T0);
            foreach (var m in db.JobMatches.ToList())
            {
                m.ChangeStatus(MatchStatus.Applied, "odesláno", T0);
                m.UpdateNotes("Návštěvník tu něco napsal", T0);
            }
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
            Assert.Equal(1, await new ResetDemo(db).ExecuteAsync());

        await using var check = _database.CreateContext();
        var d = check.JobMatches.Include(m => m.Events).Single(m => m.Id == demoJob);
        Assert.Equal((MatchStatus.New, (string?)null, 0), (d.Status, d.Notes, d.Events.Count));
        Assert.Equal("Dobrý den, …", d.CoverLetter);                                  // the AI's letter stays
        Assert.Equal(MatchStatus.Applied, check.JobMatches.Single(m => m.Id == realJob).Status);   // real users untouched
    }

    [Fact]
    public async Task No_demo_account_nothing_to_do()
    {
        await using var db = _database.CreateContext();
        Assert.Equal(0, await new ResetDemo(db).ExecuteAsync());
    }
}
