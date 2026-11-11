using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

/// The unavailable-date rules: only dates from TOMORROW up to December 31 of THIS year (Philippine time) - no
/// same-day requests -, at most 5 per calendar month (days of this month that already passed count too). On January 1
/// last year's dates are deleted (UnavailableDatesResetService), so every year starts with an empty list.
/// (An administrator may still approve an extra day off for today: that is the emergency path.)
public static class UnavailableDateRules
{
    public const string SameDayMessage = "Same-day unavailable date selection is not permitted.";

    /// Database v2.5: only ONE provider can be off on any date (first come, first served), so customers always find
    /// someone. The other provider is not named to providers (an administrator sees the name).
    public const string TakenMessage = "Another provider already has this day off. Only one provider can be off per day, so please choose another date.";
    public const string SameDayAdvice = "Same-day off requests are not allowed. Unavailable dates must be marked at least 1 day in advance.";

    /// The first day a provider can mark: tomorrow (Philippine time).
    public static DateOnly FirstAllowed(DateOnly today) => today.AddDays(1);

    public const int MaxPerMonth = 5;
    public const string MonthlyLimitMessage = "You can only set up to 5 unavailable days per month.";
    public const string ApprovedNote = "Extra day approved by an administrator (does not count toward your 5)";

    /// "Nov 20, 2026: you already have booking #12 (Accepted) on this day. ..." - a day with an active booking
    /// (Pending, Accepted or In Progress) cannot be marked unavailable, so no customer is left without their provider.
    public static string BookedMessage(string dateLabel, int bookingId, string status) =>
        $"{dateLabel}: you already have booking #{bookingId} ({status}) on this day. A day with an active booking cannot be marked unavailable.";

    public static DateOnly LastAllowed(DateOnly today) => new(today.Year, 12, 31);
    public static DateOnly FirstOfYear(DateOnly today) => new(today.Year, 1, 1);
    public static DateOnly FirstOfMonth(DateOnly today) => new(today.Year, today.Month, 1);
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

public class UnavailableDateForm
{
    public DateOnly? Date { get; set; }
    public string? Reason { get; set; }

    /// Shown only: an extra day approved by an administrator (PROVIDER_UNAVAILABLE_DATES.APPROVED_BY_ACTION_ID).
    /// Never taken from the browser - the server always looks it up in the database.
    [BindNever]
    public bool ApprovedByAdmin { get; set; }

    /// Shown only: this day off was already saved (e.g. today's, saved before today). The same-day rule is only for
    /// NEW days, so such a day can stay. Looked up in the database, never taken from the browser.
    [BindNever]
    public bool AlreadySaved { get; set; }
}

/// Everything the page posts back when "Save Schedule" is pressed.
/// There is no provider id here on purpose: the provider is always the signed-in user.
public class AvailabilityForm
{
    public List<DayScheduleForm> Days { get; set; } = new();
    public List<UnavailableDateForm> Dates { get; set; } = new();
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

    /// The last date that can be marked unavailable: December 31 of this year.
    public DateOnly LastDate => UnavailableDateRules.LastAllowed(Today);

    /// The first date that can be marked unavailable: tomorrow (no same-day requests).
    public DateOnly FirstDate => UnavailableDateRules.FirstAllowed(Today);

    /// Unavailable days of THIS month that already passed (not listed, but they count toward the 5 per month).
    public int PastDaysThisMonth { get; set; }

    /// Days from today to December 31 that ANOTHER provider already has off (only one provider per day).
    /// The page uses them to warn right away; the server checks again when saving.
    public IReadOnlyList<DateOnly> TakenDates { get; set; } = Array.Empty<DateOnly>();

    public IReadOnlyList<(int Value, string Label)> NoticeOptions { get; set; } = Array.Empty<(int, string)>();

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }
}

public sealed record AvailabilityPageResult(ProviderDashboardAccess Access, AvailabilityPageViewModel? Page);

public sealed record AvailabilitySaveResult(ProviderDashboardAccess Access, bool Saved, IReadOnlyList<string> Errors, string Message);
