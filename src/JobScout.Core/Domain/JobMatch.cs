namespace JobScout.Core.Domain;

/// <summary>Where the morning pipeline got with the job: Found → Shortlisted | TriagedOut → Evaluated.</summary>
public enum MatchStage { Found, Shortlisted, TriagedOut, Evaluated }

/// <summary>What the user did with the job.</summary>
public enum MatchStatus
{
    New, SavedForLater,
    Applied, Call, Round1, Round2, Offer, Rejected, Withdrawn,
    Dismissed,   // "Zahozená" – not interested, stays in the archive
    Hidden,      // "Nechci vidět" – never show again
}

/// <summary>One user × one job: AI evaluation, cover letter, status and notes. Unique by UserId + JobOfferId.</summary>
public sealed class JobMatch
{
    private readonly List<JobMatchEvent> _events = [];

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid JobOfferId { get; private set; }
    public JobOffer? JobOffer { get; private set; }            // navigation, loaded by EF when needed
    /// <summary>Profile version the job was last evaluated with (before the evaluation: the version it was found with).</summary>
    public int ProfileVersion { get; private set; }
    public MatchStage Stage { get; private set; }
    public string? TriageReason { get; private set; }
    public int? TriageScore { get; private set; }               // 1–5 from triage; orders the evaluation queue
    public int? Score { get; private set; }
    public string? EvaluationJson { get; private set; }         // JobEvaluation from AI
    public string? CoverLetter { get; private set; }
    public MatchStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<JobMatchEvent> Events => _events;

    private JobMatch() { }

    public JobMatch(Guid userId, Guid jobOfferId, int profileVersion, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        UserId = userId;
        JobOfferId = jobOfferId;
        ProfileVersion = profileVersion;
        Stage = MatchStage.Found;
        Status = MatchStatus.New;
        CreatedAt = UpdatedAt = now;
    }

    public void MarkTriagedOut(string reason, DateTimeOffset now, int? relevance = null)
    {
        Stage = MatchStage.TriagedOut;
        TriageReason = reason;
        TriageScore = relevance;
        UpdatedAt = now;
    }

    /// <summary>Passed the cheap triage with relevance 3–5 – waits for the detail and the full evaluation.</summary>
    public void Shortlist(int relevance, DateTimeOffset now)
    {
        if (relevance is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(relevance), relevance, "Relevance must be 1–5.");

        Stage = MatchStage.Shortlisted;
        TriageScore = relevance;
        UpdatedAt = now;
    }

    /// <summary>Evaluated with profile version <paramref name="profileVersion"/> – compared with the current one to spot outdated evaluations (6d).</summary>
    public void ApplyEvaluation(int score, string evaluationJson, int profileVersion, DateTimeOffset now)
    {
        if (score is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(score), score, "Score must be 1–10.");

        Stage = MatchStage.Evaluated;
        Score = score;
        EvaluationJson = evaluationJson;
        ProfileVersion = profileVersion;
        UpdatedAt = now;
    }

    public void SetCoverLetter(string coverLetter, DateTimeOffset now)
    {
        CoverLetter = coverLetter;
        UpdatedAt = now;
    }

    /// <summary>Every status change is recorded in the history – there is no other way to change it.</summary>
    public void ChangeStatus(MatchStatus to, string? note, DateTimeOffset now)
    {
        if (to == Status && string.IsNullOrWhiteSpace(note))
            return;   // nothing happened

        _events.Add(new JobMatchEvent(Id, Status, to, note, now));
        Status = to;
        UpdatedAt = now;
    }

    public void UpdateNotes(string? notes, DateTimeOffset now)
    {
        Notes = notes;
        UpdatedAt = now;
    }

    /// <summary>
    /// The demo account's nightly clean-up (ADR-0020): what visitors changed – status, history, notes – goes away;
    /// what the AI produced (evaluation, letter) stays. UpdatedAt is kept, so the morning report still sees what's new.
    /// </summary>
    /// <returns>true = something was changed back</returns>
    public bool ResetVisitorChanges()
    {
        if (Status == MatchStatus.New && Notes is null && _events.Count == 0)
            return false;
        Status = MatchStatus.New;
        Notes = null;
        _events.Clear();
        return true;
    }

    /// <summary>Shown in the user's list (triaged-out and hidden jobs are not).</summary>
    public bool IsVisible => Stage == MatchStage.Evaluated && Status != MatchStatus.Hidden;
}

/// <summary>History entry: status change and/or note (e.g. "Call with HR, next round on Friday").</summary>
public sealed class JobMatchEvent
{
    public Guid Id { get; private set; }
    public Guid JobMatchId { get; private set; }
    public MatchStatus From { get; private set; }
    public MatchStatus To { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset At { get; private set; }

    private JobMatchEvent() { }

    internal JobMatchEvent(Guid jobMatchId, MatchStatus from, MatchStatus to, string? note, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7(at);
        JobMatchId = jobMatchId;
        From = from;
        To = to;
        Note = note;
        At = at;
    }
}
