using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface ICustomerDashboardService
{
    /// Returns the dashboard for the signed-in user, or null when that user is not
    /// an Active customer (the controller then signs them out).
    Task<CustomerDashboardViewModel?> GetAsync(int userId, CancellationToken ct = default);
}

public class CustomerDashboardService : ICustomerDashboardService
{
    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);   // Philippines has no daylight saving
    private const int RecentLimit = 5;
    private const int RecommendedLimit = 3;

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;

    public CustomerDashboardService(ServisyoDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
    }

    private sealed class BookingRow
    {
        public int BookingId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public DateOnly BookingDate { get; set; }
        public TimeOnly BookingTime { get; set; }
        public decimal TotalFee { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public int ProviderUserId { get; set; }
        public int ProviderId { get; set; }
        public string? PayMethod { get; set; }
        public string? PayStatus { get; set; }
        public string? PayReference { get; set; }
    }

    private static readonly Expression<Func<Booking, BookingRow>> ToRow = b => new BookingRow
    {
        BookingId = b.BookingId,
        Status = b.Status,
        PaymentStatus = b.PaymentStatus,
        BookingDate = b.BookingDate,
        BookingTime = b.BookingTime,
        TotalFee = b.TotalFee,
        CreatedAt = b.CreatedAt,
        ProviderName = b.Provider.BusinessName,
        ProviderUserId = b.Provider.UserId,
        ProviderId = b.ProviderId,
        PayMethod = b.Payment != null ? b.Payment.PaymentMethod : null,
        PayStatus = b.Payment != null ? b.Payment.PaymentStatus : null,
        PayReference = b.Payment != null ? b.Payment.TransactionReference : null
    };

    public async Task<CustomerDashboardViewModel?> GetAsync(int userId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().ToOffset(ManilaOffset);
        var today = DateOnly.FromDateTime(now.DateTime);

        // The customer is found from the SIGNED-IN user id, never from anything the browser sends.
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.UserId == userId
                     && c.User.Role == Roles.Customer
                     && c.User.AccountStatus == AccountStatuses.Active)
            .Select(c => new { c.CustomerId, c.User.FirstName, c.User.LastName })
            .FirstOrDefaultAsync(ct);
        if (customer is null) return null;

        var mine = _db.Bookings.AsNoTracking().Where(b => b.CustomerId == customer.CustomerId);

        // Current booking: an active one (In Progress first, then the soonest),
        // otherwise a Completed one that is still waiting for the customer's review.
        var currentRow = await mine
            .Where(b => b.Status == BookingStatuses.Pending
                     || b.Status == BookingStatuses.Accepted
                     || b.Status == BookingStatuses.InProgress)
            .OrderBy(b => b.Status == BookingStatuses.InProgress ? 0 : 1)
            .ThenBy(b => b.BookingDate).ThenBy(b => b.BookingTime)
            .Select(ToRow)
            .FirstOrDefaultAsync(ct);

        currentRow ??= await mine
            .Where(b => b.Status == BookingStatuses.Completed
                     && !_db.Reviews.Any(r => r.BookingId == b.BookingId))
            .OrderByDescending(b => b.BookingDate).ThenByDescending(b => b.BookingTime)
            .Select(ToRow)
            .FirstOrDefaultAsync(ct);

        var recentRows = await mine
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BookingId)
            .Take(RecentLimit)
            .Select(ToRow)
            .ToListAsync(ct);

        var ids = recentRows.Select(r => r.BookingId).ToList();
        if (currentRow is not null && !ids.Contains(currentRow.BookingId)) ids.Add(currentRow.BookingId);
        var titles = await _db.GetBookingTitlesAsync(ids, ct);
        string TitleOf(int id) => titles.TryGetValue(id, out var t) ? t : "Service booking";

        // the services in each recent booking, so "Book Again" can tick them on the booking page
        var recentIds = recentRows.Select(r => r.BookingId).ToList();
        var serviceRows = await _db.BookingServices.AsNoTracking()
            .Where(bs => recentIds.Contains(bs.BookingId))
            .OrderBy(bs => bs.BookingServiceId)
            .Select(bs => new { bs.BookingId, bs.ServiceId })
            .ToListAsync(ct);
        var servicesOf = serviceRows.GroupBy(x => x.BookingId).ToDictionary(g => g.Key, g => g.Select(x => x.ServiceId).ToList());

        var vm = new CustomerDashboardViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Customer",
                Subtitle = "Customer Account",
                FirstName = customer.FirstName,
                FullName = $"{customer.FirstName} {customer.LastName}",
                Initials = NameFormat.Initials($"{customer.FirstName} {customer.LastName}"),
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            Recent = recentRows.Select(r => new RecentBookingViewModel
            {
                BookingId = r.BookingId,
                Title = TitleOf(r.BookingId),
                ProviderName = r.ProviderName,
                Date = r.BookingDate,
                Status = r.Status,
                ProviderId = r.ProviderId,
                ServiceIds = servicesOf.TryGetValue(r.BookingId, out var sids) ? sids : new List<int>()
            }).ToList()
        };

        if (currentRow is not null)
        {
            var pay = new PaymentFacts(currentRow.Status, currentRow.PaymentStatus, currentRow.PayMethod, currentRow.PayStatus,
                currentRow.PayReference, currentRow.TotalFee);
            vm.State = DashboardStates.FromBooking(currentRow.Status, pay.Effective);
            var title = TitleOf(currentRow.BookingId);
            vm.Current = new CurrentBookingViewModel
            {
                BookingId = currentRow.BookingId,
                Reference = $"BK-{currentRow.CreatedAt.Year}-{currentRow.BookingId:D4}",
                Title = title,
                ProviderName = currentRow.ProviderName,
                ProviderUserId = currentRow.ProviderUserId,
                Date = currentRow.BookingDate,
                Time = currentRow.BookingTime,
                Fee = currentRow.TotalFee,
                UpcomingText = vm.State == DashboardState.BookingConfirmed
                    ? $"{DayLabel(currentRow.BookingDate, today)} · {currentRow.BookingTime.ToString("h:mm tt", CultureInfo.InvariantCulture)} — {title} with {currentRow.ProviderName}"
                    : null,
                Payment = PaymentNotes.ForCustomer(pay, currentRow.ProviderName),
                CashToCollect = pay.CashToCollect
            };
        }

        vm.Recommended = await GetRecommendedAsync(today, TimeOnly.FromDateTime(now.DateTime), ct);
        return vm;
    }

    private async Task<IReadOnlyList<RecommendedProviderViewModel>> GetRecommendedAsync(
        DateOnly today, TimeOnly nowTime, CancellationToken ct)
    {
        var providers = await _db.Providers.AsNoTracking()
            .WhereEligible()
            .Where(p => p.Services.Any(s => s.IsActive))
            .OrderByDescending(p => p.AvgRating ?? 0m)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.BusinessName)
            .Take(RecommendedLimit)
            .Select(p => new { p.ProviderId, p.BusinessName, p.AvgRating, p.ReviewCount, p.MinNoticeDays })
            .ToListAsync(ct);
        if (providers.Count == 0) return Array.Empty<RecommendedProviderViewModel>();

        var ids = providers.Select(p => p.ProviderId).ToList();

        var categoryRows = await _db.Services.AsNoTracking()
            .Where(s => s.IsActive && ids.Contains(s.ProviderId))
            .Select(s => new { s.ProviderId, Name = s.Category.CategoryName })
            .Distinct()
            .ToListAsync(ct);

        var scheduleRows = await _db.ProviderAvailabilities.AsNoTracking()
            .Where(a => ids.Contains(a.ProviderId) && a.IsAvailable)
            .Select(a => new { a.ProviderId, a.DayOfWeek, a.EndTime })
            .ToListAsync(ct);

        var lastDay = today.AddDays(AvailabilityHint.LookAheadDays);
        var offRows = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(u => ids.Contains(u.ProviderId) && u.UnavailableDate >= today && u.UnavailableDate <= lastDay)
            .Select(u => new { u.ProviderId, u.UnavailableDate })
            .ToListAsync(ct);

        return providers.Select(p =>
        {
            var endByDay = scheduleRows.Where(s => s.ProviderId == p.ProviderId)
                                       .GroupBy(s => s.DayOfWeek)
                                       .ToDictionary(g => g.Key, g => g.Max(x => x.EndTime));
            var off = offRows.Where(o => o.ProviderId == p.ProviderId).Select(o => o.UnavailableDate).ToHashSet();

            return new RecommendedProviderViewModel
            {
                ProviderId = p.ProviderId,
                Name = p.BusinessName,
                Initials = NameFormat.Initials(p.BusinessName),
                AvgRating = p.AvgRating,
                ReviewCount = p.ReviewCount,
                CategoryName = categoryRows.Where(c => c.ProviderId == p.ProviderId)
                                           .Select(c => c.Name).OrderBy(n => n).FirstOrDefault(),
                AvailabilityText = AvailabilityHint.Next(p.MinNoticeDays, endByDay, off, today, nowTime)
            };
        }).ToList();
    }

    private static string DayLabel(DateOnly date, DateOnly today) =>
        date == today ? "Today"
        : date == today.AddDays(1) ? "Tomorrow"
        : date.ToString("MMM d", CultureInfo.InvariantCulture);
}

public static class AvailabilityHint
{
    public const int LookAheadDays = 7;

    /// A simple hint ("Available Today / Tomorrow / This Week") from the provider's weekly
    /// schedule, unavailable dates and minimum notice. It does NOT check existing bookings
    /// (the booking stage does the strict check), so it is a hint, not a guarantee.
    public static string? Next(
        int minNoticeDays,
        IReadOnlyDictionary<byte, TimeOnly> scheduleEndByDay,   // key: 0 = Sunday ... 6 = Saturday
        IReadOnlySet<DateOnly> unavailableDates,
        DateOnly today,
        TimeOnly nowTime)
    {
        for (var i = Math.Max(0, minNoticeDays); i <= LookAheadDays; i++)
        {
            var date = today.AddDays(i);
            if (unavailableDates.Contains(date)) continue;
            if (!scheduleEndByDay.TryGetValue((byte)date.DayOfWeek, out var end)) continue;
            if (i == 0 && nowTime >= end) continue;   // today's working hours are already over

            return i == 0 ? "Available Today" : i == 1 ? "Available Tomorrow" : "Available This Week";
        }
        return null;
    }
}
