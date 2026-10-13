using JobScout.Application.Demo;
using JobScout.Application.Notifications;
using JobScout.Application.UserFeedback;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Application.UserFeedback;

/// <summary>"Zpětná vazba" (13 Oct): stored, e-mailed to the admins, a few a day per user.</summary>
public sealed class SendFeedbackTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeEmailSender _mail = new();

    public void Dispose() => _database.Dispose();

    private Guid AddUser(string provider = "google", string email = "jana@example.com", string name = "Jana")
    {
        using var db = _database.CreateContext();
        var user = new User(provider, Guid.NewGuid().ToString(), email, name, T0);
        user.Approve(T0);
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private async Task<SendFeedbackResult> SendAsync(Guid userId, string message = "Přidejte prosím prace.cz.",
        FeedbackKind kind = FeedbackKind.Idea, int? rating = 5, bool replyWanted = true)
    {
        await using var db = _database.CreateContext();
        var notifier = new Notifier(_mail,
            new NotificationOptions { AppUrl = "https://jobscout.example", AdminAddresses = ["admin@example.com"] },
            NullLogger<Notifier>.Instance);
        return await new SendFeedback(db, _time, notifier).ExecuteAsync(userId, kind, rating, message, replyWanted);
    }

    [Fact]
    public async Task Feedback_is_stored_and_the_admin_can_answer_by_reply()
    {
        var jana = AddUser();

        Assert.Equal(SendFeedbackResult.Sent, await SendAsync(jana));

        await using var db = _database.CreateContext();
        var stored = Assert.Single(db.Feedback);
        Assert.Equal((jana, "Jana", FeedbackKind.Idea, (int?)5, "Přidejte prosím prace.cz.", true),
            (stored.UserId, stored.UserName, stored.Kind, stored.Rating, stored.Message, stored.ReplyWanted));

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal(("admin@example.com", "jana@example.com"), (mail.To, mail.ReplyTo));
        Assert.Contains("Jana", mail.Subject);
        Assert.Contains("Přidejte prosím prace.cz.", mail.Text);
        Assert.Contains("★★★★★", mail.Text);
    }

    [Fact]
    public async Task Without_a_wish_for_an_answer_reply_goes_nowhere_special()
    {
        var jana = AddUser();

        await SendAsync(jana, replyWanted: false, rating: null);

        Assert.Null(Assert.Single(_mail.Sent).ReplyTo);
    }

    [Fact]
    public async Task Five_messages_a_day_then_tomorrow()
    {
        var jana = AddUser();
        for (var i = 0; i < SendFeedback.PerUserPer24h; i++)
            Assert.Equal(SendFeedbackResult.Sent, await SendAsync(jana, $"Zpráva {i}"));

        Assert.Equal(SendFeedbackResult.TooMany, await SendAsync(jana, "Ještě jedna"));
        Assert.Equal(SendFeedbackResult.Sent, await SendAsync(AddUser(email: "petr@example.com", name: "Petr")));   // others are not affected

        _time.Now = T0.AddHours(24).AddMinutes(1);
        Assert.Equal(SendFeedbackResult.Sent, await SendAsync(jana, "Další den"));
    }

    [Fact]
    public async Task The_demo_account_can_write_but_nobody_can_answer_it()
    {
        var demo = AddUser(provider: DemoAccount.Provider, email: "", name: "Honza Testovací");

        Assert.Equal(SendFeedbackResult.Sent, await SendAsync(demo, replyWanted: true));

        await using var db = _database.CreateContext();
        Assert.False(Assert.Single(db.Feedback).ReplyWanted);
        Assert.Null(Assert.Single(_mail.Sent).ReplyTo);
    }

    [Fact]
    public async Task Empty_feedback_is_refused_and_nothing_is_sent()
    {
        var jana = AddUser();

        await Assert.ThrowsAsync<ArgumentException>(() => SendAsync(jana, "   "));

        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task The_admin_lists_the_newest_first_with_the_e_mail()
    {
        var jana = AddUser();
        await SendAsync(jana, "První");
        _time.Now = T0.AddMinutes(5);
        await SendAsync(jana, "Druhá", FeedbackKind.Problem);

        await using var db = _database.CreateContext();
        var rows = await new ListFeedback(db).ExecuteAsync();

        Assert.Equal(["Druhá", "První"], rows.Select(r => r.Message));
        Assert.Equal(("jana@example.com", FeedbackKind.Problem), (rows[0].Email, rows[0].Kind));
    }
}
