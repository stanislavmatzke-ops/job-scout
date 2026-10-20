using JobScout.Application.Scouting;

namespace JobScout.Tests.Application.Scouting;

/// <summary>
/// The search runs at night at a different hour every night (1–5 o'clock Prague time), the retry an hour later.
/// The job wakes up every night hour (cron in UTC) – summer and winter time must both work.
/// </summary>
public class NightScheduleTests
{
    /// <summary>The cron of the morning job in main.bicep: minute 13 of these UTC hours.</summary>
    private static readonly int[] CronUtcHours = [23, 0, 1, 2, 3, 4, 5];

    private static IEnumerable<DateOnly> Days(int count) =>
        Enumerable.Range(0, count).Select(i => new DateOnly(2026, 1, 1).AddDays(i));

    [Fact]
    public void Every_night_has_an_hour_between_one_and_five_never_the_same_two_nights_in_a_row()
    {
        var days = Days(3 * 366).ToList();

        Assert.All(days, d => Assert.InRange(NightSchedule.HourFor(d), 1, 5));
        Assert.All(days.Skip(1), d => Assert.NotEqual(NightSchedule.HourFor(d.AddDays(-1)), NightSchedule.HourFor(d)));
        Assert.Equal([1, 2, 3, 4, 5], days.Select(NightSchedule.HourFor).Distinct().Order().ToArray());
    }

    [Fact]
    public void The_same_day_always_gets_the_same_hour()
    {
        var day = new DateOnly(2026, 10, 21);

        Assert.Equal(NightSchedule.HourFor(day), NightSchedule.HourFor(day));
    }

    [Fact]
    public void Tonights_hour_runs_for_everybody_an_hour_later_the_retry_otherwise_nothing()
    {
        var day = new DateOnly(2026, 10, 21);
        var hour = NightSchedule.HourFor(day);
        DateTimeOffset At(int h) => new(day.ToDateTime(new TimeOnly(h, 13)), TimeSpan.FromHours(2));   // CEST

        Assert.Equal(NightRun.Main, NightSchedule.Decide(At(hour)));
        Assert.Equal(NightRun.Retry, NightSchedule.Decide(At(hour + 1)));
        Assert.Equal(NightRun.NotNow, NightSchedule.Decide(At(hour == 1 ? 4 : hour - 1)));
        Assert.Equal(NightRun.Manual, NightSchedule.Decide(At(10)));        // started by hand in the daytime
    }

    [Fact]
    public void Every_wake_up_of_the_cron_is_a_night_hour_in_summer_and_winter()
    {
        // A wake-up outside the night window would count as "started by hand" and run everybody again
        foreach (var day in Days(366))
        foreach (var utcHour in CronUtcHours)
        {
            var wakeUp = new DateTimeOffset(day.ToDateTime(new TimeOnly(utcHour, 13)), TimeSpan.Zero);
            Assert.NotEqual(NightRun.Manual, NightSchedule.Decide(wakeUp));
        }
    }

    [Fact]
    public void The_cron_wakes_the_job_for_tonights_hour_and_for_the_retry_in_summer_and_winter()
    {
        foreach (var day in Days(366))
        {
            var wakeUps = CronUtcHours
                .Select(h => new DateTimeOffset(day.ToDateTime(new TimeOnly(h, 13)), TimeSpan.Zero))
                .Concat(CronUtcHours.Select(h => new DateTimeOffset(day.AddDays(-1).ToDateTime(new TimeOnly(h, 13)), TimeSpan.Zero)))
                .Where(t => NightSchedule.Today(t) == day)
                .Select(NightSchedule.Decide)
                .ToList();

            Assert.Contains(NightRun.Main, wakeUps);
            Assert.Contains(NightRun.Retry, wakeUps);
        }
    }

    [Fact]
    public void Planned_start_is_tonights_hour_in_prague_time()
    {
        var summer = new DateOnly(2026, 7, 15);
        var winter = new DateOnly(2026, 12, 15);

        Assert.Equal(TimeSpan.FromHours(2), NightSchedule.PlannedStart(summer).Offset);
        Assert.Equal(NightSchedule.HourFor(summer), NightSchedule.PlannedStart(summer).Hour);
        Assert.Equal(TimeSpan.FromHours(1), NightSchedule.PlannedStart(winter).Offset);
    }

    [Fact]
    public void Without_time_zone_data_the_eu_rule_gives_the_same_prague_time()
    {
        var prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        var fallback = NightSchedule.EuCentralTime();

        for (var t = new DateTimeOffset(2026, 1, 1, 0, 13, 0, TimeSpan.Zero); t.Year < 2029; t = t.AddHours(5))
            Assert.Equal(prague.GetUtcOffset(t), fallback.GetUtcOffset(t));
    }
}
