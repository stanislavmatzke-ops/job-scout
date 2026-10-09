namespace JobScout.Core.Domain;

/// <summary>One call to the model – paid or failed. Audit log for costs (ADR-0016); no foreign keys on purpose.</summary>
public sealed class LlmCall
{
    public const int MaxErrorLength = 1000;

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? ScoutRunId { get; private set; }
    public string Purpose { get; private set; } = "";
    public string Model { get; private set; } = "";
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public long CostMicroUsd { get; private set; }      // 1 USD = 1 000 000 – exact and summable in SQL
    public int DurationMs { get; private set; }
    public bool Succeeded { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset At { get; private set; }

    public decimal CostUsd => CostMicroUsd / 1_000_000m;

    private LlmCall() { }   // for EF Core

    public static LlmCall Success(Guid? userId, Guid? scoutRunId, string purpose, string model,
        long inputTokens, long outputTokens, long costMicroUsd, int durationMs, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), UserId = userId, ScoutRunId = scoutRunId, Purpose = purpose, Model = model,
        InputTokens = inputTokens, OutputTokens = outputTokens, CostMicroUsd = costMicroUsd,
        DurationMs = durationMs, Succeeded = true, At = now,
    };

    public static LlmCall Failure(Guid? userId, Guid? scoutRunId, string purpose, string model,
        string error, int durationMs, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), UserId = userId, ScoutRunId = scoutRunId, Purpose = purpose, Model = model,
        Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error,
        DurationMs = durationMs, Succeeded = false, At = now,
    };
}