using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Infrastructure.Persistence;

namespace JobScout.Tests.TestUtils;

/// <summary>Puts one job (shared offer + the user's match) into the database in the state a test needs.</summary>
internal static class MatchSeeder
{
    private static int _next;

    public static Guid Add(
        JobScoutDbContext db, Guid userId, string title, DateTimeOffset at,
        MatchStage stage = MatchStage.Evaluated, int score = 7, int triage = 4,
        MatchStatus status = MatchStatus.New, string? letter = null, string? evaluationJson = null,
        string? triageReason = null, string? adText = null, bool adTextComplete = true, string company = "Firma s.r.o.",
        int profileVersion = 1)
    {
        var externalId = $"9{Interlocked.Increment(ref _next):D9}";
        var url = $"https://www.jobs.cz/rpd/{externalId}/";
        var offer = JobOffer.FromListing(new JobListing("jobs.cz", externalId, title, company, "Praha", url, []), at);
        if (adText is not null)
            offer.SetDetail(new JobDetail("jobs.cz", externalId, url, title, company, adText, adTextComplete), at);

        var match = new JobMatch(userId, offer.Id, profileVersion, at);
        switch (stage)
        {
            case MatchStage.Shortlisted:
                match.Shortlist(triage, at);
                break;
            case MatchStage.TriagedOut:
                match.MarkTriagedOut(triageReason ?? "Jiný obor", at, relevance: 1);
                break;
            case MatchStage.Evaluated:
                match.Shortlist(triage, at);
                match.ApplyEvaluation(score, evaluationJson ?? EvaluationJson(score), profileVersion, at);
                break;
        }

        if (letter is not null)
            match.SetCoverLetter(letter, at);
        if (status != MatchStatus.New)
            match.ChangeStatus(status, null, at);

        db.AddRange(offer, match);
        db.SaveChanges();
        return match.Id;
    }

    /// <summary>The JSON the evaluation stores (same shape as JobEvaluation).</summary>
    public static string EvaluationJson(int score, string summary = "Shrnutí hodnocení.") => $$"""
        { "score": {{score}}, "summary": "{{summary}}", "reasons": ["Sedí role"], "risks": ["Mzda neuvedena"],
          "questions": ["Jaký je podíl home office?"], "salary": "", "remoteWork": "hybrid", "employmentType": "employee" }
        """;
}
