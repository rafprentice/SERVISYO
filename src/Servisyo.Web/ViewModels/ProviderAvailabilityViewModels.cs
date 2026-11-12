using System.Globalization;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// The break rules of the weekly schedule (checked by ProviderAvailabilityService, shown by the page):
///   - a break is only possible on a work shift of 8 hours or more (EndTime - StartTime);
///   - one break per day, exactly 1 hour long;
///   - it starts between the shift's StartTime and EndTime - 1 hour (so it ends inside the shift).
public static class BreakRules
{
    public const int MinShiftMinutes = 8 * 60;
    public const int BreakMinutes = 60;
    public const int StepMinutes = 30;              // the start times offered in the dropdown
    public const int MaxBreaksPerDay = 1;
    public const string ShortShiftMessage = "Break is only available for work shifts longer than 8 hours.";

    public static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// 8:00 am - 5:00 pm -> 540 (9 hours). A shift that ends before it starts gives 0 or less.
    public static int ShiftMinutes(TimeOnly start, TimeOnly end) => Minutes(end) - Minutes(start);

    public static bool AllowsBreak(TimeOnly? start, TimeOnly? end) =>
        start is { } s && end is { } e && ShiftMinutes(s, e) >= MinShiftMinutes;

    /// True when a 1-hour break starting at breakStart is completely inside the shift.
    public static bool Fits(TimeOnly breakStart, TimeOnly start, TimeOnly end) =>
        Minutes(breakStart) >= Minutes(start) && Minutes(breakStart) + BreakMinutes <= Minutes(end);

    public static TimeOnly EndOf(TimeOnly breakStart) => breakStart.AddMinutes(BreakMinutes);

    /// The latest start: EndTime - 1 hour.
    public static TimeOnly LatestStart(TimeOnly end) => end.AddMinutes(-BreakMinutes);

    /// The dropdown: every 30 minutes from StartTime up to EndTime - 1 hour (empty when the shift is under 8 hours).
    /// 7:00 am - 5:00 pm -> 7:00, 7:30, ... 4:00 pm.
    public static IReadOnlyList<TimeOnly> StartOptions(TimeOnly? start, TimeOnly? end)
    {
        if (!AllowsBreak(start, end)) return Array.Empty<TimeOnly>();
        var list = new List<TimeOnly>();
        for (var m = Minutes(start!.Value); m + BreakMinutes <= Minutes(end!.Value); m += StepMinutes)
            list.Add(new TimeOnly(m / 60, m % 60));
        return list;
    }

    /// The start to show for a saved break: itself when it still fits, otherwise the nearest start that does.
    public static TimeOnly? Nearest(TimeOnly breakStart, TimeOnly? start, TimeOnly? end)
    {
        var options = StartOptions(start, end);
        if (options.Count == 0) return null;
        if (Fits(breakStart, start!.Value, end!.Value)) return breakStart;
        return options.OrderBy(o => Math.Abs(Minutes(o) - Minutes(breakStart))).First();
    }

    /// 13:00 -> "1:00 pm"
    public static string Clock(TimeOnly t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
}

/// The day-off rules (database v2.7). A provider REQUESTS days off and an administrator approves or rejects them:
///   - 1 to 7 days in a row per request ("Maximum Day Off is 1 week or 7 days");
///   - from TOMORROW up to December 31 of THIS year (Philippine time) - no same-day requests;
///   - only ONE provider can be off on any date: a Pending request already holds its dates (first come, first served);
///   - a day with an active booking cannot be requested;
///   - one request at a time may wait for an administrator, so nobody can hold many dates with requests;
///   - a Pending request does NOT stop bookings; an Approved one does. A Rejected one gives its dates back.
/// On January 1 last year's days off and requests are deleted (UnavailableDatesResetService).
/// (An administrator can still give an extra day off for today: that is the emergency path.)
public static class UnavailableDateRules
{
    public const string SameDayMessage = "Same-day unavailable date selection is not permitted.";
    public const string SameDayAdvice = "Same-day off requests are not allowed. Unavailable dates must be marked at least 1 day in advance.";

    /// Database v2.5: only ONE provider can be off on any date (first come, first served), so customers always find
    /// someone. The other provider is not named to providers (an administrator sees the name).
    public const string TakenMessage = "Another provider already has this day off. Only one provider can be off per day, so please choose another date.";

    /// The most days one request can cover (a week).
    public const int MaxDaysPerRequest = 7;
    public const string MaxDaysMessage = "Maximum Day Off is 1 week or 7 days";

    public const string PendingLabel = "Pending Admin Approval";
    public const string ApprovedLabel = "Approved";
    public const string ApprovedNote = "Extra day off given by an administrator";

    /// The first day a provider can request: tomorrow (Philippine time).
    public static DateOnly FirstAllowed(DateOnly today) => today.AddDays(1);

    /// "Nov 20, 2026: you already have booking #12 (Accepted) on this day. ..." - a day with an active booking
    /// (Pending, Accepted or In Progress) cannot be requested, so no customer is left without their provider.
    public static string BookedMessage(string dateLabel, int bookingId, string status) =>
        $"{dateLabel}: you already have booking #{bookingId} ({status}) on this day. A day with an active booking cannot be marked unavailable.";

