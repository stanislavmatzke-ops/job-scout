namespace JobScout.Core.Domain;

public enum ScoutRunStatus { Running, Succeeded, Failed }

/// <summary>
/// One search run for one user. The last SUCCEEDED run decides how far back the next one searches –
/// a failed or interrupted run doesn't count, so a missed morning doesn't lose that day's ads.
/// </summary>
public sealed class ScoutRun
{
    public const int MaxErrorLength = 2000;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ScoutRunStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public int Found { get; private set; }
    public int NewForUser { get; private set; }
    public string? Error { get; private set; }

    private ScoutRun() { }   // for EF Core

    public ScoutRun(Guid userId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        UserId = userId;
        Status = ScoutRunStatus.Running;
        StartedAt = now;
    }

    public void Succeed(int found, int newForUser, DateTimeOffset now)
    {
        EnsureRunning();
        Status = ScoutRunStatus.Succeeded;
        Found = found;
        NewForUser = newForUser;
        FinishedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        EnsureRunning();
        Status = ScoutRunStatus.Failed;
        Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        FinishedAt = now;
    }

    // A run has exactly one outcome – a second one is a bug in the caller
    private void EnsureRunning()
    {
        if (Status != ScoutRunStatus.Running)
            throw new InvalidOperationException($"Běh {Id} už skončil ({Status}).");
    }
}