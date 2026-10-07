namespace JobScout.Tests.TestUtils;

/// <summary>Clock under the test's control (ADR-0009: time is a parameter). Move it with <see cref="Now"/>.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
