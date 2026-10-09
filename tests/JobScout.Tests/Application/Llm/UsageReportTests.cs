using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Application.Llm;

/// <summary>How much did a run / a user cost – grouped by purpose, summed in the database.</summary>
public sealed class UsageReportTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _run = Guid.NewGuid();

    public void Dispose() => _database.Dispose();

    private void Seed(params LlmCall[] calls)
    {
        using var db = _database.CreateContext();
        db.LlmCalls.AddRange(calls);
        db.SaveChanges();
    }

    private LlmCall Ok(string purpose, long cost, Guid? run = null, Guid? user = null, DateTimeOffset? at = null) =>
        LlmCall.Success(user ?? _user, run ?? _run, purpose, "claude-sonnet-5-5", 1000, 100, cost, 500, at ?? T0);

    [Fact]
    public async Task Run_summary_groups_by_purpose()
    {
        Seed(Ok("Evaluation", 17_000), Ok("Evaluation", 15_000), Ok("Triage", 2_500), Ok("CoverLetter", 18_000));

        await using var db = _database.CreateContext();
        var summary = await new UsageReport(db).ForRunAsync(_run);

        var evaluation = summary.Lines.Single(l => l.Purpose == "Evaluation");
        Assert.Equal((2, 2000L, 200L, 32_000L), (evaluation.Calls, evaluation.InputTokens, evaluation.OutputTokens, evaluation.CostMicroUsd));
        Assert.Equal(3, summary.Lines.Count);
        Assert.Equal(52_500, summary.TotalMicroUsd);
        Assert.Equal(0.0525m, summary.TotalUsd);
    }

    [Fact]
    public async Task Other_runs_are_not_counted()
    {
        Seed(Ok("Evaluation", 17_000), Ok("Evaluation", 99_000, run: Guid.NewGuid()));

        await using var db = _database.CreateContext();

        Assert.Equal(17_000, (await new UsageReport(db).ForRunAsync(_run)).TotalMicroUsd);
    }

    [Fact]
    public async Task Failed_calls_are_counted_as_calls_with_zero_cost()
    {
        Seed(Ok("Evaluation", 17_000), LlmCall.Failure(_user, _run, "Evaluation", "Smart", "529", 100, T0));

        await using var db = _database.CreateContext();
        var line = Assert.Single((await new UsageReport(db).ForRunAsync(_run)).Lines);

        Assert.Equal(2, line.Calls);
        Assert.Equal(1, line.Failed);
        Assert.Equal(17_000, line.CostMicroUsd);
    }

    [Fact]
    public async Task User_summary_covers_a_time_range_across_runs()
    {
        Seed(
            Ok("Profile", 24_000, run: null, at: T0.AddDays(-40)),       // outside the month
            Ok("Evaluation", 17_000, run: Guid.NewGuid(), at: T0.AddDays(-3)),
            Ok("Evaluation", 15_000, run: Guid.NewGuid(), at: T0),
            Ok("Evaluation", 50_000, user: Guid.NewGuid(), at: T0));     // somebody else

        await using var db = _database.CreateContext();
        var month = await new UsageReport(db).ForUserAsync(_user, T0.AddDays(-30), T0.AddDays(1));

        Assert.Equal(32_000, month.TotalMicroUsd);
    }

    [Fact]
    public async Task Empty_run_has_an_empty_summary()
    {
        await using var db = _database.CreateContext();
        var summary = await new UsageReport(db).ForRunAsync(_run);

        Assert.Empty(summary.Lines);
        Assert.Equal(0, summary.TotalMicroUsd);
    }
}
