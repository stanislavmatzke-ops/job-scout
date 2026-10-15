using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>User × job: pipeline stage, AI evaluation, user status and its history.</summary>
public class JobMatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private static JobMatch NewMatch() => new(Guid.NewGuid(), Guid.NewGuid(), profileVersion: 1, T0);

    [Fact]
    public void New_match_starts_as_found_and_new_without_history()
    {
        var match = NewMatch();

        Assert.NotEqual(Guid.Empty, match.Id);
        Assert.Equal(MatchStage.Found, match.Stage);
        Assert.Equal(MatchStatus.New, match.Status);
        Assert.Equal(1, match.ProfileVersion);
        Assert.Null(match.Score);
        Assert.Empty(match.Events);
        Assert.Equal(T0, match.CreatedAt);
        Assert.Equal(T0, match.UpdatedAt);
    }

    // ---------- Status + history ----------

    [Fact]
    public void Every_status_change_is_recorded_in_history()
    {
        var match = NewMatch();

        match.ChangeStatus(MatchStatus.Applied, "Poslal jsem CV a dopis", T0.AddHours(1));
        match.ChangeStatus(MatchStatus.Call, "Call s HR v pátek", T0.AddDays(2));

        Assert.Equal(MatchStatus.Call, match.Status);
        Assert.Collection(match.Events,
            e =>
            {
                Assert.Equal(MatchStatus.New, e.From);
                Assert.Equal(MatchStatus.Applied, e.To);
                Assert.Equal("Poslal jsem CV a dopis", e.Note);
                Assert.Equal(T0.AddHours(1), e.At);
                Assert.Equal(match.Id, e.JobMatchId);
            },
            e =>
            {
                Assert.Equal(MatchStatus.Applied, e.From);
                Assert.Equal(MatchStatus.Call, e.To);
            });
    }

    [Fact]
    public void Same_status_without_note_is_not_recorded()
    {
        var match = NewMatch();

        match.ChangeStatus(MatchStatus.New, null, T0.AddHours(1));
        match.ChangeStatus(MatchStatus.New, "   ", T0.AddHours(2));

        Assert.Empty(match.Events);
        Assert.Equal(T0, match.UpdatedAt);   // nothing happened → not "updated"
    }

    [Fact]
    public void Same_status_with_note_is_recorded_as_a_note()
    {
        // e.g. still "Applied", but "HR replied they'll call next week"
        var match = NewMatch();
        match.ChangeStatus(MatchStatus.Applied, null, T0.AddHours(1));

        match.ChangeStatus(MatchStatus.Applied, "HR se ozve příští týden", T0.AddDays(1));

        Assert.Equal(2, match.Events.Count);
        Assert.Equal("HR se ozve příští týden", match.Events[1].Note);
        Assert.Equal(MatchStatus.Applied, match.Events[1].From);
        Assert.Equal(MatchStatus.Applied, match.Events[1].To);
    }

    [Fact]
    public void Status_can_be_corrected_back()
    {
        // No strict state machine on purpose: a mis-click must be fixable, history keeps the truth
        var match = NewMatch();
        match.ChangeStatus(MatchStatus.Rejected, null, T0.AddHours(1));

        match.ChangeStatus(MatchStatus.Round1, "Omyl – pohovor pokračuje", T0.AddHours(2));

        Assert.Equal(MatchStatus.Round1, match.Status);
        Assert.Equal(2, match.Events.Count);
        Assert.Equal(T0.AddHours(2), match.UpdatedAt);
    }

    // ---------- Pipeline: triage and evaluation ----------

    [Fact]
    public void Triage_out_keeps_the_reason()
    {
        var match = NewMatch();

        match.MarkTriagedOut("Android vývoj, mimo profil", T0.AddMinutes(5));

        Assert.Equal(MatchStage.TriagedOut, match.Stage);
        Assert.Equal("Android vývoj, mimo profil", match.TriageReason);
        Assert.Equal(T0.AddMinutes(5), match.UpdatedAt);
    }

    [Fact]
    public void Evaluation_sets_stage_score_and_json()
    {
        var match = NewMatch();

        match.ApplyEvaluation(8, """{"score":8}""", profileVersion: 3, T0.AddMinutes(10));

        Assert.Equal(MatchStage.Evaluated, match.Stage);
        Assert.Equal(3, match.ProfileVersion);         // evaluated with profile v3 – found with v1 (6d: outdated evaluations)
        Assert.Equal(8, match.Score);
        Assert.Equal("""{"score":8}""", match.EvaluationJson);
        Assert.Equal(MatchStatus.New, match.Status);   // evaluation doesn't touch the user's status
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public void Evaluation_rejects_score_outside_1_to_10_and_changes_nothing(int score)
    {
        var match = NewMatch();

        Assert.Throws<ArgumentOutOfRangeException>(() => match.ApplyEvaluation(score, "{}", 2, T0.AddMinutes(10)));

        Assert.Equal(MatchStage.Found, match.Stage);
        Assert.Null(match.Score);
        Assert.Null(match.EvaluationJson);
    }

    [Fact]
    public void Cover_letter_and_notes_are_stored()
    {
        var match = NewMatch();

        match.SetCoverLetter("Dobrý den, …", T0.AddMinutes(1));
        match.UpdateNotes("Zeptat se na HO", T0.AddMinutes(2));

        Assert.Equal("Dobrý den, …", match.CoverLetter);
        Assert.Equal("Zeptat se na HO", match.Notes);
        Assert.Equal(T0.AddMinutes(2), match.UpdatedAt);
    }

    [Fact]
    public void Job_added_by_hand_waits_for_evaluation_without_triage()
    {
        var match = JobMatch.AddedByUser(Guid.NewGuid(), Guid.NewGuid(), profileVersion: 2, T0);

        Assert.True(match.AddedManually);
        Assert.Equal((MatchStage.Shortlisted, MatchStatus.New, (int?)null), (match.Stage, match.Status, match.TriageScore));
        Assert.False(new JobMatch(Guid.NewGuid(), Guid.NewGuid(), 1, T0).AddedManually);
    }
}
