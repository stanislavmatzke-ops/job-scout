namespace JobScout.Application.Scouting;

/// <summary>What the scheduled job should do when it wakes up.</summary>
public enum NightRun
{
    /// <summary>Tonight's hour – the run for everybody.</summary>
    Main,
    /// <summary>An hour later – again, only for the users whose run failed tonight; a second failure tells the admins.</summary>
    Retry,
    /// <summary>A night hour that is not tonight's – nothing to do.</summary>
    NotNow,
    /// <summary>Daytime – somebody started the job by hand: the run for everybody, as before.</summary>
    Manual,
}

/// <summary>
/// The search runs at night, when job portals are quiet, at a different hour every night (between 1 and 5 o'clock,
/// Prague time) – not at a round hour together with every other robot (20 Oct 2026: jobs.cz answered the 7:30 run with
/// empty pages, a run two hours later was fine). The job wakes up every night hour (cron) and asks this class.
/// Deterministic – no state to keep: odd days pick from 1/3/5 o'clock, even days from 2/4, so two nights in a row
/// never share the hour.
/// </summary>
public static class NightSchedule
{
    /// <summary>Night hours belong to the schedule (with a margin for summer/winter time – the cron is in UTC).</summary>
    public const int NightStartHour = 0;
    public const int NightEndHour = 7;

    private static readonly int[] OddDayHours = [1, 3, 5];
    private static readonly int[] EvenDayHours = [2, 4];

    public static readonly TimeZoneInfo Prague = FindPrague();

    /// <summary>The hour (Prague time) of the night that starts the given day: 1–5.</summary>
    public static int HourFor(DateOnly day)
    {
        var hours = day.DayNumber % 2 == 1 ? OddDayHours : EvenDayHours;
        // A cheap, well-spread mix of the day number – the same day always gives the same hour
        var mixed = unchecked((uint)day.DayNumber * 2654435761u) >> 16;
        return hours[mixed % (uint)hours.Length];
    }

    /// <summary>When tonight's run starts (UTC) – the retry runs only for users without a successful run since then.</summary>
    public static DateTimeOffset PlannedStart(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(HourFor(day), 0));
        return new DateTimeOffset(local, Prague.GetUtcOffset(local));
    }

    public static NightRun Decide(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Prague);
        if (local.Hour is < NightStartHour or > NightEndHour)
            return NightRun.Manual;

        var planned = HourFor(DateOnly.FromDateTime(local.DateTime));
        return local.Hour == planned ? NightRun.Main
            : local.Hour == planned + 1 ? NightRun.Retry
            : NightRun.NotNow;
    }

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Prague).DateTime);

    private static TimeZoneInfo FindPrague()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");     // IANA ids work on Windows too (.NET 6+, ICU)
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Not UTC: an hour off, a cron wake-up at 23 UTC would look like the daytime and run everybody again
            return EuCentralTime();
        }
    }

    /// <summary>CET with the EU summer time rule (last Sunday of March 2:00 – last Sunday of October 3:00).</summary>
    internal static TimeZoneInfo EuCentralTime()
    {
        var summer = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone("CET (EU rules)", TimeSpan.FromHours(1), "CET", "CET", "CEST", [summer]);
    }
}
