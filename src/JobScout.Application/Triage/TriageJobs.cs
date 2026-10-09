using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Triage;

public sealed record TriageDecision(string Id, int Relevance, string Reason);
public sealed record TriageDecisions(IReadOnlyList<TriageDecision> Decisions);

public sealed record TriagedJob(JobOffer Offer, bool Keep, int Relevance, string Reason);

public sealed record TriageResult(IReadOnlyList<TriagedJob> Jobs, IReadOnlyList<LlmResponse> Calls)
{
    public int Kept => Jobs.Count(j => j.Keep);
    public int Dropped => Jobs.Count(j => !j.Keep);
}

/// <summary>
/// Cheap first sieve (Fast model) over every job waiting in stage Found: titles only, one call per batch.
/// Clear misfits → TriagedOut with a reason; everything else, including anything unclear → Shortlisted.
/// </summary>
public sealed class TriageJobs(IAppDbContext db, ILlmClient llm, TimeProvider time)
{
    public const int BatchSize = 40;
    /// <summary>Only clear nonsense (relevance 1) is triaged out – everything that theoretically makes sense passes (8 Oct).</summary>
    public const int DropAtOrBelow = 1;
    /// <summary>Missing, invalid or reasonless decision = "unsure".</summary>
    public const int DefaultRelevance = 3;
    public async Task<TriageResult> ExecuteAsync(Guid userId, CandidateProfile profile, CancellationToken ct = default)
    {
        var pending = await db.JobMatches
            .Include(m => m.JobOffer)
            .Where(m => m.UserId == userId && m.Stage == MatchStage.Found)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var jobs = new List<TriagedJob>();
        var calls = new List<LlmResponse>();

        foreach (var batch in pending.Chunk(BatchSize))
        {
            var response = await llm.CompleteAsync(BuildRequest(profile, batch), ct);
            calls.Add(response);

            var decisions = LlmJson.Parse<TriageDecisions>(response).Decisions
                .GroupBy(d => d.Id.Trim())
                .ToDictionary(g => g.Key, g => g.First());

            var now = time.GetUtcNow();
            for (var i = 0; i < batch.Length; i++)
            {
                var match = batch[i];
                decisions.TryGetValue((i + 1).ToString(), out var d);
                var reason = d?.Reason?.Trim() ?? "";

                // Drop only a valid low score WITH a reason; anything missing or odd = unsure (3) – never trust a silent drop
                if (d is { Relevance: >= 1 and <= DropAtOrBelow } && reason.Length > 0)
                {
                    match.MarkTriagedOut(reason, now, d.Relevance);
                    jobs.Add(new TriagedJob(match.JobOffer!, Keep: false, d.Relevance, reason));
                }
                else
                {
                    var relevance = d is { Relevance: > DropAtOrBelow and <= 5 } ? d.Relevance : DefaultRelevance;
                    match.Shortlist(relevance, now);
                    jobs.Add(new TriagedJob(match.JobOffer!, Keep: true, relevance, ""));
                }
            }

            await db.SaveChangesAsync(ct);   // per batch – progress survives a failure in a later batch
        }

        return new TriageResult(jobs, calls);
    }

    private static LlmRequest BuildRequest(CandidateProfile profile, JobMatch[] batch)
    {
        // Short numeric ids: cheaper and safer than asking the model to copy GUIDs
        var lines = batch.Select((m, i) =>
            $"{i + 1}. {m.JobOffer!.Title} | {m.JobOffer.Company} | {m.JobOffer.Location} | {string.Join(", ", m.JobOffer.Tags)}");

        return new LlmRequest(
            TriagePrompts.System,
            $"""
            <profil_kandidata>
            {LlmJson.Serialize(profile)}
            </profil_kandidata>

            <nabidky>
            {string.Join("\n", lines)}
            </nabidky>

            Rozhodni o každé nabídce.
            """)
        {
            JsonSchema = TriagePrompts.Schema,
            MaxTokens = 4000,
            Tier = LlmTier.Fast,
            Purpose = LlmPurpose.Triage
        };
    }
}