using JobScout.Application.Admin;
using JobScout.Application.Notifications;
using JobScout.Application.Reports;
using JobScout.Application.Users;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Application.Notifications;

/// <summary>
/// 7f – who gets which e-mail: the admin hears about a request, the user about approval, rejection and suspension,
/// and every morning about new jobs (unless turned off). A failing mail service never breaks the action.
/// </summary>
public sealed class NotifierTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeEmailSender _mail = new();
    private readonly Notifier _notifier;

    public NotifierTests()
    {
        _notifier = new Notifier(_mail,
            new NotificationOptions { AppUrl = "https://jobscout.example", AdminAddresses = ["admin@example.com"] },
            NullLogger<Notifier>.Instance);
    }

    public void Dispose() => _database.Dispose();

    private Guid AddUser(string provider = "google", string email = "jana@example.com")
    {
        using var db = _database.CreateContext();
        var user = new User(provider, Guid.NewGuid().ToString(), email, "Jana", T0);
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task Asking_again_the_same_day_does_not_flood_the_admin()
    {
        var jana = AddUser();

        for (var i = 0; i < 5; i++)
        {
            await using var db = _database.CreateContext();
            await new RequestAccess(db, _time, _notifier).ExecuteAsync(jana, $"Hledám práci ({i}).");
        }
        Assert.Single(_mail.Sent);

        _time.Now = T0.AddHours(25);
        await using (var db = _database.CreateContext())
            await new RequestAccess(db, _time, _notifier).ExecuteAsync(jana, "Pořád hledám.");
        Assert.Equal(2, _mail.Sent.Count);                                  // a day later it's news again
    }

    [Fact]
    public async Task The_admin_hears_about_a_new_request()
    {
        var jana = AddUser();
        await using var db = _database.CreateContext();

        await new RequestAccess(db, _time, _notifier).ExecuteAsync(jana, "Hledám práci.");

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal("admin@example.com", mail.To);
        Assert.Contains("Hledám práci.", mail.Text);
        Assert.Contains("https://jobscout.example/admin", mail.Text);
    }

    [Fact]
    public async Task Approval_is_announced_once_in_czech_and_english()
    {
        var jana = AddUser();
        await using var db = _database.CreateContext();

        await new ApproveUser(db, _time, _notifier).ExecuteAsync(jana);
        await new ApproveUser(db, _time, _notifier).ExecuteAsync(jana);          // already approved – nothing new to say

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal("jana@example.com", mail.To);
        Assert.Contains("byl schválen", mail.Text);
        Assert.Contains("has been approved", mail.Text);
    }

    [Fact]
    public async Task Rejection_says_why()
    {
        var jana = AddUser();
        await using var db = _database.CreateContext();

        await new RejectUser(db, _time, _notifier).ExecuteAsync(jana, "Teď nové uživatele nepřijímáme.");

        Assert.Contains("Teď nové uživatele nepřijímáme.", Assert.Single(_mail.Sent).Text);
    }

    [Theory]
    [InlineData("dev", "")]
    [InlineData("demo", "")]
    [InlineData("google", "")]                      // no address from the provider
    public async Task Personas_the_demo_and_users_without_an_address_get_nothing(string provider, string email)
    {
        var id = AddUser(provider, email);
        await using var db = _database.CreateContext();

        await new ApproveUser(db, _time, _notifier).ExecuteAsync(id);

        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task A_failing_mail_service_does_not_undo_the_approval()
    {
        var jana = AddUser();
        _mail.Fail = true;
        await using var db = _database.CreateContext();

        Assert.True(await new ApproveUser(db, _time, _notifier).ExecuteAsync(jana));
        Assert.Equal(AccessStatus.Approved, db.Users.Single(u => u.Id == jana).Access);
    }

    // ---------- the morning e-mail ----------

    private static ReportJob Job(int score, string title) =>
        new(title, "Firma", "Brno", "https://www.jobs.cz/rpd/1/", score, 4, "Sedí.", [], [], [], null, null, false);

    private static MorningReport Report(IReadOnlyList<ReportJob> recommended, IReadOnlyList<ReportJob> other) =>
        new("jana", T0, new ReportStats(10, 3, 3, 1, 0.1m), recommended, other, [], []);

    private static User Jana(bool morningEmail = true)
    {
        var user = new User("google", "g-1", "jana@example.com", "Jana", T0);
        user.SetMorningEmail(morningEmail);
        return user;
    }

    [Fact]
    public async Task Morning_email_lists_the_new_jobs_best_first()
    {
        var sent = await _notifier.MorningAsync(Jana(), Report([Job(8, "Architekt")], [Job(5, "Tester"), Job(4, "Analytik")]));

        Assert.True(sent);
        var mail = Assert.Single(_mail.Sent);
        Assert.Contains("3 nové nabídky", mail.Subject);
        Assert.Contains("8/10 · Architekt", mail.Text);
        Assert.Contains("https://jobscout.example/matches", mail.Text);
    }

    [Fact]
    public async Task No_morning_email_when_nothing_is_new_or_it_is_turned_off()
    {
        Assert.False(await _notifier.MorningAsync(Jana(), Report([], [])));
        Assert.False(await _notifier.MorningAsync(Jana(morningEmail: false), Report([Job(9, "Architekt")], [])));
        Assert.Empty(_mail.Sent);
    }

    // ---------- a failed morning run ----------

    [Fact]
    public async Task The_admin_hears_about_a_failed_morning_run()
    {
        await _notifier.MorningRunFailedAsync(3, 3, "jobs.cz místo výsledků hledání vrátil jinou stránku");

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal("admin@example.com", mail.To);
        Assert.Contains("3 z 3", mail.Subject);
        Assert.Contains("jobs.cz místo výsledků hledání vrátil jinou stránku", mail.Text);
    }
}
