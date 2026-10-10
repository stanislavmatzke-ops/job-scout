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
public sealed class WriteCoverLetters(IAppDbContext db, CoverLetterWriter writer, TimeProvider time)
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
            var draft = await writer.WriteAsync(profile, match, ct);
            calls.Add(draft.Response);

            if (draft.Letter is null)
            {
                failed++;                                   // stays pending for the next run
                continue;
            }

            match.SetCoverLetter(draft.Letter, time.GetUtcNow());
            letters.Add(new WrittenLetter(match.JobOffer!, match, draft.Letter));
            await db.SaveChangesAsync(ct);
        }

        return new WriteCoverLettersResult(letters, failed, pending.Count - letters.Count, calls);
    }

}
