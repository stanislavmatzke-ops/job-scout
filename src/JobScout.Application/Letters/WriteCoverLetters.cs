using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Letters;

public sealed record WrittenLetter(JobOffer Offer, JobMatch Match, string Letter);
public sealed record WriteCoverLettersResult(
    IReadOnlyList<WrittenLetter> Letters, int Failed, int Remaining, IReadOnlyList<LlmResponse> Calls);

/// <summary>
/// Cover letter drafts for evaluated jobs with score ≥ 7, best first, at most maxPerRun per run.
/// Never overwrites an existing letter (the user may have edited it). Nothing is ever sent.
/// </summary>
public sealed class WriteCoverLetters(IAppDbContext db, ILlmClient llm, TimeProvider time)
{
    public const int MinScore = 7;
    public const int MaxPerRun = 5;

    public async Task<WriteCoverLettersResult> ExecuteAsync(
        Guid userId, CandidateProfile profile, int maxPerRun = MaxPerRun, CancellationToken ct = default)
    {
        var pending = await db.JobMatches
            .Include(m => m.JobOffer)
            .Where(m => m.UserId == userId
                        && m.Stage == MatchStage.Evaluated
                        && m.Score >= MinScore
                        && m.CoverLetter == null
                        && m.Status != MatchStatus.Hidden
                        && m.Status != MatchStatus.Dismissed)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var letters = new List<WrittenLetter>();
        var calls = new List<LlmResponse>();
        var failed = 0;

        foreach (var match in pending.Take(maxPerRun))
        {
            var response = await llm.CompleteAsync(BuildRequest(profile, match), ct);   // API outage still throws
            calls.Add(response);

            // Content problem (cut off / empty) → skip this one, it stays pending for the next run
            var letter = response.Text.Trim();
            if (response.StopReason == "max_tokens" || letter.Length == 0)
            {
                failed++;
                continue;
            }

            match.SetCoverLetter(letter, time.GetUtcNow());
            letters.Add(new WrittenLetter(match.JobOffer!, match, letter));
            await db.SaveChangesAsync(ct);
        }

        return new WriteCoverLettersResult(letters, failed, pending.Count - letters.Count, calls);
    }

    private static LlmRequest BuildRequest(CandidateProfile profile, JobMatch match)
    {
        var offer = match.JobOffer!;
        return new LlmRequest(
            CoverLetterPrompts.System,
            $"""
            <profil_kandidata>
            {LlmJson.Serialize(profile)}
            </profil_kandidata>

            <hodnoceni>
            {match.EvaluationJson}
            </hodnoceni>

            <nabidka>
            Pozice: {offer.Title}
            Firma: {offer.Company}
            Místo: {offer.Location}

            {offer.DetailText}
            </nabidka>

            Napiš návrh průvodního dopisu.
            """)
        {
            MaxTokens = 2000,
            Tier = LlmTier.Smart,
            Purpose = LlmPurpose.CoverLetter
        };
    }
}