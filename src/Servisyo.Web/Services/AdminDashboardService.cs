using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IAdminDashboardService
{
    /// Returns null when the signed-in user is not an Active administrator.
    Task<AdminDashboardViewModel?> GetAsync(int userId, CancellationToken ct = default);
}

public class AdminDashboardService : IAdminDashboardService
{
    private const int RecentBookingCount = 5;
    private const int PendingApplicationCount = 4;

    private const int ManilaOffsetHours = 8;          // the Philippines has no daylight saving

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IOptionsMonitor<InactiveAccountOptions>? _inactive;

    public AdminDashboardService(ServisyoDbContext db, TimeProvider? clock = null, IOptionsMonitor<InactiveAccountOptions>? inactive = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _inactive = inactive;
    }

    private DateOnly TodayInCebu() => DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(TimeSpan.FromHours(ManilaOffsetHours)).DateTime);

    public async Task<AdminDashboardViewModel?> GetAsync(int userId, CancellationToken ct = default)
    {
        // Who is asking comes from the sign-in cookie; the database must agree they are an active admin.
        var admin = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId
                     && u.Role == Roles.Administrator
                     && u.AccountStatus == AccountStatuses.Active)
            .Select(u => new { u.FirstName })
            .FirstOrDefaultAsync(ct);
        if (admin is null) return null;

        // ---- Numbers (administrators are not counted as "users" of the marketplace) ----
        var customers = await _db.Users.CountAsync(u => u.Role == Roles.Customer, ct);
        var providers = await _db.Users.CountAsync(u => u.Role == Roles.ServiceProvider, ct);
        var pending = await _db.Providers.CountAsync(p => p.ApprovalStatus == ApprovalStatuses.Pending, ct);
        var totalBookings = await _db.Bookings.CountAsync(ct);
        var flagged = await _db.Reviews.CountAsync(r => r.Status == ReviewStatuses.Flagged, ct);
        // suspended accounts: customers are managed on Admin > Users, providers on Admin > Service Providers
        var suspendedCustomers = await _db.Users.CountAsync(
            u => u.AccountStatus == AccountStatuses.Suspended && u.Role == Roles.Customer, ct);
        var suspendedProviders = await _db.Users.CountAsync(
            u => u.AccountStatus == AccountStatuses.Suspended && u.Role == Roles.ServiceProvider, ct);

        // database v2.7: day-off requests still waiting (the same list as Admin > Service Providers: a day from today on),
        // oldest first, so "Review Day Offs" opens the provider who has waited longest
        var today = TodayInCebu();
        var waitingDayOffs = await _db.DayOffRequests.AsNoTracking()
            .Where(r => r.Status == DayOffStatuses.Pending && r.Days.Any(d => d.UnavailableDate >= today))
            .OrderBy(r => r.RequestedAt).ThenBy(r => r.RequestId)
            .Select(r => r.ProviderId)
            .ToListAsync(ct);

        // database v2.8: name change requests still waiting
        var nameChanges = await _db.NameChangeRequests.CountAsync(r => r.Status == NameChangeStatuses.Pending, ct);
        // database v2.3: worked out by dbo.usp_GetBookingsNeedingAttention (TotalCount = all of them, not only the 25 rows)
        var attention = (await _db.GetBookingsNeedingAttentionAsync(ct)).FirstOrDefault();
        // database v2.12: the late starts and no-shows of the last 7 days have their own row (counted once, not twice)
        var needAttention = attention is null ? 0 : attention.TotalCount - attention.LateStartCount - attention.NoShowCount;

        // database v2.10: while automatic deletion is off, the accounts the daily clean-up WOULD warn (counted now,
        // nothing stored; this replaces the v2.9 REPORT lines in System Logs)
        int inactive = 0, inactiveOverOneYear = 0;
        if (_inactive?.CurrentValue.DeleteEnabled != true)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var wouldWarn = InactiveAccountQueries.DueForWarning(InactiveAccountQueries.Candidates(_db), now);
            inactive = await wouldWarn.CountAsync(ct);
            if (inactive > 0) inactiveOverOneYear = await InactiveAccountQueries.OverOneYear(wouldWarn, now).CountAsync(ct);
        }

        // ---- Recent bookings (newest first) ----
        var bookingRows = await _db.Bookings.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BookingId)
            .Take(RecentBookingCount)
            .Select(b => new
            {
                b.BookingId,
                b.Status,
                CustomerFirst = b.Customer.User.FirstName,
                CustomerLast = b.Customer.User.LastName,
                ProviderFirst = b.Provider.User.FirstName,
                ProviderLast = b.Provider.User.LastName
            })
            .ToListAsync(ct);
        var titles = await _db.GetBookingTitlesAsync(bookingRows.Select(r => r.BookingId).ToList(), ct);

        // ---- Pending provider applications (oldest first = fair review queue) ----
        var applicationRows = await _db.Providers.AsNoTracking()
            .Where(p => p.ApprovalStatus == ApprovalStatuses.Pending)
            .OrderBy(p => p.DateApplied).ThenBy(p => p.ProviderId)
            .Take(PendingApplicationCount)
            .Select(p => new
            {
                p.ProviderId,
                p.BusinessName,
                First = p.User.FirstName,
                Last = p.User.LastName,
                Category = p.ProviderCategories
                    .OrderBy(pc => pc.ProviderCategoryId)
                    .Select(pc => pc.Category.CategoryName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return new AdminDashboardViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Admin",
                FirstName = admin.FirstName,
                FullName = admin.FirstName,
                Initials = NameFormat.Initials(admin.FirstName),
                Subtitle = "System Administrator",
                UnreadNotifications = await _db.CountUnreadNotificationsAsync(userId, ct),
                UnreadMessages = 0
            },
            TotalUsers = customers + providers,
            Customers = customers,
            Providers = providers,
            PendingApprovals = pending,
            TotalBookings = totalBookings,
            FlaggedReviews = flagged,
            Attention = AdminAttention.Build(new AdminAttentionCounts
            {
                DayOffRequests = waitingDayOffs.Count,
                FirstDayOffProviderId = waitingDayOffs.Count > 0 ? waitingDayOffs[0] : null,
                NameChangeRequests = nameChanges,
                PendingApplications = pending,
                FirstPendingProviderId = applicationRows.Select(a => (int?)a.ProviderId).FirstOrDefault(),
                SuspendedCustomers = suspendedCustomers,
                SuspendedProviders = suspendedProviders,
                FlaggedReviews = flagged,
                BookingsNeedingAttention = needAttention,
                LateStarts = attention?.LateStartCount ?? 0,
                NoShows = attention?.NoShowCount ?? 0,
                InactiveAccounts = inactive,
                InactiveOverOneYear = inactiveOverOneYear
            }),
            RecentBookings = bookingRows.Select(r => new AdminBookingRowViewModel
            {
                BookingId = r.BookingId,
                Title = titles.TryGetValue(r.BookingId, out var t) ? t : "Service booking",
                CustomerName = $"{r.CustomerFirst} {r.CustomerLast}".Trim(),
                ProviderName = $"{r.ProviderFirst} {r.ProviderLast}".Trim(),
                Status = r.Status,
                Display = AdminBookingDisplay.For(r.Status)
            }).ToList(),
            PendingApplications = applicationRows.Select(a =>
            {
                var name = $"{a.First} {a.Last}".Trim();
                // "Plumbing". Until categories exist for the applicant, the business name stands in.
                var details = string.IsNullOrWhiteSpace(a.Category) ? a.BusinessName : a.Category;
                return new PendingApplicationViewModel
                {
                    ProviderId = a.ProviderId,
                    Name = name,
                    Initials = NameFormat.Initials(name),
                    Details = NameFormat.Truncate(details, 40)
                };
            }).ToList()
        };
    }
}
