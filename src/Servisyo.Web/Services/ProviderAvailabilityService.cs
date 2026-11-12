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

    /// Validates and saves weekly hours, breaks and booking settings in one save. (Days off have their own requests.)
    Task<AvailabilitySaveResult> SaveAsync(int userId, AvailabilityForm form, CancellationToken ct = default);

    /// "Request Day Off" (database v2.7): 1 to 7 days in a row from tomorrow to December 31, saved as a Pending request
    /// whose days hold the dates (one provider per date) without stopping bookings until an administrator approves.
    Task<DayOffResult> RequestDayOffAsync(int userId, DayOffRequestForm form, CancellationToken ct = default);

    /// Cancels the provider's OWN request (Pending) or removes its days from today on (Approved): the dates are free again.
    Task<DayOffResult> RemoveRequestAsync(int userId, int requestId, CancellationToken ct = default);

    /// Removes one of the provider's OWN days off that has no request (an older day, or an administrator's extra day).
    Task<DayOffResult> RemoveDayAsync(int userId, int unavailableId, CancellationToken ct = default);
}

public class ProviderAvailabilityService : IProviderAvailabilityService
{
    public const int MaxBreaksPerDay = BreakRules.MaxBreaksPerDay;
    public const int MaxNoticeDays = 30;
    public const int MaxJobsLimit = ProviderLimits.MaxJobsPerDay;      // 20 (database v2.6)
    public const int MaxReasonLength = 255;

    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);
    private static readonly TimeOnly DefaultStart = new(8, 0);
    private static readonly TimeOnly DefaultEnd = new(17, 0);
    private static readonly int[] NoticeChoices = { 0, 1, 2, 3, 5, 7, 14 };

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProviderAvailabilityService> _logger;
    private readonly IDayOffLiveUpdates? _live;

    public ProviderAvailabilityService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<ProviderAvailabilityService>? logger = null,
        IDayOffLiveUpdates? live = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderAvailabilityService>.Instance;
        _live = live;
    }

    /// The administrators' open pages (and this provider's other tabs) show the change at once.
    private Task PushAsync(int providerId, int providerUserId, string what) =>
        _live?.DayOffChangedAsync(providerId, providerUserId, what) ?? Task.CompletedTask;

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
        }
        else
        {
            // a rejected form: show the days in the usual order even if the browser sent them differently
            form.Days = form.Days.OrderBy(d => Array.IndexOf(AvailabilityDays.DisplayOrder, d.DayOfWeek)).ToList();
        }

        var daysOff = await DaysOffAsync(who.ProviderId, today, ct);
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
            DaysOff = daysOff,
            HasPendingRequest = daysOff.Any(d => d.IsPending),
            TakenDates = takenDates,
            NoticeOptions = options.Select(v => (v, v == 0 ? "Same day" : Plural.Of(v, "day"))).ToList()
        });
    }

    // ----------------------------------------------------------------- write

    public async Task<AvailabilitySaveResult> SaveAsync(int userId, AvailabilityForm form, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, false, Array.Empty<string>(), string.Empty);

        var errors = new List<string>();
        var days = ValidateDays(form, errors);

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

            // ---- booking settings ----
            provider.MinNoticeDays = form.MinNoticeDays;
            provider.MaxJobsPerDay = form.MaxJobsPerDay;

            await _db.SaveChangesAsync(ct);          // everything above is one database transaction
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Saving the schedule of provider {ProviderId} failed.", who.ProviderId);
            return new(ProviderDashboardAccess.Allowed, false,
                new[] { "Nothing was saved because the database refused the change. Please reload the page and try again." },
                "Your schedule was not saved.");
        }

        _logger.LogInformation("Provider {ProviderId} saved their schedule ({Open} open days).", who.ProviderId, days.Count(d => d.IsAvailable));
        return new(ProviderDashboardAccess.Allowed, true, Array.Empty<string>(), "Your schedule was saved.");
    }

    // ------------------------------------------------------------ day-off requests (database v2.7)

    public async Task<DayOffResult> RequestDayOffAsync(int userId, DayOffRequestForm form, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return DayOffResult.Denied(access);

        var today = TodayInCebu();

        // 1. the form on its own: dates, at most 7 days, the reason
        var errors = new List<string>();
        var reason = string.IsNullOrWhiteSpace(form.Reason) ? null : form.Reason.Trim();
        if (form.Start is null)
            return DayOffResult.Refused("Please choose the first day off.");
        var first = form.Start.Value;
        var last = form.End ?? first;                       // one day: the last day can be left empty
        var firstLabel = DayOffFormat.Date(first);

        if (last < first)
            errors.Add("The last day off cannot be before the first day.");
        else if (first < today)
            errors.Add($"{firstLabel} is in the past. Choose tomorrow or a later date.");
        else if (first == today)
            errors.Add($"{firstLabel}: {UnavailableDateRules.SameDayMessage} Unavailable dates must be marked at least 1 day in advance.");
        else if (last > UnavailableDateRules.LastAllowed(today))
            errors.Add($"{DayOffFormat.Date(last)} is not in {today.Year}. You can only request days off until December 31, {today.Year}.");
        else if (last.DayNumber - first.DayNumber + 1 > UnavailableDateRules.MaxDaysPerRequest)
            errors.Add(UnavailableDateRules.MaxDaysMessage);
        if (reason is { Length: > MaxReasonLength })
            errors.Add($"The reason can be at most {MaxReasonLength} characters.");
        if (errors.Count > 0)
            return DayOffResult.Refused(errors[0], errors);

        var dates = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(first.AddDays).ToList();
        var rangeText = DayOffFormat.Range(first, last);

        // Serializable: the checks below and the save are ONE step, so another provider cannot take the same dates
        // and a customer cannot book one of them in between (the unique index on the date is the last guard).
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        // 2. only one request at a time may wait for an administrator
        var waiting = await PendingRequestAsync(who.ProviderId, today, ct);
        if (waiting is not null)
            return DayOffResult.Refused($"Your request for {waiting} is still waiting for an administrator. You can send a new request when it is answered, or cancel it first.");

        // 3. every day: not already off for this provider, not taken by another provider, no active booking
        var mine = (await _db.ProviderUnavailableDates.AsNoTracking()
                .Where(d => d.ProviderId == who.ProviderId && dates.Contains(d.UnavailableDate))
                .Select(d => d.UnavailableDate).ToListAsync(ct))
            .ToHashSet();
        var taken = await TakenByOthersAsync(who.ProviderId, dates, ct);
        var booked = await ActiveBookingDaysAsync(who.ProviderId, dates, ct);
        foreach (var day in dates)
        {
            var label = DayOffFormat.Date(day);
            if (mine.Contains(day)) errors.Add($"{label}: you already have this day off (or asked for it).");
            else if (taken.Contains(day)) errors.Add($"{label}: {UnavailableDateRules.TakenMessage}");
            else if (booked.TryGetValue(day, out var job)) errors.Add(UnavailableDateRules.BookedMessage(label, job.BookingId, job.Status));
        }
        if (errors.Count > 0)
            return DayOffResult.Refused($"Your day off request for {rangeText} was not sent.", errors);

        // 4. the request and its days: one SaveChanges = one database transaction
        var request = new ProviderDayOffRequest
        {
            ProviderId = who.ProviderId, Reason = reason, Status = DayOffStatuses.Pending, RequestedAt = _clock.GetUtcNow().UtcDateTime
        };
        foreach (var day in dates)
            request.Days.Add(new ProviderUnavailableDate { ProviderId = who.ProviderId, UnavailableDate = day });
        _db.DayOffRequests.Add(request);

        try
        {
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // most likely another provider took one of these days at the same moment (only one provider per day -
            // IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE): nothing was saved
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "The day off request of provider {ProviderId} for {Dates} was not saved.", who.ProviderId, rangeText);
            return DayOffResult.Refused("Nothing was sent: another provider may have just taken one of these days (only one provider can be off per day). Please reload the page and try again.");
        }

        _logger.LogInformation("Provider {ProviderId} requested {Days} off: {Dates} (request {RequestId}).",
            who.ProviderId, DayOffFormat.Days(dates.Count), rangeText, request.RequestId);
        await PushAsync(who.ProviderId, userId, "requested");
        return DayOffResult.Ok($"Your day off request for {rangeText} ({DayOffFormat.Days(dates.Count)}) was sent. Customers can still book you on those days until an administrator approves it.");
    }

    public async Task<DayOffResult> RemoveRequestAsync(int userId, int requestId, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return DayOffResult.Denied(access);

        var today = TodayInCebu();

        // only the provider's OWN request (the provider comes from the sign-in, never from the browser)
        var request = await _db.DayOffRequests.Include(r => r.Days)
            .FirstOrDefaultAsync(r => r.RequestId == requestId && r.ProviderId == who.ProviderId, ct);
        if (request is null || request.Status == DayOffStatuses.Rejected || request.Days.Count == 0)
            return DayOffResult.Refused("That day off request was not found. Please reload the page.");

        var rangeText = DayOffFormat.Range(request.Days.Min(d => d.UnavailableDate), request.Days.Max(d => d.UnavailableDate));
        string message;
        if (request.Status == DayOffStatuses.Pending)
        {
            // never decided: it goes completely
            _db.ProviderUnavailableDates.RemoveRange(request.Days);
            _db.DayOffRequests.Remove(request);
            message = $"Your day off request for {rangeText} was cancelled.";
        }
        else
        {
            // approved: the days from today on are free again; days that already passed stay as history
            var upcoming = request.Days.Where(d => d.UnavailableDate >= today).ToList();
            if (upcoming.Count == 0)
                return DayOffResult.Refused($"Your days off for {rangeText} have already passed.");
            _db.ProviderUnavailableDates.RemoveRange(upcoming);
            if (upcoming.Count == request.Days.Count) _db.DayOffRequests.Remove(request);
            message = $"Your days off for {rangeText} were removed. Customers can book you on those days again.";
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Provider {ProviderId} removed day off request {RequestId} ({Status}).", who.ProviderId, requestId, request.Status);
        await PushAsync(who.ProviderId, userId, request.Status == DayOffStatuses.Pending ? "cancelled" : "removed");
        return DayOffResult.Ok(message);
    }

    public async Task<DayOffResult> RemoveDayAsync(int userId, int unavailableId, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return DayOffResult.Denied(access);

        var today = TodayInCebu();
        var day = await _db.ProviderUnavailableDates
            .FirstOrDefaultAsync(d => d.UnavailableId == unavailableId && d.ProviderId == who.ProviderId && d.RequestId == null, ct);
        if (day is null || day.UnavailableDate < today)
            return DayOffResult.Refused("That day off was not found. Please reload the page.");

        _db.ProviderUnavailableDates.Remove(day);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Provider {ProviderId} removed the day off {Date}.", who.ProviderId, day.UnavailableDate);
        await PushAsync(who.ProviderId, userId, "removed");
        return DayOffResult.Ok($"{DayOffFormat.Date(day.UnavailableDate)} was removed. Customers can book you on that day again.");
    }

    // ------------------------------------------------------------------ helpers

    /// The provider's list: each request (whose last day is today or later) as one line, and each day without a request.
    private async Task<IReadOnlyList<DayOffItem>> DaysOffAsync(int providerId, DateOnly today, CancellationToken ct)
    {
        var rows = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(d => d.ProviderId == providerId)
            .Select(d => new
            {
                d.UnavailableId, d.UnavailableDate, d.Reason, d.RequestId,
                Status = d.Request != null ? d.Request.Status : null,
                RequestReason = d.Request != null ? d.Request.Reason : null,
                ByAdmin = d.ApprovedByActionId != null
            })
            .ToListAsync(ct);

        var items = new List<(DateOnly First, DayOffItem Item)>();
        foreach (var g in rows.Where(r => r.RequestId != null).GroupBy(r => r.RequestId!.Value))
        {
            var first = g.Min(r => r.UnavailableDate);
            var last = g.Max(r => r.UnavailableDate);
            if (last < today) continue;
            var pending = g.First().Status == DayOffStatuses.Pending;
            items.Add((first, new DayOffItem(g.Key, null, DayOffFormat.Range(first, last), DayOffFormat.Days(g.Count()), g.First().RequestReason,
                pending ? UnavailableDateRules.PendingLabel : UnavailableDateRules.ApprovedLabel, pending ? "pending" : "completed",
                null, pending, pending ? "Cancel request" : "Remove")));
        }
        foreach (var r in rows.Where(r => r.RequestId == null && r.UnavailableDate >= today))
        {
            items.Add((r.UnavailableDate, new DayOffItem(null, r.UnavailableId, DayOffFormat.Date(r.UnavailableDate), DayOffFormat.Days(1), r.Reason,
                null, "muted", r.ByAdmin ? UnavailableDateRules.ApprovedNote : null, false, "Remove")));
        }
        return items.OrderBy(i => i.First).Select(i => i.Item).ToList();
    }

    /// "Nov 20 to Nov 22, 2026" when one of the provider's requests still waits for an administrator (and is not over).
    private async Task<string?> PendingRequestAsync(int providerId, DateOnly today, CancellationToken ct)
    {
        var days = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(d => d.ProviderId == providerId && d.RequestId != null && d.Request!.Status == DayOffStatuses.Pending)
            .Select(d => new { d.RequestId, d.UnavailableDate })
            .ToListAsync(ct);
        var waiting = days.GroupBy(d => d.RequestId)
            .Select(g => new { First = g.Min(d => d.UnavailableDate), Last = g.Max(d => d.UnavailableDate) })
            .Where(r => r.Last >= today)
            .OrderBy(r => r.First)
            .FirstOrDefault();
        return waiting is null ? null : DayOffFormat.Range(waiting.First, waiting.Last);
    }

    /// Which of these days ANOTHER provider already has off or asked for (only one provider can be off per day).
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
}
