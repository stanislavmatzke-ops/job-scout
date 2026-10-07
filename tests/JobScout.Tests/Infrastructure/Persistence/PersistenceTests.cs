using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Infrastructure.Persistence;

/// <summary>
/// Integration tests: domain model ↔ EF Core mapping ↔ real SQLite (in memory), created by our migrations.
/// They guard the three EF pitfalls from phase 3b and the unique keys the whole app relies on.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static User NewUser(string subject = "subject-1") => new("google", subject, "a@example.com", "A", T0);

    private static JobOffer NewOffer(string externalId = "2001000001", DateTimeOffset? seenAt = null) =>
        JobOffer.FromListing(new JobListing("jobs.cz", externalId, $"Nabídka {externalId}", "Ukázková firma",
            "Praha", $"https://www.jobs.cz/rpd/{externalId}/", ["65 000 – 80 000 Kč", "Práce převážně z domova"]),
            seenAt ?? T0);

    /// <summary>Saves a user, an offer and a match between them; returns the IDs.</summary>
    private (Guid UserId, Guid OfferId, Guid MatchId) SeedMatch()
    {
        using var db = _db.CreateContext();
        var user = NewUser();
        var offer = NewOffer();
        var match = new JobMatch(user.Id, offer.Id, profileVersion: 1, T0);
        db.AddRange(user, offer, match);
        db.SaveChanges();
        return (user.Id, offer.Id, match.Id);
    }

    // ---------- Migrations ----------

    [Fact]
    public void Migrations_match_the_model()
    {
        // Fails when someone changes an entity/configuration and forgets "dotnet ef migrations add"
        using var db = _db.CreateContext();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    // ---------- Pitfall #1: our own Guid v7 keys + history added later ----------

    [Fact]
    public void Status_change_on_saved_match_adds_history_rows()
    {
        // Without ValueGeneratedNever() EF thinks the new event already exists → UPDATE → concurrency exception
        var (_, _, matchId) = SeedMatch();

        using (var db = _db.CreateContext())   // new "request": load, change, save
        {
            var match = db.JobMatches.Include(m => m.Events).Single(m => m.Id == matchId);
            match.ChangeStatus(MatchStatus.Applied, "Poslal jsem CV", T0.AddHours(1));
            match.ChangeStatus(MatchStatus.Call, "Call v pátek", T0.AddDays(1));
            db.SaveChanges();
        }

        using (var db = _db.CreateContext())
        {
            var match = db.JobMatches.Include(m => m.Events).Single(m => m.Id == matchId);
            var history = match.Events.OrderBy(e => e.At).ToList();

            Assert.Equal(MatchStatus.Call, match.Status);
            Assert.Equal(2, history.Count);
            Assert.Equal((MatchStatus.New, MatchStatus.Applied, "Poslal jsem CV"), (history[0].From, history[0].To, history[0].Note));
            Assert.Equal((MatchStatus.Applied, MatchStatus.Call), (history[1].From, history[1].To));
        }
    }

    // ---------- Pitfall #2: ordering by DateTimeOffset in SQLite ----------

    [Fact]
    public void Offers_can_be_ordered_by_time()
    {
        // Without the DateTimeOffset → number converter, SQLite can't translate this ORDER BY
        using (var db = _db.CreateContext())
        {
            db.AddRange(
                NewOffer("2001000001", T0.AddDays(-2)),
                NewOffer("2001000002", T0),
                NewOffer("2001000003", T0.AddDays(-1)));
            db.SaveChanges();
        }

        using (var db = _db.CreateContext())
        {
            var newestFirst = db.JobOffers.OrderByDescending(o => o.LastSeenAt).Select(o => o.ExternalId).ToList();
            var lastTwoDays = db.JobOffers.Count(o => o.LastSeenAt >= T0.AddDays(-1));

            Assert.Equal(new[] { "2001000002", "2001000003", "2001000001" }, newestFirst);
            Assert.Equal(2, lastTwoDays);
        }
    }

    [Fact]
    public void Time_with_offset_survives_the_roundtrip()
    {
        var pragueMorning = new DateTimeOffset(2026, 10, 6, 7, 45, 0, TimeSpan.FromHours(2));
        using (var db = _db.CreateContext())
        {
            db.Add(NewOffer("2001000001", pragueMorning));
            db.SaveChanges();
        }

        using (var db = _db.CreateContext())
        {
            var offer = db.JobOffers.Single();

            Assert.Equal(pragueMorning, offer.FirstSeenAt);
            Assert.Equal(TimeSpan.FromHours(2), offer.FirstSeenAt.Offset);
        }
    }

    // ---------- Mapping: enums as text, tags as JSON ----------

    [Fact]
    public void Statuses_are_stored_as_readable_text()
    {
        var (_, _, matchId) = SeedMatch();
        using (var db = _db.CreateContext())
        {
            var match = db.JobMatches.Include(m => m.Events).Single(m => m.Id == matchId);
            match.ApplyEvaluation(8, "{}", T0);
            match.ChangeStatus(MatchStatus.SavedForLater, null, T0);
            db.SaveChanges();
        }

        Assert.Equal("Evaluated", _db.Scalar("SELECT Stage FROM JobMatches"));
        Assert.Equal("SavedForLater", _db.Scalar("SELECT Status FROM JobMatches"));
        Assert.Equal("SavedForLater", _db.Scalar("SELECT \"To\" FROM JobMatchEvents"));
    }

    [Fact]
    public void Tags_roundtrip_through_json_column()
    {
        using (var db = _db.CreateContext())
        {
            db.Add(NewOffer());
            db.SaveChanges();
        }

        using (var db = _db.CreateContext())
        {
            Assert.Equal(new[] { "65 000 – 80 000 Kč", "Práce převážně z domova" }, db.JobOffers.Single().Tags);
        }

        Assert.StartsWith("[", (string)_db.Scalar("SELECT Tags FROM JobOffers")!);   // stored as a JSON array
    }

    // ---------- Unique keys the app relies on ----------

    [Fact]
    public void Same_ad_from_same_source_cannot_be_stored_twice()
    {
        using var db = _db.CreateContext();
        db.AddRange(NewOffer("2001000001"), NewOffer("2001000001"));

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Same_job_cannot_be_offered_to_the_same_user_twice()
    {
        var (userId, offerId, _) = SeedMatch();
        using var db = _db.CreateContext();
        db.Add(new JobMatch(userId, offerId, profileVersion: 2, T0.AddDays(1)));   // e.g. the ad was "refreshed"

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Same_sso_identity_cannot_create_two_users()
    {
        using var db = _db.CreateContext();
        db.AddRange(NewUser("same-subject"), NewUser("same-subject"));

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Profile_versions_are_unique_per_user_and_latest_is_found()
    {
        var user = NewUser();
        using (var db = _db.CreateContext())
        {
            db.Add(user);
            db.AddRange(
                new UserProfile(user.Id, 1, "cv.pdf", null, "HASH1", "{}", "claude-sonnet-5-5", T0),
                new UserProfile(user.Id, 2, "cv.pdf", "HO", "HASH2", "{}", "claude-sonnet-5-5", T0.AddDays(1)));
            db.SaveChanges();
        }

        using (var db = _db.CreateContext())
        {
            var latest = db.UserProfiles.Where(p => p.UserId == user.Id).OrderByDescending(p => p.Version).First();
            Assert.Equal("HASH2", latest.InputHash);

            db.Add(new UserProfile(user.Id, 2, "cv.pdf", null, "HASH3", "{}", "x", T0.AddDays(2)));
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
    }

    // ---------- Deleting (GDPR "delete my account") ----------

    [Fact]
    public void Deleting_a_user_removes_their_data_but_keeps_shared_offers()
    {
        var (userId, _, matchId) = SeedMatch();
        using (var db = _db.CreateContext())
        {
            var match = db.JobMatches.Include(m => m.Events).Single(m => m.Id == matchId);
            match.ChangeStatus(MatchStatus.Applied, null, T0);
            db.Add(new UserProfile(userId, 1, "cv.pdf", null, "HASH", "{}", "m", T0));
            db.SaveChanges();
        }

        using (var db = _db.CreateContext())
        {
            db.Users.Remove(db.Users.Single(u => u.Id == userId));
            db.SaveChanges();
        }

        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM UserProfiles"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM JobMatches"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM JobMatchEvents"));
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM JobOffers"));   // shared – other users may have it
    }

    [Fact]
    public void Offer_still_in_someones_list_cannot_be_deleted()
    {
        var (_, offerId, _) = SeedMatch();
        using var db = _db.CreateContext();
        db.JobOffers.Remove(db.JobOffers.Single(o => o.Id == offerId));

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    // ---------- Learned catalogue (ADR-0015) ----------

    [Fact]
    public void Source_category_is_unique_per_source_and_key()
    {
        using (var db = _db.CreateContext())
        {
            db.SourceCategories.Add(new SourceCategory("jobs.cz", "Lakýrník", "lakyrnik", T0));
            db.SaveChanges();
        }

        using var db2 = _db.CreateContext();
        db2.SourceCategories.Add(new SourceCategory("jobs.cz", "lakýrník", "lakyrnik", T0));   // same key, other spelling

        Assert.Throws<DbUpdateException>(() => db2.SaveChanges());
    }

    [Fact]
    public void Same_key_on_another_source_is_allowed_and_negative_answer_roundtrips()
    {
        using (var db = _db.CreateContext())
        {
            db.SourceCategories.Add(new SourceCategory("jobs.cz", "Autolakýrník", null, T0));
            db.SourceCategories.Add(new SourceCategory("prace.cz", "Autolakýrník", "autolakyrnik", T0));
            db.SaveChanges();
        }

        using var read = _db.CreateContext();
        var jobsCz = read.SourceCategories.Single(c => c.Source == "jobs.cz");

        Assert.False(jobsCz.Exists);
        Assert.Null(jobsCz.CategoryId);
        Assert.Equal(T0, jobsCz.CheckedAt);
        Assert.Equal(2, read.SourceCategories.Count());
    }
}
