using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IProviderManagementService
{
    /// The provider cards for the admin Providers page. Returns null when the signed-in user is not an Active administrator.
    Task<ProvidersPageViewModel?> GetProvidersAsync(int adminUserId, ProviderListQuery query, CancellationToken ct = default);

    /// Everything the "View Details" pop-up shows. NotAllowed when the signed-in user is not an Active administrator.
    Task<ProviderDetailsLookup> GetDetailsAsync(int adminUserId, int providerId, CancellationToken ct = default);

    /// Suspend / deactivate / reactivate / restore a provider's ACCOUNT, and write the admin log entry in the same save.
    /// Suspending or deactivating also cancels the provider's Pending bookings (the customers are notified) and lists
    /// their Accepted / In Progress bookings in the message so the administrator can follow them up.
    Task<ChangeStatusResult> ChangeStatusAsync(int adminUserId, int providerId, AccountAction action, string? reason, CancellationToken ct = default);

    /// An emergency (for example the provider is sick today, which they cannot request themselves): the administrator
    /// gives the day off at once. The ADMIN_ACTIONS row (who, when, why) and the PROVIDER_UNAVAILABLE_DATES row that
    /// points to it are saved together. Refused on a day with an active booking or a day another provider has.
    Task<ExtraDayOffResult> ApproveExtraDayOffAsync(int adminUserId, int providerId, DateOnly? date, string? reason, CancellationToken ct = default);

    /// Database v2.7: approve or reject a provider's Pending day-off request, with an ADMIN_ACTIONS row saved in the
    /// same transaction. Approving blocks bookings on its days (refused while a day has an active booking);
    /// rejecting deletes its days, so the provider stays bookable and other providers can ask for those dates.
    Task<AdminDayOffResult> DecideDayOffRequestAsync(int adminUserId, int providerId, int requestId, bool approve, CancellationToken ct = default);
}

public class ProviderManagementService : IProviderManagementService
{
    public const int PageSize = 12;      // 4 rows of 3 cards

    private const int TagsShown = 2;

    public const int MaxReasonLength = 255;

    private const int ManilaOffsetHours = 8;          // the Philippines has no daylight saving

    /// How many still-open bookings the message lists by number (the rest are counted).
    private const int OpenBookingsListed = 5;

    /// ADMIN_ACTIONS.ACTION_TYPE of an approved extra day off.
    public const string ExtraDayOffActionType = "Extra Day Off Approved";

    /// The badge on a provider's card while one of their day-off requests waits for an answer (database v2.7).
    public const string DayOffRequestBadge = "Day off request";

    /// ADMIN_ACTIONS.ACTION_TYPE of the answer to a day-off request (database v2.7); REASON holds its dates.
    public const string DayOffApprovedActionType = "Day Off Approved";
    public const string DayOffRejectedActionType = "Day Off Rejected";

    /// The reason is also written to ADMIN_ACTIONS.REASON (255) after the date ("Nov 20, 2026: "), so it is kept shorter.
    public const int MaxExtraDayReasonLength = 200;

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProviderManagementService> _logger;

    private readonly IDayOffLiveUpdates? _live;

    public ProviderManagementService(ServisyoDbContext db, ILogger<ProviderManagementService>? logger = null, TimeProvider? clock = null,
        IDayOffLiveUpdates? live = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderManagementService>.Instance;
        _live = live;
    }

    /// Every administrator's open pages and the provider's own pages show the change at once (database v2.7).
    private Task PushDayOffAsync(int providerId, int providerUserId, string what) =>
        _live?.DayOffChangedAsync(providerId, providerUserId, what) ?? Task.CompletedTask;

