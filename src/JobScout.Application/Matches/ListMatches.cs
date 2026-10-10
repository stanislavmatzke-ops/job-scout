using JobScout.Application.Abstractions;
using JobScout.Application.Evaluation;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Matches;

public sealed record MatchListItem(
    Guid Id, string Title, string Company, string Location, string Url,
    MatchStage Stage, MatchStatus Status, int? Score, int? TriageScore, string? Summary, string? TriageReason,
    bool HasCoverLetter, DateTimeOffset FoundAt, DateTimeOffset UpdatedAt);

public sealed record MatchCounts(int Open, int InProgress, int Closed, int TriagedOut);

public sealed record MatchList(MatchView View, IReadOnlyList<MatchListItem> Items, MatchCounts Counts);

/// <summary>One tab of the user's jobs + counts for all tabs. Open = best first: evaluated by score, then waiting by relevance.</summary>
public sealed class ListMatches(IAppDbContext db)
{
    public async Task<MatchList> ExecuteAsync(Guid userId, MatchView view, CancellationToken ct = default)
    {
        var mine = db.JobMatches.AsNoTracking().Where(m => m.UserId == userId);

        var query = mine.Where(MatchViews.Filter(view));
        query = view == MatchView.Open
            ? query.OrderByDescending(m => m.Score != null)        // evaluated first…
                .ThenByDescending(m => m.Score)                     // …best first
                .ThenByDescending(m => m.TriageScore)               // waiting ones by triage relevance
                .ThenByDescending(m => m.CreatedAt)
            : query.OrderByDescending(m => m.UpdatedAt);            // other tabs: latest activity first

        // Only the columns the list needs (not the ad text or the letter)
        var rows = await query.Select(m => new
            {
                m.Id, m.JobOffer!.Title, m.JobOffer.Company, m.JobOffer.Location, m.JobOffer.Url,
                m.Stage, m.Status, m.Score, m.TriageScore, m.EvaluationJson, m.TriageReason,
                HasCoverLetter = m.CoverLetter != null, m.CreatedAt, m.UpdatedAt
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new MatchListItem(
                r.Id, r.Title, r.Company, r.Location, r.Url, r.Stage, r.Status, r.Score, r.TriageScore,
                JobEvaluationJson.TryParse(r.EvaluationJson)?.Summary, r.TriageReason, r.HasCoverLetter,
                r.CreatedAt, r.UpdatedAt))
            .ToList();

        // Tab counts: two small columns of all my jobs, sorted out in C# by the same rule
        var states = await mine.Select(m => new { m.Stage, m.Status }).ToListAsync(ct);
        var perView = states
            .Select(s => MatchViews.Of(s.Stage, s.Status))
            .OfType<MatchView>()
            .CountBy(v => v)
            .ToDictionary();
        int Count(MatchView v) => perView.GetValueOrDefault(v);

        return new MatchList(view, items,
            new MatchCounts(Count(MatchView.Open), Count(MatchView.InProgress), Count(MatchView.Closed), Count(MatchView.TriagedOut)));
    }
}
