using JobScout.Application.Matches;
using JobScout.Core.Domain;

namespace JobScout.Tests.Application.Matches;

/// <summary>
/// Which tab of the UI a job belongs to. What the user did (status) wins over where the pipeline got (stage):
/// a job you applied to is "in progress" even if triage once dropped it. Hidden is never listed.
/// </summary>
public class MatchViewsTests
{
    [Theory]
    // open = waiting for the user's decision
    [InlineData(MatchStage.Evaluated, MatchStatus.New, MatchView.Open)]
    [InlineData(MatchStage.Evaluated, MatchStatus.SavedForLater, MatchView.Open)]
    [InlineData(MatchStage.Shortlisted, MatchStatus.New, MatchView.Open)]          // passed triage, not evaluated yet
    // the user acted
    [InlineData(MatchStage.Evaluated, MatchStatus.Applied, MatchView.InProgress)]
    [InlineData(MatchStage.Evaluated, MatchStatus.Round2, MatchView.InProgress)]
    [InlineData(MatchStage.Evaluated, MatchStatus.Offer, MatchView.InProgress)]
    [InlineData(MatchStage.TriagedOut, MatchStatus.Applied, MatchView.InProgress)] // status wins over stage
    [InlineData(MatchStage.Evaluated, MatchStatus.Rejected, MatchView.Closed)]
    [InlineData(MatchStage.Evaluated, MatchStatus.Withdrawn, MatchView.Closed)]
    [InlineData(MatchStage.Evaluated, MatchStatus.Dismissed, MatchView.Closed)]    // "Zahozená" stays in the archive
    // the safety net for triage
    [InlineData(MatchStage.TriagedOut, MatchStatus.New, MatchView.TriagedOut)]
    // never listed
    [InlineData(MatchStage.Evaluated, MatchStatus.Hidden, null)]                   // "Nechci vidět"
    [InlineData(MatchStage.TriagedOut, MatchStatus.Hidden, null)]
    [InlineData(MatchStage.Found, MatchStatus.New, null)]                          // not triaged yet – only for a moment
    public void Job_belongs_to_one_view(MatchStage stage, MatchStatus status, MatchView? expected)
    {
        Assert.Equal(expected, MatchViews.Of(stage, status));
    }

    [Fact]
    public void Every_status_is_covered()
    {
        // A new status added to the enum must be put into a view on purpose – this test reminds you
        foreach (var status in Enum.GetValues<MatchStatus>().Where(s => s != MatchStatus.Hidden))
            Assert.NotNull(MatchViews.Of(MatchStage.Evaluated, status));
    }
}
