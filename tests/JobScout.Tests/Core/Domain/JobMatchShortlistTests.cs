using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>Triage outcome: a found job either passes to evaluation (Shortlisted) or is triaged out with a reason.</summary>
public class JobMatchShortlistTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Shortlisted_job_waits_for_evaluation()
    {
        var match = new JobMatch(Guid.NewGuid(), Guid.NewGuid(), 2, T0);

        match.Shortlist(T0.AddMinutes(1));

        Assert.Equal(MatchStage.Shortlisted, match.Stage);
        Assert.Equal(T0.AddMinutes(1), match.UpdatedAt);
        Assert.Null(match.TriageReason);
        Assert.False(match.IsVisible);           // not shown until evaluated
        Assert.Equal(MatchStatus.New, match.Status);
    }

    [Fact]
    public void Triaged_out_job_keeps_the_reason()
    {
        var match = new JobMatch(Guid.NewGuid(), Guid.NewGuid(), 2, T0);

        match.MarkTriagedOut("Statik – jiná profese", T0);

        Assert.Equal(MatchStage.TriagedOut, match.Stage);
        Assert.Equal("Statik – jiná profese", match.TriageReason);
        Assert.False(match.IsVisible);
    }
}
