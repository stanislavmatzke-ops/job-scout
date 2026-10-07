using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>
/// Triage outcome: a found job either passes to evaluation (Shortlisted, with relevance 3–5 that orders the queue)
/// or is triaged out with a reason.
/// </summary>
public class JobMatchShortlistTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Shortlisted_job_keeps_relevance_and_waits_for_evaluation()
    {
        var match = new JobMatch(Guid.NewGuid(), Guid.NewGuid(), 2, T0);

        match.Shortlist(5, T0.AddMinutes(1));

        Assert.Equal(MatchStage.Shortlisted, match.Stage);
        Assert.Equal(5, match.TriageScore);
        Assert.Equal(T0.AddMinutes(1), match.UpdatedAt);
        Assert.Null(match.TriageReason);
        Assert.False(match.IsVisible);           // not shown until evaluated
        Assert.Equal(MatchStatus.New, match.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Relevance_outside_1_to_5_is_rejected(int relevance)
    {
        var match = new JobMatch(Guid.NewGuid(), Guid.NewGuid(), 2, T0);

        Assert.Throws<ArgumentOutOfRangeException>(() => match.Shortlist(relevance, T0));
    }

    [Fact]
    public void Triaged_out_job_keeps_the_reason_and_optionally_the_relevance()
    {
        var match = new JobMatch(Guid.NewGuid(), Guid.NewGuid(), 2, T0);

        match.MarkTriagedOut("Statik – jiná profese", T0, relevance: 1);

        Assert.Equal(MatchStage.TriagedOut, match.Stage);
        Assert.Equal("Statik – jiná profese", match.TriageReason);
        Assert.Equal(1, match.TriageScore);
        Assert.False(match.IsVisible);
    }

    [Fact]
    public void Triaged_out_without_relevance_has_none()
    {
        // e.g. "ad no longer available" – not a relevance judgement
        var match = new JobMatch(Guid.NewGuid(), Guid.NewGuid(), 2, T0);

        match.MarkTriagedOut("Inzerát už není dostupný.", T0);

        Assert.Null(match.TriageScore);
    }
}
