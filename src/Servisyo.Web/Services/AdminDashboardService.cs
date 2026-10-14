using Microsoft.EntityFrameworkCore;
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

    private readonly ServisyoDbContext _db;

    public AdminDashboardService(ServisyoDbContext db) => _db = db;

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
        var suspended = await _db.Users.CountAsync(
            u => u.AccountStatus == AccountStatuses.Suspended && u.Role != Roles.Administrator, ct);
        var openReports = await _db.BookingReports.CountAsync(r => r.Status != ReportStatuses.Resolved, ct);

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
                p.City,
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
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = 0
            },
            TotalUsers = customers + providers,
            Customers = customers,
            Providers = providers,
            PendingApprovals = pending,
            TotalBookings = totalBookings,
            FlaggedReviews = flagged,
            Attention = AdminAttention.Build(pending, flagged, suspended, openReports,
                firstPendingProviderId: applicationRows.Select(a => (int?)a.ProviderId).FirstOrDefault()),
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
                // "Plumbing · Mandaue". Until categories exist for the applicant, the business name stands in.
                var what = string.IsNullOrWhiteSpace(a.Category) ? a.BusinessName : a.Category;
                var details = string.IsNullOrWhiteSpace(a.City) ? what : $"{what} · {a.City}";
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
