using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public record AdminReportResult(bool Allowed, AdminReportViewModel? Report);

public interface IAdminReportService
{
    /// The monthly summary for one month ("2026-10"; empty = this month, Cebu time). Active administrators only.
    Task<AdminReportResult> GetMonthlyAsync(int adminUserId, string? month, CancellationToken ct = default);
}

/// How the numbers are worked out (stated on the report too):
///  - a booking belongs to the month of its BOOKING DATE (the day of the service);
///  - revenue = total fee of the PAID bookings of that month ("Approve & Pay");
///  - platform fee = a percentage of revenue, from the setting Reports:PlatformFeePercent (default 10). SERVISYO stores no
///    commission yet, so this is an ESTIMATE; provider earnings = revenue - platform fee;
///  - "in demand" = how many times a category's services were booked in the month (every status).
public class AdminReportService : IAdminReportService
{
    public const decimal DefaultPlatformFeePercent = 10m;
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly decimal _feePercent;

    public AdminReportService(ServisyoDbContext db, IConfiguration? config = null, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        var configured = config?["Reports:PlatformFeePercent"];
        _feePercent = decimal.TryParse(configured, NumberStyles.Number, En, out var p) && p is >= 0 and <= 100 ? p : DefaultPlatformFeePercent;
    }

    public async Task<AdminReportResult> GetMonthlyAsync(int adminUserId, string? month, CancellationToken ct = default)
    {
        var admin = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == adminUserId && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(ct);
        if (admin is null) return new(false, null);

        var nowCebu = _clock.GetUtcNow().ToOffset(Manila);
        var first = DateTime.TryParseExact(month, "yyyy-MM", En, DateTimeStyles.None, out var picked)
            ? new DateOnly(picked.Year, picked.Month, 1)
            : new DateOnly(nowCebu.Year, nowCebu.Month, 1);
        var next = first.AddMonths(1);

        var inMonth = _db.Bookings.AsNoTracking().Where(b => b.BookingDate >= first && b.BookingDate < next);

        // ---- sales
        var paid = inMonth.Where(b => b.PaymentStatus == PaymentStatuses.Paid);
        var revenue = await paid.SumAsync(b => (decimal?)b.TotalFee, ct) ?? 0m;
        var paidCount = await paid.CountAsync(ct);
        var fees = Math.Round(revenue * _feePercent / 100m, 2, MidpointRounding.AwayFromZero);

        // ---- bookings by status
        var statusRows = await inMonth.GroupBy(b => b.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountOf(string s) => statusRows.Where(r => r.Status == s).Sum(r => r.Count);
        var completed = CountOf(BookingStatuses.Completed);
        var cancelled = CountOf(BookingStatuses.Cancelled);
        var rejected = CountOf(BookingStatuses.Rejected);
        var finished = completed + cancelled + rejected;
        var order = new[] { BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress, BookingStatuses.Completed, BookingStatuses.Cancelled, BookingStatuses.Rejected };

        // ---- top 5 categories
        var categoryRows = await _db.BookingServices.AsNoTracking()
            .Where(bs => bs.Booking.BookingDate >= first && bs.Booking.BookingDate < next)
            .GroupBy(bs => bs.Service.Category.CategoryName)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var allBooked = categoryRows.Sum(c => c.Count);
        var top = categoryRows.OrderByDescending(c => c.Count).ThenBy(c => c.Name).Take(5)
            .Select((c, i) => new ReportCategory(i + 1, c.Name, c.Count, allBooked == 0 ? 0 : Math.Round(c.Count * 100m / allBooked, 1)))
            .ToList();

        // ---- people
        var monthStartUtc = first.ToDateTime(TimeOnly.MinValue) - Manila;
        var monthEndUtc = next.ToDateTime(TimeOnly.MinValue) - Manila;

        var activeCustomers = await _db.Customers.CountAsync(c => c.User.AccountStatus == AccountStatuses.Active, ct);
        var activeProviders = await _db.Providers.CountAsync(p => p.ApprovalStatus == ApprovalStatuses.Approved && p.User.AccountStatus == AccountStatuses.Active, ct);
        var pending = await _db.Providers.CountAsync(p => p.ApprovalStatus == ApprovalStatuses.Pending, ct);
        var newCustomers = await _db.Users.CountAsync(u => u.Role == Roles.Customer && u.CreatedAt >= monthStartUtc && u.CreatedAt < monthEndUtc, ct);
        var newProviders = await _db.Users.CountAsync(u => u.Role == Roles.ServiceProvider && u.CreatedAt >= monthStartUtc && u.CreatedAt < monthEndUtc, ct);

        // ---- last 6 months (oldest first, ending with the chosen month)
        var trend = new List<ReportMonthRevenue>();
        for (var i = 5; i >= 0; i--)
        {
            var m = first.AddMonths(-i);
            var mNext = m.AddMonths(1);
            var q = _db.Bookings.AsNoTracking().Where(b => b.BookingDate >= m && b.BookingDate < mNext && b.PaymentStatus == PaymentStatuses.Paid);
            trend.Add(new ReportMonthRevenue(m.ToString("MMM yyyy", En), await q.SumAsync(b => (decimal?)b.TotalFee, ct) ?? 0m, await q.CountAsync(ct)));
        }

        var vm = new AdminReportViewModel
        {
            MonthKey = first.ToString("yyyy-MM", En),
            MonthLabel = first.ToString("MMMM yyyy", En),
            GeneratedText = nowCebu.ToString("MMM d, yyyy h:mm tt", En) + " (Cebu time)",
            GeneratedBy = $"{admin.FirstName} {admin.LastName}".Trim(),
            TotalRevenue = revenue,
            PlatformFeePercent = _feePercent,
            PlatformFees = fees,
            ProviderEarnings = revenue - fees,
            PaidBookings = paidCount,
            AverageBookingValue = paidCount == 0 ? 0 : Math.Round(revenue / paidCount, 2),
            TotalBookings = statusRows.Sum(r => r.Count),
            Completed = completed,
            Cancelled = cancelled,
            Rejected = rejected,
            CompletionRatePercent = finished == 0 ? 0 : Math.Round(completed * 100m / finished, 1),
            ByStatus = order.Select(s => new ReportStatusCount(s, CountOf(s))).ToList(),
            TopCategories = top,
            ActiveCustomers = activeCustomers,
            ActiveProviders = activeProviders,
            PendingApplications = pending,
            NewCustomers = newCustomers,
            NewProviders = newProviders,
            Trend = trend
        };
        return new(true, vm);
    }
}
