using JobScout.Application.Jobs;

namespace JobScout.Tests.Application.Jobs;

/// <summary>
/// How far back to search: from the last SUCCESSFUL run, not from "has the user any matches".
/// A missed morning (outage) must not lose the ads published that day.
/// </summary>
public class SearchWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_run_searches_three_days()
    {
        var window = SearchWindow.For(lastSuccessStartedAt: null, Now);

        Assert.Equal(JobAge.ThreeDays, window.MaxAge);
        Assert.False(window.HasGap);
    }

    [Theory]
    [InlineData(1)]      // ran an hour ago (manual re-run)
    [InlineData(23)]     // yesterday morning, a bit later than today
    public void Run_within_a_day_searches_the_last_24_hours(int hoursAgo)
    {
        var window = SearchWindow.For(Now.AddHours(-hoursAgo), Now);

        Assert.Equal(JobAge.Day, window.MaxAge);
        Assert.False(window.HasGap);
    }

    [Theory]
    [InlineData(25)]     // yesterday's run was a bit earlier – 24 h would leave a hole
    [InlineData(48)]     // a missed morning
    [InlineData(72)]
    public void Older_last_success_searches_three_days(int hoursAgo)
    {
        var window = SearchWindow.For(Now.AddHours(-hoursAgo), Now);

        Assert.Equal(JobAge.ThreeDays, window.MaxAge);
        Assert.False(window.HasGap);
    }

    [Fact]
    public void Gap_longer_than_three_days_is_reported()
    {
        // jobs.cz offers no longer "new" filter we use – the run must say that ads may have been missed
        var window = SearchWindow.For(Now.AddDays(-5), Now);

        Assert.Equal(JobAge.ThreeDays, window.MaxAge);
        Assert.True(window.HasGap);
    }
}
