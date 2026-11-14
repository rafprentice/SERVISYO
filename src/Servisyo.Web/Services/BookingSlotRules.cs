namespace Servisyo.Web.Services;

/// What is wrong with a requested date or time (Ok = nothing).
public enum SlotCheck
{
    Ok,
    NoticeTooShort,   // earlier than today + the provider's minimum notice days (or in the past)
    TooFar,           // further ahead than BookingSlotRules.MaxAdvanceDays
    DayOff,           // the provider marked this date as unavailable
    NotWorking,       // the provider has no schedule on this day of the week
    DayFull,          // the provider already has their maximum number of jobs on this date
    TimePassed,       // today, but that time is already over (the earliest start today is the next whole minute)
    NotWholeMinute,   // a time with seconds: start times are whole minutes
    OutsideHours,     // the whole job does not fit inside the provider's working hours
    InBreak,          // the job would run into one of the provider's breaks
    Clash             // another active booking (Pending / Accepted / In Progress) overlaps
}

/// The booking rules from the project document in one place, with no database access so they can be tested alone:
/// minimum notice, weekly schedule, unavailable dates, breaks, maximum jobs per day and overlapping bookings.
/// (The database functions fn_IsProviderAvailable / fn_CountProviderJobsOnDate describe the same rules. This version
/// also checks that the WHOLE job fits the working hours and does not touch a break, not only the start time.)
public static class BookingSlotRules
{
    public const int MaxAdvanceDays = 90;
    public const int StepMinutes = 30;           // start times are offered every 30 minutes (plus the exact ones below)

    /// One working window of the chosen weekday, with its breaks.
    public sealed record Window(TimeOnly Start, TimeOnly End, IReadOnlyList<(TimeOnly Start, TimeOnly End)> Breaks);

    /// An existing active booking of the provider on the chosen date.
    public sealed record Busy(TimeOnly Start, int Minutes);

