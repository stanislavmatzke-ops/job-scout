using System.Linq.Expressions;
using JobScout.Core.Domain;

namespace JobScout.Application.Matches;

/// <summary>Tabs of the job list in the UI.</summary>
public enum MatchView { Open, InProgress, Closed, TriagedOut }

/// <summary>
/// Which tab a job belongs to. What the user did (status) wins over where the pipeline got (stage).
/// The same rule twice: in C# (counts) and as a database filter (the list) – MatchViewsTests keep them honest.
/// </summary>
public static class MatchViews
{
    public static MatchView? Of(MatchStage stage, MatchStatus status) => status switch
    {
        MatchStatus.Hidden => null,
        MatchStatus.Applied or MatchStatus.Call or MatchStatus.Round1 or MatchStatus.Round2 or MatchStatus.Offer
            => MatchView.InProgress,
        MatchStatus.Rejected or MatchStatus.Withdrawn or MatchStatus.Dismissed
            => MatchView.Closed,
        _ => stage switch       // New / SavedForLater – the user hasn't acted yet
        {
            MatchStage.Shortlisted or MatchStage.Evaluated => MatchView.Open,
            MatchStage.TriagedOut => MatchView.TriagedOut,
            _ => null           // Found – not triaged yet, only for a moment during the run
        }
    };

    /// <summary>Explicit comparisons (no arrays) – translate to plain SQL with enums stored as text.</summary>
    public static Expression<Func<JobMatch, bool>> Filter(MatchView view) => view switch
    {
        MatchView.Open => m => (m.Status == MatchStatus.New || m.Status == MatchStatus.SavedForLater)
                               && (m.Stage == MatchStage.Shortlisted || m.Stage == MatchStage.Evaluated),
        MatchView.InProgress => m => m.Status == MatchStatus.Applied || m.Status == MatchStatus.Call
                                     || m.Status == MatchStatus.Round1 || m.Status == MatchStatus.Round2
                                     || m.Status == MatchStatus.Offer,
        MatchView.Closed => m => m.Status == MatchStatus.Rejected || m.Status == MatchStatus.Withdrawn
                                 || m.Status == MatchStatus.Dismissed,
        MatchView.TriagedOut => m => (m.Status == MatchStatus.New || m.Status == MatchStatus.SavedForLater)
                                     && m.Stage == MatchStage.TriagedOut,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, null)
    };
}