    public static DateOnly LastAllowed(DateOnly today) => new(today.Year, 12, 31);
    public static DateOnly FirstOfYear(DateOnly today) => new(today.Year, 1, 1);
}

/// How days off are written on the pages and in the admin log.
public static class DayOffFormat
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    /// "Nov 20, 2026"
    public static string Date(DateOnly d) => d.ToString("MMM d, yyyy", En);

    /// "Nov 20, 2026" (one day), "Nov 20 to Nov 22, 2026", "Dec 30, 2026 to Jan 2, 2027".
    public static string Range(DateOnly first, DateOnly last) =>
        first == last ? Date(first)
        : first.Year == last.Year ? $"{first.ToString("MMM d", En)} to {Date(last)}"
        : $"{Date(first)} to {Date(last)}";

    /// "1 day", "3 days"
    public static string Days(int n) => n == 1 ? "1 day" : $"{n} days";
}

/// Day numbers follow the database: 0 = Sunday ... 6 = Saturday. The page lists Monday first.
public static class AvailabilityDays
{
    public static readonly string[] Names = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
    public static readonly int[] DisplayOrder = { 1, 2, 3, 4, 5, 6, 0 };
}

public class BreakForm
{
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}

public class DayScheduleForm
{
    public int DayOfWeek { get; set; }
    public bool IsAvailable { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    public List<BreakForm> Breaks { get; set; } = new();
}

/// "Request Day Off": the first and last day (one day: the last day can be left empty) and the reason.
/// There is no provider id here on purpose: the provider is always the signed-in user.
public class DayOffRequestForm
{
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public string? Reason { get; set; }
}

/// One line of the provider's "Unavailable Dates" list: a request (1-7 days) or a single day without a request
/// (an older day off, or an extra day an administrator gave).
public sealed record DayOffItem(
    int? RequestId,             // set for a request
    int? UnavailableId,         // set for a single day without a request
    string DatesText,           // "Nov 20 to Nov 22, 2026"
    string DaysText,            // "3 days"
    string? Reason,
    string? Badge,              // "Pending Admin Approval", "Approved" or null
    string BadgeTone,           // "pending", "completed"
    string? Note,               // "Extra day off given by an administrator"
    bool IsPending,
    string RemoveLabel);        // "Cancel request" or "Remove"

/// Everything the page posts back when "Save Schedule" is pressed.
/// There is no provider id here on purpose: the provider is always the signed-in user.
public class AvailabilityForm
{
    public List<DayScheduleForm> Days { get; set; } = new();
    public int MinNoticeDays { get; set; } = 1;
    public int MaxJobsPerDay { get; set; } = 5;
}

public class AvailabilityPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Provider" };
    public AvailabilityForm Form { get; set; } = new();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    /// True when the provider has never saved a schedule: the page shows a suggested Mon-Fri 8-5 week.
    public bool IsSuggested { get; set; }

    public DateOnly Today { get; set; }

    /// The last date that can be requested: December 31 of this year.
    public DateOnly LastDate => UnavailableDateRules.LastAllowed(Today);

    /// The first date that can be requested: tomorrow (no same-day requests).
    public DateOnly FirstDate => UnavailableDateRules.FirstAllowed(Today);

    /// The provider's days off and requests from today on, oldest first.
    public IReadOnlyList<DayOffItem> DaysOff { get; set; } = Array.Empty<DayOffItem>();

    /// True while one request waits for an administrator (only one at a time): "Request Day Off" explains why it waits.
    public bool HasPendingRequest { get; set; }

    /// Days from today to December 31 that ANOTHER provider already has off or requested (only one provider per day).
    /// The page uses them to warn right away; the server checks again.
    public IReadOnlyList<DateOnly> TakenDates { get; set; } = Array.Empty<DateOnly>();

    /// The "Request Day Off" form that was refused: shown again (with DayOffErrors) so nothing has to be typed twice.
    public DayOffRequestForm? DayOffForm { get; set; }
    public IReadOnlyList<string> DayOffErrors { get; set; } = Array.Empty<string>();

    public IReadOnlyList<(int Value, string Label)> NoticeOptions { get; set; } = Array.Empty<(int, string)>();

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }
}

public sealed record AvailabilityPageResult(ProviderDashboardAccess Access, AvailabilityPageViewModel? Page);

public sealed record AvailabilitySaveResult(ProviderDashboardAccess Access, bool Saved, IReadOnlyList<string> Errors, string Message);

/// The answer to "Request Day Off", "Cancel request" and "Remove". Errors lists every problem of a refused request.
public sealed record DayOffResult(ProviderDashboardAccess Access, bool Done, string Message, IReadOnlyList<string> Errors)
{
    public static DayOffResult Denied(ProviderDashboardAccess access) => new(access, false, string.Empty, Array.Empty<string>());
    public static DayOffResult Ok(string message) => new(ProviderDashboardAccess.Allowed, true, message, Array.Empty<string>());
    public static DayOffResult Refused(string message, IReadOnlyList<string>? errors = null) =>
        new(ProviderDashboardAccess.Allowed, false, message, errors ?? new[] { message });
}