    private DateOnly TodayInCebu() => DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(TimeSpan.FromHours(ManilaOffsetHours)).DateTime);

    public async Task<ProvidersPageViewModel?> GetProvidersAsync(int adminUserId, ProviderListQuery query, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return null;

        var all = _db.Providers.AsNoTracking();

        // ---- Tab numbers (one provider = one tab, so they add up to "All") ----
        var groups = await all
            .GroupBy(p => new { p.ApprovalStatus, Account = p.User.AccountStatus })
            .Select(g => new { g.Key.ApprovalStatus, g.Key.Account, Count = g.Count() })
            .ToListAsync(ct);
        int CountOf(string key) =>
            groups.Where(g => ProviderStatuses.Primary(g.ApprovalStatus, g.Account) == key).Sum(g => g.Count);

        var status = ProviderStatuses.Normalize(query.Status);
        var filtered = ApplyFilter(all, status);

        var matching = await filtered.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize));
        var page = Math.Clamp(query.Page, 1, pageCount);

        // ---- This page's providers (oldest application first) ----
        var rows = await filtered
            .OrderBy(p => p.DateApplied).ThenBy(p => p.ProviderId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(p => new
            {
                p.ProviderId, p.BusinessName, p.ApprovalStatus, p.AvgRating, p.ReviewCount,
                First = p.User.FirstName, Last = p.User.LastName, Account = p.User.AccountStatus
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.ProviderId).ToList();

        // Services (active ones only) and categories for just these providers.
        var services = await _db.Services.AsNoTracking()
            .Where(s => ids.Contains(s.ProviderId) && s.IsActive)
            .OrderBy(s => s.ServiceId)
            .Select(s => new { s.ProviderId, s.ServiceName, Category = s.Category.CategoryName })
            .ToListAsync(ct);
        var declared = await _db.ProviderCategories.AsNoTracking()
            .Where(pc => ids.Contains(pc.ProviderId))
            .OrderBy(pc => pc.ProviderCategoryId)
            .Select(pc => new { pc.ProviderId, Category = pc.Category.CategoryName })
            .ToListAsync(ct);

        // Database v2.7: the day-off requests still waiting for an answer (all providers): the list at the top and
        // a "Day off request" badge on the cards of this page.
        var today = TodayInCebu();
        var waitingDays = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(d => d.RequestId != null && d.Request!.Status == DayOffStatuses.Pending)
            .Select(d => new { RequestId = d.RequestId!.Value, d.ProviderId, d.UnavailableDate, RequestedAt = d.Request!.RequestedAt,
                               d.Provider.User.FirstName, d.Provider.User.LastName })
            .ToListAsync(ct);
        var waiting = waitingDays.GroupBy(d => d.RequestId)
            .Select(g => new { g.First().ProviderId, g.First().RequestedAt, First = g.Min(d => d.UnavailableDate), Last = g.Max(d => d.UnavailableDate),
                               Days = g.Count(), Name = $"{g.First().FirstName} {g.First().LastName}".Trim() })
            .Where(w => w.Last >= today)
            .OrderBy(w => w.RequestedAt).ThenBy(w => w.First)
            .ToList();
        var waitingProviders = waiting.Select(w => w.ProviderId).ToHashSet();

        var cards = rows.Select(r =>
        {
            var name = $"{r.First} {r.Last}".Trim();
            var myServices = services.Where(s => s.ProviderId == r.ProviderId).ToList();

            // Category: what the provider declared, otherwise the category of their first service.
            var category = declared.Where(d => d.ProviderId == r.ProviderId).Select(d => d.Category).FirstOrDefault()
                           ?? myServices.Select(s => s.Category).FirstOrDefault();
            var where = new[] { category }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            var badges = new List<ProviderBadge> { ApprovalBadge(r.ApprovalStatus) };
            if (r.Account == AccountStatuses.Suspended) badges.Add(new("Suspended", "danger"));
            else if (r.Account == AccountStatuses.Deactivated) badges.Add(new("Deactivated", "muted"));
            if (waitingProviders.Contains(r.ProviderId)) badges.Add(new(DayOffRequestBadge, "pending"));

            return new ProviderCardViewModel
            {
                ProviderId = r.ProviderId,
                Name = name,
                Initials = NameFormat.Initials(name),
                BusinessName = r.BusinessName,
                Details = where.Count == 0 ? null : string.Join(" · ", where),
                ApprovalStatus = r.ApprovalStatus,
                Rating = r.ReviewCount > 0 ? r.AvgRating : null,
                ReviewCount = r.ReviewCount,
                Badges = badges,
                ServiceTags = myServices.Take(TagsShown).Select(s => NameFormat.Truncate(s.ServiceName, 24)).ToList(),
                ExtraServices = Math.Max(0, myServices.Count - TagsShown)
            };
        }).ToList();

        return new ProvidersPageViewModel
        {
            Shell = shell,
            Status = status,
            CountAll = groups.Sum(g => g.Count),
            CountPending = CountOf(ApprovalStatuses.Pending),
            CountApproved = CountOf(ApprovalStatuses.Approved),
            CountSuspended = CountOf(AccountStatuses.Suspended),
            CountRejected = CountOf(ApprovalStatuses.Rejected),
            CountDeactivated = CountOf(AccountStatuses.Deactivated),
            TotalMatching = matching,
            Page = page,
            PageCount = pageCount,
            Cards = cards,
            WaitingDayOffs = waiting.Select(w => new WaitingDayOffLine(w.ProviderId, w.Name, DayOffFormat.Range(w.First, w.Last), DayOffFormat.Days(w.Days))).ToList()
        };
    }


    // ------------------------------------------------------------------ details pop-up

    public async Task<ProviderDetailsLookup> GetDetailsAsync(int adminUserId, int providerId, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return new(ProviderDetailsOutcome.NotAllowed, null);

        var p = await _db.Providers.AsNoTracking()
            .Where(x => x.ProviderId == providerId && x.User.Role == Roles.ServiceProvider)
            .Select(x => new
            {
                x.ProviderId, x.BusinessName, x.Description, x.Address, x.ApprovalStatus, x.AvgRating, x.ReviewCount,
                First = x.User.FirstName, Last = x.User.LastName, x.User.Email, x.User.PhoneNumber,
                Account = x.User.AccountStatus, Joined = x.User.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
        if (p is null) return new(ProviderDetailsOutcome.NotFound, null);

        var services = await _db.Services.AsNoTracking()
            .Where(s => s.ProviderId == providerId && s.IsActive)
            .OrderBy(s => s.ServiceId)
            .Select(s => new { s.ServiceName, Category = s.Category.CategoryName, s.Price, s.DurationMinutes })
            .ToListAsync(ct);

        var declared = await _db.ProviderCategories.AsNoTracking()
            .Where(pc => pc.ProviderId == providerId)
            .OrderBy(pc => pc.ProviderCategoryId)
            .Select(pc => pc.Category.CategoryName)
            .FirstOrDefaultAsync(ct);

        var completed = await _db.Bookings.AsNoTracking()
            .CountAsync(b => b.ProviderId == providerId && b.Status == BookingStatuses.Completed, ct);

        var today = TodayInCebu();
        var lastDay = UnavailableDateRules.LastAllowed(today);
        var daysOff = await DayOffLinesAsync(providerId, today, ct);

        var name = $"{p.First} {p.Last}".Trim();
        var category = declared ?? services.Select(s => s.Category).FirstOrDefault();
        var where = new[] { category }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        var badges = new List<ProviderBadge> { ApprovalBadge(p.ApprovalStatus) };
        if (p.Account == AccountStatuses.Suspended) badges.Add(new("Suspended", "danger"));
        else if (p.Account == AccountStatuses.Deactivated) badges.Add(new("Deactivated", "muted"));

        return new(ProviderDetailsOutcome.Ok, new ProviderDetailsViewModel
        {
            Shell = shell,
            ProviderId = p.ProviderId,
            Name = name,
            Initials = NameFormat.Initials(name),
            BusinessName = p.BusinessName,
            Location = where.Count == 0 ? null : string.Join(" · ", where),
            ApprovalStatus = p.ApprovalStatus,
            AccountStatus = p.Account,
            Badges = badges,
            Email = p.Email,
            Phone = string.IsNullOrWhiteSpace(p.PhoneNumber) ? null : p.PhoneNumber.Trim(),
            MemberSince = p.Joined.AddHours(ManilaOffsetHours).ToString("MMM d, yyyy", CultureInfo.InvariantCulture),   // stored in UTC, shown in Cebu time
            CompletedJobs = completed,
            Address = string.IsNullOrWhiteSpace(p.Address) ? null : p.Address.Trim(),
            Description = string.IsNullOrWhiteSpace(p.Description) ? null : p.Description.Trim(),
            Rating = p.ReviewCount > 0 ? p.AvgRating : null,
            ReviewCount = p.ReviewCount,
            Services = services.Select(s => new ProviderServiceLine(
                s.ServiceName, s.Category, ServiceFormat.Peso(s.Price), ServiceFormat.Duration(s.DurationMinutes))).ToList(),
            Actions = AccountTransitions.AvailableFor(p.Account).Select(UserActionInfo.For).ToList(),
            DaysOff = daysOff,
            CanApproveDayOff = p.ApprovalStatus == ApprovalStatuses.Approved,
            DayOffMin = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DayOffMax = lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
    }

    // ------------------------------------------------------------------ approve an extra day off

    public async Task<ExtraDayOffResult> ApproveExtraDayOffAsync(int adminUserId, int providerId, DateOnly? date, string? reason, CancellationToken ct = default)
    {
        // 1. The actor must be an Active administrator (checked in the database, not trusted from the browser).
        var isAdmin = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == adminUserId
            && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active, ct);
        if (!isAdmin)
        {
            _logger.LogWarning("Extra day off refused: user {UserId} is not an active administrator.", adminUserId);
            return new(ExtraDayOffOutcome.NotAllowed, "You don't have permission to do that.");
        }

        // 2. The form: a reason (it is an exception, so it is always explained) and a day from today to December 31.
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is null)
            return new(ExtraDayOffOutcome.Invalid, "Please give the reason for the extra day off (for example: sick, family emergency).");
        if (cleanReason.Length > MaxExtraDayReasonLength)
            return new(ExtraDayOffOutcome.Invalid, $"The reason can be at most {MaxExtraDayReasonLength} characters.");
        if (date is null)
            return new(ExtraDayOffOutcome.Invalid, "Please choose the day off.");

        var day = date.Value;
        var today = TodayInCebu();
        var label = day.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        if (day < today)
            return new(ExtraDayOffOutcome.Invalid, $"{label} is in the past. Choose today or a later date.");
        if (day > UnavailableDateRules.LastAllowed(today))
            return new(ExtraDayOffOutcome.Invalid, $"{label} is not in {today.Year}. Extra days off can only be approved until December 31, {today.Year}.");

        // Serializable: the checks below and the save are ONE step, so a customer cannot book that day in between.
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        // 3. The target must be an APPROVED provider.
        var provider = await _db.Providers.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.ProviderId == providerId && p.User.Role == Roles.ServiceProvider, ct);
        if (provider is null)
            return new(ExtraDayOffOutcome.NotFound, "That provider was not found.");
        var name = $"{provider.User.FirstName} {provider.User.LastName}".Trim();
        if (provider.ApprovalStatus != ApprovalStatuses.Approved)
            return new(ExtraDayOffOutcome.Invalid, $"{name} is not an approved provider, so there is no schedule to change.");

        // 4. Not a day that is already off (or asked for), and not a day with an active booking (a customer is waiting for them).
        var existing = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(d => d.ProviderId == providerId && d.UnavailableDate == day)
            .Select(d => new { Approved = d.ApprovedByActionId != null, Pending = d.RequestId != null && d.Request!.Status == DayOffStatuses.Pending })
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return new(ExtraDayOffOutcome.Invalid, existing.Approved
                ? $"An extra day off on {label} was already approved for {name}."
                : existing.Pending
                    ? $"{name} already asked for {label} off. Approve their request in the Days Off list instead."
                    : $"{name} has already marked {label} as a day off.");

        // only ONE provider can be off on any date (database v2.5), an administrator's approval included
        var other = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(d => d.UnavailableDate == day && d.ProviderId != providerId)
            .Select(d => new { d.Provider.User.FirstName, d.Provider.User.LastName })
            .FirstOrDefaultAsync(ct);
        if (other is not null)
            return new(ExtraDayOffOutcome.Invalid,
                $"{other.FirstName} {other.LastName} already has {label} off. Only one provider can be off per day, so {name} cannot be given that day.");

        var job = await _db.Bookings.AsNoTracking()
            .Where(b => b.ProviderId == providerId && b.BookingDate == day && ActiveBookingRules.Statuses.Contains(b.Status))
            .OrderBy(b => b.BookingTime)
            .Select(b => new { b.BookingId, b.Status })
            .FirstOrDefaultAsync(ct);
        if (job is not null)
            return new(ExtraDayOffOutcome.Invalid,
                $"{name} has booking #{job.BookingId} ({job.Status}) on {label}. That booking has to be settled first, so the customer is not left without a provider.");

        // 5. The admin log entry and the day off that points to it: one SaveChanges = one database transaction.
        var adminLog = AdminAction.ForProvider(adminUserId, ExtraDayOffActionType, providerId, $"{label}: {cleanReason}");
        adminLog.CreatedAt = DateTime.UtcNow;
        _db.AdminActions.Add(adminLog);
        _db.ProviderUnavailableDates.Add(new ProviderUnavailableDate
        {
            ProviderId = providerId, UnavailableDate = day, Reason = cleanReason, ApprovedBy = adminLog
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // e.g. this or another provider took the same day at the same moment (a unique index caught it): nothing was saved
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Extra day off for provider {ProviderId} on {Date} was not saved.", providerId, day);
            return new(ExtraDayOffOutcome.Invalid, $"The extra day off for {name} could not be saved. Please open the provider again and retry.");
        }

        _logger.LogInformation("Admin {AdminUserId} approved an extra day off for provider {ProviderId} on {Date}.", adminUserId, providerId, day);
        await PushDayOffAsync(providerId, provider.UserId, "extra-day");
        return new(ExtraDayOffOutcome.Success, $"Extra day off approved for {name} on {label}. Customers cannot book them on that day.");
    }

    // ------------------------------------------------------------------ approve / reject a day-off request (v2.7)

    public async Task<AdminDayOffResult> DecideDayOffRequestAsync(int adminUserId, int providerId, int requestId, bool approve, CancellationToken ct = default)
    {
        // 1. The actor must be an Active administrator (checked in the database, not trusted from the browser).
        var isAdmin = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == adminUserId
            && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active, ct);
        if (!isAdmin)
        {
            _logger.LogWarning("Day off decision refused: user {UserId} is not an active administrator.", adminUserId);
            return new(AdminDayOffOutcome.NotAllowed, "You don't have permission to do that.");
        }

        var today = TodayInCebu();

        // Serializable: "no active booking on these days" is checked and the approval saved as ONE step, so a customer
        // cannot book one of the days in between (BookProviderService reads the days off the same way).
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        // 2. The request must belong to THIS provider (both come from the address and are checked together).
        var request = await _db.DayOffRequests
            .Include(r => r.Days)
            .Include(r => r.Provider).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(r => r.RequestId == requestId && r.ProviderId == providerId, ct);
        if (request is null || (request.Days.Count == 0 && request.Status == DayOffStatuses.Pending))
            return new(AdminDayOffOutcome.NotFound, "That day off request was not found. It may have been cancelled by the provider.");

        var name = $"{request.Provider.User.FirstName} {request.Provider.User.LastName}".Trim();
        if (request.Status != DayOffStatuses.Pending)
            return new(AdminDayOffOutcome.Invalid, $"This day off request of {name} was already {request.Status.ToLowerInvariant()}.");

        var first = request.Days.Min(d => d.UnavailableDate);
        var last = request.Days.Max(d => d.UnavailableDate);
        var rangeText = DayOffFormat.Range(first, last);
        var daysText = DayOffFormat.Days(request.Days.Count);

        // 3. Approving: no active booking on any of its days from today on (a customer is waiting for the provider).
        if (approve)
        {
            var dates = request.Days.Where(d => d.UnavailableDate >= today).Select(d => d.UnavailableDate).ToList();
            var job = await _db.Bookings.AsNoTracking()
                .Where(b => b.ProviderId == providerId && dates.Contains(b.BookingDate) && ActiveBookingRules.Statuses.Contains(b.Status))
                .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime)
                .Select(b => new { b.BookingId, b.Status, b.BookingDate })
                .FirstOrDefaultAsync(ct);
            if (job is not null)
                return new(AdminDayOffOutcome.Invalid,
                    $"{name} has booking #{job.BookingId} ({job.Status}) on {DayOffFormat.Date(job.BookingDate)}. That booking has to be settled first (or reject the request), so the customer is not left without a provider.");
        }

        // 4. The admin log entry and the answer that points to it: one SaveChanges = one database transaction.
        var adminLog = AdminAction.ForProvider(adminUserId, approve ? DayOffApprovedActionType : DayOffRejectedActionType, providerId, rangeText);
        adminLog.CreatedAt = DateTime.UtcNow;
        _db.AdminActions.Add(adminLog);
        request.Status = approve ? DayOffStatuses.Approved : DayOffStatuses.Rejected;
        request.DecidedBy = adminLog;
        if (!approve) _db.ProviderUnavailableDates.RemoveRange(request.Days);       // the dates are free again

        try
        {
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "The decision on day off request {RequestId} was not saved.", requestId);
            return new(AdminDayOffOutcome.Invalid, $"The answer to {name}'s request could not be saved. Please open the provider again and retry.");
        }

        _logger.LogInformation("Admin {AdminUserId} {Decision} day off request {RequestId} of provider {ProviderId} ({Dates}).",
            adminUserId, approve ? "approved" : "rejected", requestId, providerId, rangeText);
        await PushDayOffAsync(providerId, request.Provider.UserId, approve ? "approved" : "rejected");
        return new(AdminDayOffOutcome.Success, approve
            ? $"Day off approved for {name}: {rangeText} ({daysText}). Customers cannot book them on those days."
            : $"Day off request of {name} for {rangeText} was rejected. They stay available on those days.");
    }

    /// The admin pop-up's "Days Off": requests (Pending first in the eye: they need an answer) and single days, by date.
    private async Task<IReadOnlyList<ProviderDayOffLine>> DayOffLinesAsync(int providerId, DateOnly today, CancellationToken ct)
    {
        var rows = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(d => d.ProviderId == providerId)
            .Select(d => new
            {
                d.UnavailableDate, d.Reason, d.RequestId,
                Status = d.Request != null ? d.Request.Status : null,
                RequestReason = d.Request != null ? d.Request.Reason : null,
                ByAdmin = d.ApprovedByActionId != null
            })
            .ToListAsync(ct);

        var lines = new List<(DateOnly First, ProviderDayOffLine Line)>();
        foreach (var g in rows.Where(r => r.RequestId != null).GroupBy(r => r.RequestId!.Value))
        {
            var first = g.Min(r => r.UnavailableDate);
            var last = g.Max(r => r.UnavailableDate);
            if (last < today) continue;
            var pending = g.First().Status == DayOffStatuses.Pending;
            lines.Add((first, new ProviderDayOffLine(g.Key, DayOffFormat.Range(first, last), DayOffFormat.Days(g.Count()), g.First().RequestReason,
                pending ? UnavailableDateRules.PendingLabel : UnavailableDateRules.ApprovedLabel, pending ? "pending" : "completed", pending)));
        }
        foreach (var r in rows.Where(r => r.RequestId == null && r.UnavailableDate >= today))
            lines.Add((r.UnavailableDate, new ProviderDayOffLine(null, DayOffFormat.Date(r.UnavailableDate), DayOffFormat.Days(1), r.Reason,
                r.ByAdmin ? ProviderDayOffLine.ExtraLabel : "Day off", r.ByAdmin ? "scheduled" : "muted", false)));
        return lines.OrderBy(l => l.First).Select(l => l.Line).ToList();
    }

    // ------------------------------------------------------------------ suspend / deactivate / ...

    public async Task<ChangeStatusResult> ChangeStatusAsync(
        int adminUserId, int providerId, AccountAction action, string? reason, CancellationToken ct = default)
    {
        // 1. The actor must be an Active administrator (checked in the database, not trusted from the browser).
        var isAdmin = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == adminUserId
            && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active, ct);
        if (!isAdmin)
        {
            _logger.LogWarning("Provider account change refused: user {UserId} is not an active administrator.", adminUserId);
            return new(ChangeStatusOutcome.NotAllowed, "You don't have permission to do that.");
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is { Length: > MaxReasonLength })
            return new(ChangeStatusOutcome.ReasonTooLong, $"The reason can be at most {MaxReasonLength} characters.");

        // 2. The target must be a PROVIDER account (never an administrator or a customer from this page).
        var provider = await _db.Providers.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.ProviderId == providerId && p.User.Role == Roles.ServiceProvider, ct);
        if (provider is null)
            return new(ChangeStatusOutcome.NotFound, "That provider was not found.");

        var user = provider.User;
        var name = $"{user.FirstName} {user.LastName}".Trim();

        // 3. The change must be allowed from the account's CURRENT status.
        var newStatus = AccountTransitions.TargetStatus(user.AccountStatus, action);
        if (newStatus is null)
            return new(ChangeStatusOutcome.InvalidTransition,
                $"{name}'s account is currently {user.AccountStatus.ToLowerInvariant()}, so that action isn't available. The list shows the latest status.");

        // 4. The new status (on USERS and its copy on SERVICE_PROVIDERS), the cancelled bookings, the customers'
        //    notifications and the log entry are saved together (one SaveChanges = one database transaction).
        //    In SQL Server the triggers also keep the copy equal; setting it here keeps the app right without them.
        user.AccountStatus = newStatus;
        provider.AccountStatus = newStatus;

        var stopsWorking = newStatus is AccountStatuses.Suspended or AccountStatuses.Deactivated;
        var cancelled = new List<Booking>();
        var stillOpen = new List<OpenBooking>();
        // one moment for the cancellations and the ADMIN_ACTIONS row: that is how the customers' bells
        // (dbo.usp_GetNotifications, database v2.3) know the booking was cancelled because of this suspension
        var now = DateTime.UtcNow;
        if (stopsWorking)
        {
            // Pending requests can never be accepted now: cancel them (each customer's bell says why).
            cancelled = await _db.Bookings.Include(b => b.Customer)
                .Where(b => b.ProviderId == provider.ProviderId && b.Status == BookingStatuses.Pending)
                .OrderBy(b => b.BookingId)
                .ToListAsync(ct);
            foreach (var b in cancelled)
            {
                b.Status = BookingStatuses.Cancelled;
                b.UpdatedAt = now;
            }

            // Accepted / In Progress jobs may already be paid: they are NOT changed, only shown to the administrator.
            stillOpen = await _db.Bookings.AsNoTracking()
                .Where(b => b.ProviderId == provider.ProviderId
                         && (b.Status == BookingStatuses.Accepted || b.Status == BookingStatuses.InProgress))
                .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime)
                .Select(b => new OpenBooking(b.BookingId, b.Status, b.BookingDate, b.BookingTime))
                .ToListAsync(ct);
        }

        // the log points to the SERVICE_PROVIDER (database v2.2), like the approve / reject entries
        var adminLog = AdminAction.ForProvider(adminUserId, "Account " + newStatus, provider.ProviderId, cleanReason);
        adminLog.CreatedAt = now;
        _db.AdminActions.Add(adminLog);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // a customer changed one of these bookings at the same moment (BOOKINGS.ROW_VERSION): nothing was saved
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Provider {ProviderId}: status change not saved, a booking changed at the same time.", provider.ProviderId);
            return new(ChangeStatusOutcome.InvalidTransition,
                $"{name}'s bookings changed while you were doing this, so nothing was saved. Please try again.");
        }

        _logger.LogInformation("Admin {AdminUserId} changed provider account {TargetUserId} to {NewStatus}; {Cancelled} pending booking(s) cancelled, {Open} still open.",
            adminUserId, user.UserId, newStatus, cancelled.Count, stillOpen.Count);

        var done = action switch
        {
            AccountAction.Suspend => "suspended",
            AccountAction.Deactivate => "deactivated",
            AccountAction.Reactivate => "reactivated",
            _ => "restored"
        };
        return new(ChangeStatusOutcome.Success, $"{name} was {done}." + BookingsNote(cancelled.Count, stillOpen));
    }

    private sealed record OpenBooking(int BookingId, string Status, DateOnly Date, TimeOnly Time);

    /// " 2 pending bookings were cancelled and the customers were notified. Still open, please follow up: #12 (Accepted, Nov 5, 9:00 AM)."
    private static string BookingsNote(int cancelledCount, IReadOnlyList<OpenBooking> stillOpen)
    {
        var note = cancelledCount switch
        {
            0 => string.Empty,
            1 => " 1 pending booking was cancelled and the customer was notified.",
            _ => $" {cancelledCount} pending bookings were cancelled and the customers were notified."
        };
        if (stillOpen.Count > 0)
        {
            var listed = stillOpen.Take(OpenBookingsListed).Select(b =>
                $"#{b.BookingId} ({b.Status}, {b.Date.ToString("MMM d", CultureInfo.InvariantCulture)}, {b.Time.ToString("h:mm tt", CultureInfo.InvariantCulture)})");
            var more = stillOpen.Count > OpenBookingsListed ? $" and {stillOpen.Count - OpenBookingsListed} more" : string.Empty;
            note += $" Still open, please follow up: {string.Join(", ", listed)}{more}.";
        }
        return note;
    }

    /// Same rule as ProviderStatuses.Primary, written so the database can run it.
    private static IQueryable<Provider> ApplyFilter(IQueryable<Provider> providers, string status) => status switch
    {
        ApprovalStatuses.Pending => providers.Where(p => p.User.AccountStatus == AccountStatuses.Active && p.ApprovalStatus == ApprovalStatuses.Pending),
        ApprovalStatuses.Approved => providers.Where(p => p.User.AccountStatus == AccountStatuses.Active && p.ApprovalStatus == ApprovalStatuses.Approved),
        ApprovalStatuses.Rejected => providers.Where(p => p.User.AccountStatus == AccountStatuses.Active && p.ApprovalStatus == ApprovalStatuses.Rejected),
        AccountStatuses.Suspended => providers.Where(p => p.User.AccountStatus == AccountStatuses.Suspended),
        AccountStatuses.Deactivated => providers.Where(p => p.User.AccountStatus == AccountStatuses.Deactivated),
        _ => providers
    };

    private static ProviderBadge ApprovalBadge(string approvalStatus) => approvalStatus switch
    {
        ApprovalStatuses.Approved => new("Approved", "completed"),   // green
        ApprovalStatuses.Pending => new("Pending", "pending"),       // yellow
        ApprovalStatuses.Rejected => new("Rejected", "danger"),      // red
        _ => new(approvalStatus, "muted")
    };
}
