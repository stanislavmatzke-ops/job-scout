using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>One search run for one user – the journal that tells the next run where to continue.</summary>
public class ScoutRunTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_run_is_running()
    {
        var userId = Guid.NewGuid();
        var run = new ScoutRun(userId, T0);

        Assert.Equal(userId, run.UserId);
        Assert.Equal(ScoutRunStatus.Running, run.Status);
        Assert.Equal(T0, run.StartedAt);
        Assert.Null(run.FinishedAt);
        Assert.Null(run.Error);
    }

    [Fact]
    public void Succeeded_run_keeps_counts_and_finish_time()
    {
        var run = new ScoutRun(Guid.NewGuid(), T0);

        run.Succeed(found: 9, newForUser: 3, T0.AddMinutes(2));

        Assert.Equal(ScoutRunStatus.Succeeded, run.Status);
        Assert.Equal(9, run.Found);
        Assert.Equal(3, run.NewForUser);
        Assert.Equal(T0.AddMinutes(2), run.FinishedAt);
    }

    [Fact]
    public void Failed_run_keeps_the_error()
    {
        var run = new ScoutRun(Guid.NewGuid(), T0);

        run.Fail("HttpRequestException: 503", T0.AddMinutes(1));

        Assert.Equal(ScoutRunStatus.Failed, run.Status);
        Assert.Equal("HttpRequestException: 503", run.Error);
        Assert.Equal(T0.AddMinutes(1), run.FinishedAt);
    }

    [Fact]
    public void Very_long_error_is_truncated()
    {
        var run = new ScoutRun(Guid.NewGuid(), T0);

        run.Fail(new string('x', 10_000), T0);

        Assert.Equal(ScoutRun.MaxErrorLength, run.Error!.Length);
    }

    [Fact]
    public void Finished_run_cannot_be_finished_again()
    {
        // A run has exactly one outcome – a second one is a bug in the caller
        var run = new ScoutRun(Guid.NewGuid(), T0);
        run.Succeed(1, 1, T0);

        Assert.Throws<InvalidOperationException>(() => run.Fail("late error", T0));
        Assert.Throws<InvalidOperationException>(() => run.Succeed(2, 2, T0));
    }
}
