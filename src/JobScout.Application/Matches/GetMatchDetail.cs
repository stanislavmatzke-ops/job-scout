using JobScout.Application.Abstractions;
using JobScout.Application.Evaluation;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Matches;

public sealed record MatchHistoryEntry(MatchStatus From, MatchStatus To, string? Note, DateTimeOffset At);

public sealed record MatchDetail(
    Guid Id, string Title, string Company, string Location, string Url, string Source,
    MatchStage Stage, MatchStatus Status, int? Score, int? TriageScore, string? TriageReason,
    JobEvaluation? Evaluation, string? CoverLetter, string? Notes,
    string? AdText, bool AdTextIncomplete, DateTimeOffset FoundAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<MatchHistoryEntry> History,
    bool EvaluationOutdated,                        // evaluated with an older profile version (6d)
    int LetterRewrites = 0);                        // new AI drafts over an existing letter – limited per job (ADR-0021)

/// <summary>Everything about one of my jobs. Somebody else's job = null, exactly like a missing one.</summary>
public sealed class GetMatchDetail(IAppDbContext db)
{
    public async Task<MatchDetail?> ExecuteAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        var m = await db.JobMatches.AsNoTracking()
            .Include(x => x.JobOffer)
            .Include(x => x.Events)
            .SingleOrDefaultAsync(x => x.Id == matchId && x.UserId == userId, ct);
        if (m is null)
            return null;

        var currentVersion = await db.CurrentProfileVersionAsync(userId, ct);
        var offer = m.JobOffer!;
        return new MatchDetail(
            m.Id, offer.Title, offer.Company, offer.Location, offer.Url, offer.Source,
            m.Stage, m.Status, m.Score, m.TriageScore, m.TriageReason,
            JobEvaluationJson.TryParse(m.EvaluationJson), m.CoverLetter, m.Notes,
            offer.DetailText, AdTextIncomplete: offer.DetailText is not null && !offer.DetailIsComplete,
            m.CreatedAt, m.UpdatedAt,
            m.Events.OrderBy(e => e.At).Select(e => new MatchHistoryEntry(e.From, e.To, e.Note, e.At)).ToList(),
            EvaluationOutdated: m.Stage == MatchStage.Evaluated && m.ProfileVersion < currentVersion,
            m.LetterRewrites);
    }
}
