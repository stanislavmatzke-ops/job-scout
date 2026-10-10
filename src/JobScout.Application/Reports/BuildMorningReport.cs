using JobScout.Application.Abstractions;
using JobScout.Application.Evaluation;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Reports;

public sealed record ReportStats(int Found, int NewForUser, int Evaluated, int Letters, decimal CostUsd);

public sealed record ReportJob(
    string Title, string Company, string Location, string Url, int? Score, int? Relevance,
    string? Summary, IReadOnlyList<string> Reasons, IReadOnlyList<string> Risks, IReadOnlyList<string> Questions,
    string? Salary, string? CoverLetter, bool DetailIncomplete);

public sealed record MorningReport(
    string Subject, DateTimeOffset GeneratedAt, ReportStats Stats,
    IReadOnlyList<ReportJob> NewRecommended, IReadOnlyList<ReportJob> NewOther,
    IReadOnlyList<ReportJob> EarlierRecommended, IReadOnlyList<ReportJob> WaitingForUser);

/// <summary>
/// Data for the morning report: today's results (changed since the run started), earlier open recommendations
/// and jobs waiting for the user's decision. Only jobs the user hasn't acted on (New / SavedForLater).
/// </summary>
public sealed class BuildMorningReport(IAppDbContext db, TimeProvider time)
{
    public const int RecommendedScore = 7;

    public async Task<MorningReport> ExecuteAsync(
        Guid userId, string subject, DateTimeOffset since, ReportStats stats, CancellationToken ct = default)
    {
        var open = await db.JobMatches
            .Include(m => m.JobOffer)
            .Where(m => m.UserId == userId && (m.Status == MatchStatus.New || m.Status == MatchStatus.SavedForLater))
            .Where(m => m.Stage == MatchStage.Evaluated
                        || (m.Stage == MatchStage.Shortlisted && m.TriageScore < EvaluateJobs.MinAutoRelevance))
            .ToListAsync(ct);

        var evaluated = open.Where(m => m.Stage == MatchStage.Evaluated).OrderByDescending(m => m.Score).ToList();
        var today = evaluated.Where(m => m.UpdatedAt >= since).ToList();

        return new MorningReport(subject, time.GetUtcNow(), stats,
            NewRecommended: today.Where(m => m.Score >= RecommendedScore).Select(ToJob).ToList(),
            NewOther: today.Where(m => m.Score < RecommendedScore).Select(ToJob).ToList(),
            EarlierRecommended: evaluated.Where(m => m.UpdatedAt < since && m.Score >= RecommendedScore).Select(ToJob).ToList(),
            WaitingForUser: open.Where(m => m.Stage == MatchStage.Shortlisted)
                .OrderByDescending(m => m.CreatedAt).Select(ToJob).ToList());
    }

    private static ReportJob ToJob(JobMatch m)
    {
        var offer = m.JobOffer!;
        var e = JobEvaluationJson.TryParse(m.EvaluationJson);
        return new ReportJob(offer.Title, offer.Company, offer.Location, offer.Url, m.Score, m.TriageScore,
            e?.Summary, e?.Reasons ?? [], e?.Risks ?? [], e?.Questions ?? [], e?.Salary, m.CoverLetter,
            DetailIncomplete: offer.DetailText is not null && !offer.DetailIsComplete);
    }
}