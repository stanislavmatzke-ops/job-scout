namespace JobScout.Application.Limits;

/// <summary>
/// Configuration "AiLimits" (ADR-0021): how much AI a user may start by clicking within any 24 hours.
/// The morning run has its own fixed limits (EvaluateJobs.MaxPerRun, WriteCoverLetters.MaxPerRun) and is not counted here.
/// </summary>
public sealed class AiLimitOptions
{
    public const string SectionName = "AiLimits";

    /// <summary>The sliding window – no midnight, no time zones, nothing to wait out.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>false = no limits at all (e.g. a local experiment). Admins never have limits.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>New profile analyses (a new CV or changed notes) – an unchanged CV costs nothing and is not counted.</summary>
    public int ProfilePer24h { get; init; } = 3;

    /// <summary>Cover letters written on demand – new ones and rewrites together.</summary>
    public int LettersPer24h { get; init; } = 10;

    /// <summary>"Napsat nový návrh" over an existing letter, per job, ever.</summary>
    public int LetterRewritesPerJob { get; init; } = 2;

    /// <summary>"Ohodnotit" / "Přesto ohodnotit" / "Přehodnotit".</summary>
    public int EvaluationsPer24h { get; init; } = 20;

    /// <summary>What one user's clicks may cost in total.</summary>
    public decimal UserCostPer24hUsd { get; init; } = 0.50m;

    /// <summary>All users' clicks together – the emergency brake; the admins get an e-mail when it engages.</summary>
    public decimal AppCostPer24hUsd { get; init; } = 5.00m;
}
