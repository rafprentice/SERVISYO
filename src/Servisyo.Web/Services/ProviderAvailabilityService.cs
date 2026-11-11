using System.Data;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IProviderAvailabilityService
{
    /// Builds the page for the signed-in provider. When `posted` is given (the form was rejected) the page shows those values instead of the saved ones.
    Task<AvailabilityPageResult> GetAsync(int userId, AvailabilityForm? posted = null, CancellationToken ct = default);

    /// Validates and saves weekly hours, breaks, unavailable dates and booking settings in one transaction.
    Task<AvailabilitySaveResult> SaveAsync(int userId, AvailabilityForm form, CancellationToken ct = default);
}

public class ProviderAvailabilityService : IProviderAvailabilityService
{
    public const int MaxBreaksPerDay = BreakRules.MaxBreaksPerDay;
    public const int MaxUnavailableDates = 100;
    public const int MaxNoticeDays = 30;
    public const int MaxJobsLimit = 50;
    public const int MaxReasonLength = 255;

    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);
    private static readonly TimeOnly DefaultStart = new(8, 0);
    private static readonly TimeOnly DefaultEnd = new(17, 0);
    private static readonly int[] NoticeChoices = { 0, 1, 2, 3, 5, 7, 14 };

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProviderAvailabilityService> _logger;

    public ProviderAvailabilityService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<ProviderAvailabilityService>? logger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderAvailabilityService>.Instance;
    }

    private DateOnly TodayInCebu() => DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(ManilaOffset).DateTime);

    private sealed record Who(int ProviderId, string ApprovalStatus, string FullName, string BusinessName, int MinNoticeDays, int MaxJobsPerDay);

    /// The provider is found from the SIGNED-IN user id only (never from the browser).
    private async Task<(ProviderDashboardAccess Access, Who? Who)> FindAsync(int userId, CancellationToken ct)
    {
        var p = await _db.Providers.AsNoTracking()
            .Where(x => x.UserId == userId
                     && x.User.Role == Roles.ServiceProvider
                     && x.User.AccountStatus == AccountStatuses.Active)
            .Select(x => new { x.ProviderId, x.ApprovalStatus, x.User.FirstName, x.User.LastName, x.BusinessName, x.MinNoticeDays, x.MaxJobsPerDay })
            .FirstOrDefaultAsync(ct);

        if (p is null) return (ProviderDashboardAccess.NotAllowed, null);
        if (p.ApprovalStatus == ApprovalStatuses.Pending) return (ProviderDashboardAccess.Pending, null);
        if (p.ApprovalStatus != ApprovalStatuses.Approved) return (ProviderDashboardAccess.Rejected, null);

        return (ProviderDashboardAccess.Allowed,
            new Who(p.ProviderId, p.ApprovalStatus, $"{p.FirstName} {p.LastName}".Trim(), p.BusinessName, p.MinNoticeDays, p.MaxJobsPerDay));
    }

    // ------------------------------------------------------------------ read

    public async Task<AvailabilityPageResult> GetAsync(int userId, AvailabilityForm? posted = null, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, null);

        var today = TodayInCebu();
        var form = posted is { Days.Count: 7 } ? posted : null;    // an incomplete post is replaced by the saved schedule
        var suggested = false;

        if (form is null)
        {
            var rows = await _db.ProviderAvailabilities.AsNoTracking()
                .Where(a => a.ProviderId == who.ProviderId)
                .Select(a => new
                {
                    a.DayOfWeek, a.IsAvailable, a.StartTime, a.EndTime,
                    Breaks = a.Breaks.OrderBy(b => b.BreakStart).Select(b => new { b.BreakStart, b.BreakEnd }).ToList()
                })
                .ToListAsync(ct);

            suggested = rows.Count == 0;

            form = new AvailabilityForm { MinNoticeDays = who.MinNoticeDays, MaxJobsPerDay = who.MaxJobsPerDay };
            foreach (var dow in AvailabilityDays.DisplayOrder)
            {
                var row = rows.FirstOrDefault(r => r.DayOfWeek == dow);
                var day = new DayScheduleForm { DayOfWeek = dow };
                if (row is not null)
                {
                    day.IsAvailable = row.IsAvailable;
                    day.Start = row.StartTime;
                    day.End = row.EndTime;
                    day.Breaks = row.Breaks.Select(b => new BreakForm { Start = b.BreakStart, End = b.BreakEnd }).ToList();
                }
                else
                {
                    // never saved yet: suggest Monday-Friday, 8:00 am - 5:00 pm (nothing is stored until "Save Schedule")
                    day.IsAvailable = dow is >= 1 and <= 5;
                    day.Start = DefaultStart;
                    day.End = DefaultEnd;
                }
                form.Days.Add(day);
            }

            form.Dates = (await _db.ProviderUnavailableDates.AsNoTracking()
                    .Where(d => d.ProviderId == who.ProviderId && d.UnavailableDate >= today)
                    .OrderBy(d => d.UnavailableDate)
                    .Select(d => new { d.UnavailableDate, d.Reason, Approved = d.ApprovedByActionId != null })
                    .ToListAsync(ct))
                .Select(d => new UnavailableDateForm { Date = d.UnavailableDate, Reason = d.Reason, ApprovedByAdmin = d.Approved, AlreadySaved = true })
                .ToList();
        }
        else
        {
            // a rejected form: show the days in the usual order even if the browser sent them differently
            form.Days = form.Days.OrderBy(d => Array.IndexOf(AvailabilityDays.DisplayOrder, d.DayOfWeek)).ToList();

            // which listed days were saved before / approved by an administrator comes from the database, never from the browser
            var savedDays = await SavedDatesAsync(who.ProviderId, today, ct);
            foreach (var d in form.Dates)
            {
                d.AlreadySaved = d.Date is { } day && savedDays.ContainsKey(day);
                d.ApprovedByAdmin = d.Date is { } approvedDay && savedDays.TryGetValue(approvedDay, out var approved) && approved;
            }
        }

        var pastThisMonth = await CountPastDaysThisMonthAsync(who.ProviderId, today, ct);
        var lastDay = UnavailableDateRules.LastAllowed(today);
        var takenDates = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(d => d.ProviderId != who.ProviderId && d.UnavailableDate >= today && d.UnavailableDate <= lastDay)
            .Select(d => d.UnavailableDate)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

        var options = NoticeChoices.Contains(form.MinNoticeDays) ? NoticeChoices : NoticeChoices.Append(form.MinNoticeDays).OrderBy(x => x).ToArray();

        var fullName = who.FullName;
        return new(ProviderDashboardAccess.Allowed, new AvailabilityPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Provider",
                FirstName = fullName.Split(' ')[0],
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                Subtitle = who.BusinessName,
                UnreadNotifications = await _db.CountUnreadNotificationsAsync(userId, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            Form = form,
            IsSuggested = suggested,
            Today = today,
            PastDaysThisMonth = pastThisMonth,
            TakenDates = takenDates,
            NoticeOptions = options.Select(v => (v, v == 0 ? "Same day" : Plural.Of(v, "day"))).ToList()
        });
    }

    // ----------------------------------------------------------------- write

    public async Task<AvailabilitySaveResult> SaveAsync(int userId, AvailabilityForm form, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, false, Array.Empty<string>(), string.Empty);

        var today = TodayInCebu();

        // Serializable: "no active booking on this day" is checked and the day off is saved as ONE step, so a customer
        // cannot book the provider for that day in between (BookProviderService checks the other way round the same way).
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        var errors = new List<string>();
        var days = ValidateDays(form, errors);
        var saved = await SavedDatesAsync(who.ProviderId, today, ct);
        var newDays = form.Dates.Where(d => d.Date is { } day && day >= today && !saved.ContainsKey(day)).Select(d => d.Date!.Value).Distinct().ToList();
        var booked = await ActiveBookingDaysAsync(who.ProviderId, newDays, ct);
        var taken = await TakenByOthersAsync(who.ProviderId, newDays, ct);
        var dates = ValidateDates(form, today, await CountPastDaysThisMonthAsync(who.ProviderId, today, ct), saved, booked, taken, errors);

        if (form.MinNoticeDays < 0 || form.MinNoticeDays > MaxNoticeDays)
            errors.Add($"Minimum booking notice must be between 0 and {MaxNoticeDays} days.");
        if (form.MaxJobsPerDay < 1 || form.MaxJobsPerDay > MaxJobsLimit)
            errors.Add($"Maximum jobs per day must be between 1 and {MaxJobsLimit}.");

        if (errors.Count > 0)
            return new(ProviderDashboardAccess.Allowed, false, errors, "Your schedule was not saved. Please fix the problems below.");

        try
        {
            var provider = await _db.Providers
                .Include(p => p.Availabilities).ThenInclude(a => a.Breaks)
                .Include(p => p.UnavailableDates)
                .FirstAsync(p => p.ProviderId == who.ProviderId, ct);

            // ---- weekly hours: one row per day (a unique index guarantees it), updated in place ----
            foreach (var d in days)
            {
                var row = provider.Availabilities.FirstOrDefault(a => a.DayOfWeek == d.DayOfWeek);
                if (row is null)
                {
                    row = new ProviderAvailability { ProviderId = provider.ProviderId, DayOfWeek = (byte)d.DayOfWeek };
                    provider.Availabilities.Add(row);
                }
                row.IsAvailable = d.IsAvailable;
                row.StartTime = d.Start;
                row.EndTime = d.End;

                _db.ProviderBreaks.RemoveRange(row.Breaks.ToList());           // breaks are replaced as a set
                foreach (var b in d.Breaks)
                    row.Breaks.Add(new ProviderBreak { BreakStart = b.Start, BreakEnd = b.End });
            }

            // ---- unavailable dates: only today and later are managed here; older rows are left alone ----
            var wanted = dates.ToDictionary(x => x.Date);
            foreach (var existing in provider.UnavailableDates.Where(x => x.UnavailableDate >= today).ToList())
            {
                if (wanted.TryGetValue(existing.UnavailableDate, out var keep))
                {
                    existing.Reason = keep.Reason;
                    wanted.Remove(existing.UnavailableDate);
                }
                else
                {
                    _db.ProviderUnavailableDates.Remove(existing);
                }
            }
            foreach (var add in wanted.Values)
                provider.UnavailableDates.Add(new ProviderUnavailableDate { ProviderId = provider.ProviderId, UnavailableDate = add.Date, Reason = add.Reason });

            // ---- booking settings ----
            provider.MinNoticeDays = form.MinNoticeDays;
            provider.MaxJobsPerDay = form.MaxJobsPerDay;

            await _db.SaveChangesAsync(ct);          // everything above is one database transaction
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // most likely another provider took one of these days off at the same moment (the database allows only
            // one provider per day - IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE): nothing was saved
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Saving the schedule of provider {ProviderId} failed.", who.ProviderId);
            return new(ProviderDashboardAccess.Allowed, false,
                new[] { "Nothing was saved: another provider may have just taken one of your new days off (only one provider can be off per day). Please reload the page, check your dates and save again." },
                "Your schedule was not saved.");
        }

        _logger.LogInformation("Provider {ProviderId} saved their schedule ({Open} open days, {Dates} unavailable dates).",
            who.ProviderId, days.Count(d => d.IsAvailable), dates.Count);
        return new(ProviderDashboardAccess.Allowed, true, Array.Empty<string>(), "Your schedule was saved.");
    }

    /// The provider's OWN unavailable days of this month before today (extra days approved by an administrator do not
    /// count): they are not on the page any more but count toward the 5 per month.
    private Task<int> CountPastDaysThisMonthAsync(int providerId, DateOnly today, CancellationToken ct)
    {
        var firstOfMonth = UnavailableDateRules.FirstOfMonth(today);
        return _db.ProviderUnavailableDates.CountAsync(d => d.ProviderId == providerId && d.UnavailableDate >= firstOfMonth
                                                          && d.UnavailableDate < today && d.ApprovedByActionId == null, ct);
    }

    /// The saved days off from today on: date -> true when an administrator approved it as an extra day.
    private async Task<Dictionary<DateOnly, bool>> SavedDatesAsync(int providerId, DateOnly today, CancellationToken ct) =>
        (await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(d => d.ProviderId == providerId && d.UnavailableDate >= today)
            .Select(d => new { d.UnavailableDate, Approved = d.ApprovedByActionId != null })
            .ToListAsync(ct))
        .ToDictionary(d => d.UnavailableDate, d => d.Approved);

    /// Which of these days ANOTHER provider already has off (only one provider can be off per day).
    private async Task<HashSet<DateOnly>> TakenByOthersAsync(int providerId, List<DateOnly> days, CancellationToken ct)
    {
        if (days.Count == 0) return new();
        return (await _db.ProviderUnavailableDates.AsNoTracking()
                .Where(d => d.ProviderId != providerId && days.Contains(d.UnavailableDate))
                .Select(d => d.UnavailableDate)
                .ToListAsync(ct))
            .ToHashSet();
    }

    /// For each of these days: the provider's first active booking on it (Pending, Accepted or In Progress), if any.
    private async Task<Dictionary<DateOnly, (int BookingId, string Status)>> ActiveBookingDaysAsync(int providerId, List<DateOnly> days, CancellationToken ct)
    {
        if (days.Count == 0) return new();
        var rows = await _db.Bookings.AsNoTracking()
            .Where(b => b.ProviderId == providerId && days.Contains(b.BookingDate) && ActiveBookingRules.Statuses.Contains(b.Status))
            .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime)
            .Select(b => new { b.BookingDate, b.BookingId, b.Status })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.BookingDate).ToDictionary(g => g.Key, g => (g.First().BookingId, g.First().Status));
    }

    // ------------------------------------------------------------ validation

    private sealed record CleanDay(int DayOfWeek, bool IsAvailable, TimeOnly Start, TimeOnly End, List<(TimeOnly Start, TimeOnly End)> Breaks);
    private sealed record CleanDate(DateOnly Date, string? Reason);

    private static List<CleanDay> ValidateDays(AvailabilityForm form, List<string> errors)
    {
        var result = new List<CleanDay>();

        var distinct = form.Days.Select(d => d.DayOfWeek).Distinct().ToList();
        if (form.Days.Count != 7 || distinct.Count != 7 || distinct.Any(d => d < 0 || d > 6))
        {
            errors.Add("The weekly schedule was incomplete. Please reload the page and try again.");
            return result;
        }

        foreach (var dow in AvailabilityDays.DisplayOrder)
        {
            var d = form.Days.Single(x => x.DayOfWeek == dow);
            var name = AvailabilityDays.Names[dow];

            if (!d.IsAvailable)
            {
                // a closed day keeps its hours if they make sense, otherwise the default ones; its breaks are dropped
                var ok = d.Start is not null && d.End is not null && d.Start < d.End;
                result.Add(new(dow, false, ok ? d.Start!.Value : DefaultStart, ok ? d.End!.Value : DefaultEnd, new()));
                continue;
            }

            if (d.Start is null || d.End is null)
            {
                errors.Add($"{name}: please enter a start time and an end time.");
                continue;
            }
            if (d.End <= d.Start)
            {
                errors.Add($"{name}: the end time must be after the start time.");
                continue;
            }

            var start = d.Start.Value;
            var end = d.End.Value;
            var breaks = new List<(TimeOnly Start, TimeOnly End)>();
            var ownErrors = 0;

            // ---- the break: one, exactly 1 hour, only on a shift of 8 hours or more (BreakRules) ----
            // The page sends only the start (the end is always start + 1 hour); an end that is sent must agree.
            var wanted = d.Breaks.Where(x => x.Start is not null || x.End is not null).ToList();    // empty rows are ignored
            if (wanted.Count > 0)
            {
                var b = wanted[0];
                if (!BreakRules.AllowsBreak(start, end))
                {
                    errors.Add($"{name}: {BreakRules.ShortShiftMessage}"); ownErrors++;
                }
                else if (wanted.Count > MaxBreaksPerDay)
                {
                    errors.Add($"{name}: you can add only one 1-hour break."); ownErrors++;
                }
                else if (b.Start is null)
                {
                    errors.Add($"{name}: choose when your break starts."); ownErrors++;
                }
                else if (b.End is not null && BreakRules.Minutes(b.End.Value) - BreakRules.Minutes(b.Start.Value) != BreakRules.BreakMinutes)
                {
                    errors.Add($"{name}: a break must be exactly 1 hour long."); ownErrors++;
                }
                else if (!BreakRules.Fits(b.Start.Value, start, end))
                {
                    errors.Add($"{name}: the break must start between {BreakRules.Clock(start)} and {BreakRules.Clock(BreakRules.LatestStart(end))}.");
                    ownErrors++;
                }
                else
                {
                    breaks.Add((b.Start.Value, BreakRules.EndOf(b.Start.Value)));
                }
            }

            if (ownErrors == 0) result.Add(new(dow, true, start, end, breaks));
        }

        return result;
    }

    /// saved: the days off already saved from today on (true = approved by an administrator: not counted in the 5).
    /// booked: NEW days that have an active booking (they are refused). Days that were already saved are left alone.
    /// taken: NEW days that another provider already has off (refused: only one provider per day).
    private static List<CleanDate> ValidateDates(AvailabilityForm form, DateOnly today, int pastDaysThisMonth,
        IReadOnlyDictionary<DateOnly, bool> saved, IReadOnlyDictionary<DateOnly, (int BookingId, string Status)> booked,
        IReadOnlySet<DateOnly> taken, List<string> errors)
    {
        var result = new List<CleanDate>();
        var seen = new HashSet<DateOnly>();
        var last = UnavailableDateRules.LastAllowed(today);

        foreach (var row in form.Dates)
        {
            var reason = string.IsNullOrWhiteSpace(row.Reason) ? null : row.Reason.Trim();

            if (row.Date is null)
            {
                if (reason is not null) errors.Add("Every unavailable date needs a date.");
                continue;                                               // an empty row: ignore it
            }

            var date = row.Date.Value;
            var label = date.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

            if (date < today) { errors.Add($"{label} is in the past. Choose tomorrow or a later date."); continue; }
            // no same-day requests: today is only allowed when it was already saved earlier (only NEW days are checked)
            if (date == today && !saved.ContainsKey(date))
            {
                errors.Add($"{label}: {UnavailableDateRules.SameDayMessage} Unavailable dates must be marked at least 1 day in advance.");
                continue;
            }
            if (date > last)
            {
                errors.Add($"{label} is not in {today.Year}. You can only mark unavailable days until December 31, {today.Year}.");
                continue;
            }
            if (!seen.Add(date)) { errors.Add($"{label} is listed more than once."); continue; }
            if (reason is { Length: > MaxReasonLength }) { errors.Add($"{label}: the reason can be at most {MaxReasonLength} characters."); continue; }
            if (taken.Contains(date)) { errors.Add($"{label}: {UnavailableDateRules.TakenMessage}"); continue; }
            if (booked.TryGetValue(date, out var job)) { errors.Add(UnavailableDateRules.BookedMessage(label, job.BookingId, job.Status)); continue; }

            result.Add(new(date, reason));
        }

        if (result.Count > MaxUnavailableDates)
            errors.Add($"You can list at most {MaxUnavailableDates} unavailable dates.");

        // ---- at most 5 per calendar month (this month's days that already passed count too;
        //      extra days approved by an administrator do not) ----
        foreach (var month in result.Where(x => !(saved.TryGetValue(x.Date, out var approved) && approved))
                                    .GroupBy(x => (x.Date.Year, x.Date.Month)).OrderBy(g => g.Key))
        {
            var past = month.Key == (today.Year, today.Month) ? pastDaysThisMonth : 0;
            var total = month.Count() + past;
            if (total <= UnavailableDateRules.MaxPerMonth) continue;

            var name = new DateOnly(month.Key.Year, month.Key.Month, 1).ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            errors.Add(past > 0
                ? $"{UnavailableDateRules.MonthlyLimitMessage} {name} would have {total} ({past} of them already passed)."
                : $"{UnavailableDateRules.MonthlyLimitMessage} {name} would have {total}.");
        }

        return result;
    }
}