    private static int M(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// Everything that depends on the DATE only.
    public static SlotCheck CheckDay(DateOnly date, DateOnly today, int minNoticeDays, bool dateUnavailable,
        IReadOnlyList<Window> windows, int activeJobs, int maxJobsPerDay)
    {
        if (date < today.AddDays(Math.Max(0, minNoticeDays))) return SlotCheck.NoticeTooShort;
        if (date > today.AddDays(MaxAdvanceDays)) return SlotCheck.TooFar;
        if (dateUnavailable) return SlotCheck.DayOff;
        if (windows.Count == 0) return SlotCheck.NotWorking;
        if (activeJobs >= maxJobsPerDay) return SlotCheck.DayFull;
        return SlotCheck.Ok;
    }

    /// The earliest start time today: the next whole minute (at 4:04:30 AM it is 4:05 AM). Null in the last minute of the day.
    public static TimeOnly? NextMinute(TimeOnly nowTime)
    {
        var m = M(nowTime) + 1;
        return m >= 24 * 60 ? null : new TimeOnly(m / 60, m % 60);
    }

    /// Everything that depends on the START TIME and the length of the job. Any whole minute may be asked for (the
    /// customer's "Enter exact time"); the same rules apply as for the offered buttons.
    public static SlotCheck CheckTime(TimeOnly time, int durationMinutes, DateOnly date, DateOnly today, TimeOnly nowTime,
        IReadOnlyList<Window> windows, IReadOnlyList<Busy> busy)
    {
        if (time.Second != 0 || time.Millisecond != 0 || time.Microsecond != 0) return SlotCheck.NotWholeMinute;
        if (date == today && (NextMinute(nowTime) is not { } earliest || time < earliest)) return SlotCheck.TimePassed;

        var start = M(time);
        var end = start + durationMinutes;

        var window = windows.FirstOrDefault(w => start >= M(w.Start) && end <= M(w.End));
        if (window is null) return SlotCheck.OutsideHours;

        if (window.Breaks.Any(b => start < M(b.End) && M(b.Start) < end)) return SlotCheck.InBreak;
        if (busy.Any(o => start < M(o.Start) + o.Minutes && M(o.Start) < end)) return SlotCheck.Clash;   // the two time ranges touch

        return SlotCheck.Ok;
    }

    /// The first problem with a date + time, or Ok.
    public static SlotCheck Check(DateOnly date, TimeOnly time, int durationMinutes, DateOnly today, TimeOnly nowTime,
        int minNoticeDays, bool dateUnavailable, IReadOnlyList<Window> windows, IReadOnlyList<Busy> busy, int maxJobsPerDay)
    {
        var day = CheckDay(date, today, minNoticeDays, dateUnavailable, windows, busy.Count, maxJobsPerDay);
        return day != SlotCheck.Ok ? day : CheckTime(time, durationMinutes, date, today, nowTime, windows, busy);
    }

    /// Every offered start time at which a job of this length can be booked on that date, earliest first:
    ///   - today, when the provider needs no notice: the soonest one, the next whole minute (4:05 AM when it is 4:04 AM);
    ///   - the provider's exact shift start (4:04 AM), not rounded;
    ///   - then every :00 and :30 (4:30 AM, 5:00 AM ...).
    /// Every one of them passes CheckTime (working hours, breaks, other bookings, not in the past).
    public static IReadOnlyList<TimeOnly> Slots(DateOnly date, int durationMinutes, DateOnly today, TimeOnly nowTime,
        int minNoticeDays, bool dateUnavailable, IReadOnlyList<Window> windows, IReadOnlyList<Busy> busy, int maxJobsPerDay)
    {
        if (CheckDay(date, today, minNoticeDays, dateUnavailable, windows, busy.Count, maxJobsPerDay) != SlotCheck.Ok)
            return Array.Empty<TimeOnly>();

        var found = new SortedSet<TimeOnly>();
        void Try(int m)
        {
            if (m < 0 || m >= 24 * 60) return;
            var t = new TimeOnly(m / 60, m % 60);
            if (CheckTime(t, durationMinutes, date, today, nowTime, windows, busy) == SlotCheck.Ok) found.Add(t);
        }

        if (SoonestSlot(date, today, nowTime, minNoticeDays) is { } soon) Try(M(soon));
        foreach (var w in windows)
        {
            Try(M(w.Start));                                                                    // the exact shift start
            var first = (int)Math.Ceiling(M(w.Start) / (double)StepMinutes) * StepMinutes;     // 8:10 -> 8:30
            for (var m = first; m < 24 * 60 && m + durationMinutes <= M(w.End); m += StepMinutes) Try(m);
        }
        return found.ToList();
    }

    /// The "as soon as possible" start: the next whole minute, only for today and only when the provider needs no
    /// notice days. Null otherwise.
    public static TimeOnly? SoonestSlot(DateOnly date, DateOnly today, TimeOnly nowTime, int minNoticeDays) =>
        date == today && minNoticeDays <= 0 ? NextMinute(nowTime) : null;

    /// The sentence shown to the customer.
    public static string Explain(SlotCheck check, int minNoticeDays) => check switch
    {
        SlotCheck.NoticeTooShort => minNoticeDays > 0
            ? $"This provider needs at least {minNoticeDays} day{(minNoticeDays == 1 ? "" : "s")} of notice. Please pick a later date."
            : "That date has already passed. Please pick a later date.",
        SlotCheck.TooFar => $"Bookings can only be made up to {MaxAdvanceDays} days ahead.",
        SlotCheck.DayOff => "The provider is not available on that date.",
        SlotCheck.NotWorking => "The provider does not work on that day of the week.",
        SlotCheck.DayFull => "The provider is fully booked on that date.",
        SlotCheck.TimePassed => "That time has already passed. Please pick a later time (at least 1 minute from now).",
        SlotCheck.NotWholeMinute => "Please choose a start time in whole minutes (for example 4:05 AM).",
        SlotCheck.OutsideHours => "That time does not fit the provider's working hours for the services you chose.",
        SlotCheck.InBreak => "That time overlaps the provider's break.",
        SlotCheck.Clash => "That time is already taken. Please choose another.",
        _ => string.Empty
    };

    /// True when the problem is about the DATE (so the message belongs under the date box).
    public static bool IsDateProblem(SlotCheck check) =>
        check is SlotCheck.NoticeTooShort or SlotCheck.TooFar or SlotCheck.DayOff or SlotCheck.NotWorking or SlotCheck.DayFull;
}
