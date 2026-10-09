namespace JobScout.Application.Llm;

/// <summary>
/// Ambient "who and which run is this AI call for" (ADR-0016). Set by the host (Worker / API) and AnalyzeProfile;
/// read by the usage recorder. AsyncLocal flows into awaited calls; Dispose restores the previous value.
/// </summary>
public static class LlmCallContext
{
    public sealed record Scope(Guid? UserId, Guid? ScoutRunId);

    private static readonly AsyncLocal<Scope?> Ambient = new();

    public static Scope? Current => Ambient.Value;

    public static IDisposable Begin(Guid? userId, Guid? scoutRunId = null)
    {
        var previous = Ambient.Value;
        Ambient.Value = new Scope(userId, scoutRunId);
        return new Restore(previous);
    }

    private sealed class Restore(Scope? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}