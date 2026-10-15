using JobScout.Application.Matches;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Matches;

/// <summary>What the user does with a job: status with a note (always recorded in history) and free notes.</summary>
public sealed class ChangeMatchTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0.AddDays(1));
    private readonly Guid _userId;
    private readonly Guid _otherUserId;
    private readonly Guid _matchId;

    public ChangeMatchTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "private", "", "private", T0);
        var other = new User("dev", "zednik", "", "zednik", T0);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
        _matchId = MatchSeeder.Add(db, _userId, "Solution Architect", T0);
    }

    public void Dispose() => _database.Dispose();

    private async Task<bool> ChangeStatusAsync(MatchStatus status, string? note = null, Guid? asUser = null)
    {
        await using var db = _database.CreateContext();
        return await new ChangeMatchStatus(db, _time).ExecuteAsync(asUser ?? _userId, _matchId, status, note);
    }

    private async Task<bool> UpdateNotesAsync(string? notes, Guid? asUser = null)
    {
        await using var db = _database.CreateContext();
        return await new UpdateMatchNotes(db, _time).ExecuteAsync(asUser ?? _userId, _matchId, notes);
    }

    private JobMatch Stored()
    {
        using var db = _database.CreateContext();
        return db.JobMatches.Include(m => m.Events).Single(m => m.Id == _matchId);
    }

    // ---------- status ----------

    [Fact]
    public async Task Status_change_is_saved_with_history()
    {
        var found = await ChangeStatusAsync(MatchStatus.Applied, "Odesláno přes jobs.cz");

        Assert.True(found);
        var match = Stored();
        Assert.Equal(MatchStatus.Applied, match.Status);
        Assert.Equal(_time.Now, match.UpdatedAt);
        var entry = Assert.Single(match.Events);
        Assert.Equal((MatchStatus.New, MatchStatus.Applied, "Odesláno přes jobs.cz"), (entry.From, entry.To, entry.Note));
    }

    [Fact]
    public async Task Blank_note_is_stored_as_no_note()
    {
        await ChangeStatusAsync(MatchStatus.Dismissed, "   ");

        Assert.Null(Assert.Single(Stored().Events).Note);
    }

    [Fact]
    public async Task Somebody_else_cannot_change_my_job()
    {
        var found = await ChangeStatusAsync(MatchStatus.Hidden, asUser: _otherUserId);

        Assert.False(found);
        Assert.Equal(MatchStatus.New, Stored().Status);
        Assert.Empty(Stored().Events);
    }

    [Fact]
    public async Task Too_long_note_is_rejected()
    {
        var note = new string('x', ChangeMatchStatus.MaxNoteLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => ChangeStatusAsync(MatchStatus.Applied, note));
        Assert.Equal(MatchStatus.New, Stored().Status);
    }

    [Fact]
    public async Task Undefined_status_value_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChangeStatusAsync((MatchStatus)99));
    }

    // ---------- bulk ----------

    private async Task<int> ChangeManyAsync(IReadOnlyCollection<Guid> ids, MatchStatus status, string? note = null, Guid? asUser = null)
    {
        await using var db = _database.CreateContext();
        return await new ChangeMatchStatus(db, _time).ExecuteManyAsync(asUser ?? _userId, ids, status, note);
    }

    private Guid SeedAnother(Guid userId, string title)
    {
        using var db = _database.CreateContext();
        return MatchSeeder.Add(db, userId, title, T0);
    }

    [Fact]
    public async Task Bulk_change_records_history_for_every_job()
    {
        var second = SeedAnother(_userId, "Tech Lead");

        var changed = await ChangeManyAsync([_matchId, second], MatchStatus.Rejected, "Odmítnuto e-mailem");

        Assert.Equal(2, changed);
        using var db = _database.CreateContext();
        var stored = db.JobMatches.Include(m => m.Events).Where(m => m.UserId == _userId).ToList();
        Assert.All(stored, m =>
        {
            Assert.Equal(MatchStatus.Rejected, m.Status);
            var entry = Assert.Single(m.Events);
            Assert.Equal((MatchStatus.New, MatchStatus.Rejected, "Odmítnuto e-mailem"), (entry.From, entry.To, entry.Note));
        });
    }

    [Fact]
    public async Task Bulk_change_skips_somebody_elses_and_unknown_jobs()
    {
        var foreign = SeedAnother(_otherUserId, "Zedník");

        var changed = await ChangeManyAsync([_matchId, foreign, Guid.NewGuid()], MatchStatus.Dismissed);

        Assert.Equal(1, changed);
        Assert.Equal(MatchStatus.Dismissed, Stored().Status);
        using var db = _database.CreateContext();
        Assert.Equal(MatchStatus.New, db.JobMatches.Single(m => m.Id == foreign).Status);
    }

    [Fact]
    public async Task Bulk_change_with_the_same_id_twice_records_it_once()
    {
        await ChangeManyAsync([_matchId, _matchId], MatchStatus.Applied);

        Assert.Single(Stored().Events);
    }

    [Fact]
    public async Task Bulk_change_needs_a_selection_within_the_limit()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ChangeManyAsync([], MatchStatus.Rejected));
        var tooMany = Enumerable.Range(0, ChangeMatchStatus.MaxBulk + 1).Select(_ => Guid.NewGuid()).ToList();
        await Assert.ThrowsAsync<ArgumentException>(() => ChangeManyAsync(tooMany, MatchStatus.Rejected));
        Assert.Equal(MatchStatus.New, Stored().Status);
    }

    // ---------- notes ----------

    [Fact]
    public async Task Notes_are_saved_without_touching_the_history()
    {
        var found = await UpdateNotesAsync("Zeptat se na podíl home office");

        Assert.True(found);
        var match = Stored();
        Assert.Equal("Zeptat se na podíl home office", match.Notes);
        Assert.Empty(match.Events);
    }

    [Fact]
    public async Task Empty_notes_clear_them()
    {
        await UpdateNotesAsync("Něco");
        await UpdateNotesAsync("  ");

        Assert.Null(Stored().Notes);
    }

    [Fact]
    public async Task Somebody_else_cannot_write_my_notes()
    {
        Assert.False(await UpdateNotesAsync("Cizí poznámka", asUser: _otherUserId));
        Assert.Null(Stored().Notes);
    }

    [Fact]
    public async Task Too_long_notes_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => UpdateNotesAsync(new string('x', UpdateMatchNotes.MaxLength + 1)));
    }
}
