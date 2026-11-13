using Servisyo.Web.Helpers;

namespace Servisyo.Web.Models;

/// Database v2.12: where an Accepted, scheduled booking stands against its start time (Philippine Standard Time).
public enum StartWindowState
{
    TooEarly,   // before the scheduled date and time: no button, only a muted notice
    OnTime,     // 0 to 15 minutes after: the blue "Mark In Progress"
    Late,       // 16 minutes to 2 hours after: the amber "Mark In Progress (Late)", a reason is required
    Expired     // more than 2 hours after: no button; the booking is cancelled as a provider no-show
}

/// Database v2.12: the start window of a booking. The same rules are enforced three times: here (the buttons and
/// ProviderBookingsService.StartAsync), in CHECK CK_BOOKINGS_START (0 to 120 minutes, reason when late) and in
/// trg_Bookings_StatusFlow (STARTED_AT is "now", written once; Accepted -> Cancelled only for a no-show).
public static class StartRules
{
    public const int OnTimeMinutes = 15;          // up to 15 minutes after the scheduled time = on time
    public const int WindowMinutes = 120;         // up to 2 hours after = late (with a reason); later = missed
    /// The no-show job waits one minute more than the button, so the app's and the database's clocks never disagree.
    public const int NoShowAfterMinutes = WindowMinutes + 1;
    public const int MaxLateReasonLength = 500;   // BOOKINGS.LATE_REASON NVARCHAR(500)

    public const string TooEarlyMessage = "Cannot start a booking before its scheduled date and time.";
    public const string ExpiredMessage = "Booking start window has expired.";
    public const string LateReasonRequired = "Please enter the reason for the late start before marking the booking In Progress.";
    public const string LateReasonTooLong = "Please keep the late reason to 500 characters or fewer.";
    /// Shown on a booking that was cancelled because it was not started in time (and written into System Logs).
    public const string NoShowReason = "Provider No-Show — missed scheduled start window";

    /// The scheduled start, as the clock in Cebu shows it (BOOKING_DATE + BOOKING_TIME are Cebu date and time).
    public static DateTime Scheduled(DateOnly date, TimeOnly time) => date.ToDateTime(time);

    /// Whole minutes from a to b, counted exactly like SQL Server's DATEDIFF(MINUTE, a, b) (minute boundaries crossed),
    /// so the app and CK_BOOKINGS_START always agree.
    public static int MinutesBetween(DateTime a, DateTime b) =>
        (int)(b.Ticks / TimeSpan.TicksPerMinute - a.Ticks / TimeSpan.TicksPerMinute);

    /// Minutes since the scheduled start (negative = before it). manilaNow is Cebu time (PhilippineTime.Now).
    public static int MinutesAfter(DateOnly date, TimeOnly time, DateTime manilaNow) => MinutesBetween(Scheduled(date, time), manilaNow);

    public static StartWindowState StateAt(int minutesAfter) => minutesAfter switch
    {
        < 0 => StartWindowState.TooEarly,
        <= OnTimeMinutes => StartWindowState.OnTime,
        <= WindowMinutes => StartWindowState.Late,
        _ => StartWindowState.Expired
    };

    public static StartWindowState StateAt(DateOnly date, TimeOnly time, DateTime manilaNow) => StateAt(MinutesAfter(date, time, manilaNow));

    /// The next moment the state changes (Cebu time): the scheduled time, then 16 minutes and 121 minutes after it.
    /// Null once the window has expired (the no-show job takes it from there).
    public static DateTime? NextChange(DateOnly date, TimeOnly time, DateTime manilaNow)
    {
        var scheduled = Scheduled(date, time);
        return StateAt(date, time, manilaNow) switch
        {
            StartWindowState.TooEarly => scheduled,
            StartWindowState.OnTime => scheduled.AddMinutes(OnTimeMinutes + 1),
            StartWindowState.Late => scheduled.AddMinutes(WindowMinutes + 1),
            _ => null
        };
    }

    /// How late a start that happened was: the minutes when it was more than 15 minutes after the scheduled time,
    /// null when it was on time (or has not happened). The same number as the calculated column MINUTES_LATE.
    public static int? MinutesLate(DateOnly date, TimeOnly time, DateTime? startedAtUtc)
    {
        if (startedAtUtc is null) return null;
        var minutes = MinutesAfter(date, time, PhilippineTime.FromUtc(startedAtUtc.Value));
        return minutes > OnTimeMinutes ? minutes : null;
    }

    /// A cancelled booking that has a payment (paid, or cash on completion promised) is a provider no-show: customers
    /// cancel only Pending requests, which never have a payment, and trg_Bookings_StatusFlow allows Accepted ->
    /// Cancelled only for a no-show. Worked out from the data, not stored (rule 2).
    public static bool IsNoShow(string status, bool hasPayment) => status == BookingStatuses.Cancelled && hasPayment;

    /// The late reason as it is saved: one kind of line break, trimmed, NULL when empty.
    public static string? CleanReason(string? reason)
    {
        var text = reason?.Replace("\r\n", "\n").Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
