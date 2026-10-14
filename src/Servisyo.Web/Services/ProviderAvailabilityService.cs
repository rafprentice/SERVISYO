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
    public const int MaxBreaksPerDay = 5;
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
                    .Select(d => new { d.UnavailableDate, d.Reason })
                    .ToListAsync(ct))
                .Select(d => new UnavailableDateForm { Date = d.UnavailableDate, Reason = d.Reason })
                .ToList();
        }
        else
        {
            // a rejected form: show the days in the usual order even if the browser sent them differently
            form.Days = form.Days.OrderBy(d => Array.IndexOf(AvailabilityDays.DisplayOrder, d.DayOfWeek)).ToList();
        }

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
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            Form = form,
            IsSuggested = suggested,
            Today = today,
            NoticeOptions = options.Select(v => (v, v == 0 ? "Same day" : Plural.Of(v, "day"))).ToList()
        });
    }

    // ----------------------------------------------------------------- write

    public async Task<AvailabilitySaveResult> SaveAsync(int userId, AvailabilityForm form, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, false, Array.Empty<string>(), string.Empty);

        var today = TodayInCebu();
        var errors = new List<string>();
        var days = ValidateDays(form, errors);
        var dates = ValidateDates(form, today, errors);

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
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Saving the schedule of provider {ProviderId} failed.", who.ProviderId);
            return new(ProviderDashboardAccess.Allowed, false,
                new[] { "Something went wrong while saving. Nothing was changed. Please try again." },
                "Your schedule was not saved.");
        }

        _logger.LogInformation("Provider {ProviderId} saved their schedule ({Open} open days, {Dates} unavailable dates).",
            who.ProviderId, days.Count(d => d.IsAvailable), dates.Count);
        return new(ProviderDashboardAccess.Allowed, true, Array.Empty<string>(), "Your schedule was saved.");
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

            foreach (var b in d.Breaks)
            {
                if (b.Start is null && b.End is null) continue;          // an empty row: ignore it
                if (b.Start is null || b.End is null) { errors.Add($"{name}: every break needs a start time and an end time."); ownErrors++; continue; }
                if (b.End <= b.Start) { errors.Add($"{name}: a break must end after it starts."); ownErrors++; continue; }
                if (b.Start < start || b.End > end) { errors.Add($"{name}: breaks must be inside your working hours."); ownErrors++; continue; }
                breaks.Add((b.Start.Value, b.End.Value));
            }

            if (breaks.Count > MaxBreaksPerDay)
            {
                errors.Add($"{name}: you can add at most {MaxBreaksPerDay} breaks.");
                ownErrors++;
            }

            breaks = breaks.OrderBy(b => b.Start).ToList();
            for (var i = 1; i < breaks.Count; i++)
            {
                if (breaks[i].Start < breaks[i - 1].End)
                {
                    errors.Add($"{name}: breaks cannot overlap.");
                    ownErrors++;
                    break;
                }
            }

            if (ownErrors == 0) result.Add(new(dow, true, start, end, breaks));
        }

        return result;
    }

    private static List<CleanDate> ValidateDates(AvailabilityForm form, DateOnly today, List<string> errors)
    {
        var result = new List<CleanDate>();
        var seen = new HashSet<DateOnly>();

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

            if (date < today) { errors.Add($"{label} is in the past. Choose today or a later date."); continue; }
            if (!seen.Add(date)) { errors.Add($"{label} is listed more than once."); continue; }
            if (reason is { Length: > MaxReasonLength }) { errors.Add($"{label}: the reason can be at most {MaxReasonLength} characters."); continue; }

            result.Add(new(date, reason));
        }

        if (result.Count > MaxUnavailableDates)
            errors.Add($"You can list at most {MaxUnavailableDates} unavailable dates.");

        return result;
    }
}
