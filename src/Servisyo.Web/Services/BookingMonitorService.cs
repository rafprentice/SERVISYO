using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IBookingMonitorService
{
    /// The platform-wide booking list for the admin (read-only). Returns null when the signed-in user is not an Active administrator.
    Task<BookingsPageViewModel?> GetBookingsAsync(int adminUserId, BookingListQuery query, CancellationToken ct = default);
}

public class BookingMonitorService : IBookingMonitorService
{
    public const int PageSize = 20;
    private const int MaxSearchLength = 100;

    // order of the tabs in the design
    private static readonly string[] StatusOrder =
    {
        BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress,
        BookingStatuses.Completed, BookingStatuses.Rejected, BookingStatuses.Cancelled
    };

    private readonly ServisyoDbContext _db;

    public BookingMonitorService(ServisyoDbContext db) => _db = db;

    public async Task<BookingsPageViewModel?> GetBookingsAsync(int adminUserId, BookingListQuery query, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return null;

        var all = _db.Bookings.AsNoTracking();

        // ---- Tab numbers (always for ALL bookings, whatever is typed in the search box) ----
        var counts = await all.GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(c => c.Count);

        var tabs = new List<BookingTabViewModel> { new(BookingFilters.All, "All", total) };
        tabs.AddRange(StatusOrder.Select(s =>
            new BookingTabViewModel(s, s, counts.Where(c => c.Status == s).Select(c => c.Count).FirstOrDefault())));

        // ---- Filter + search ----
        var status = BookingFilters.NormalizeStatus(query.Status);
        var search = (query.Q ?? string.Empty).Trim();
        if (search.Length > MaxSearchLength) search = search[..MaxSearchLength];

        var filtered = all;
        if (status != BookingFilters.All)
            filtered = filtered.Where(b => b.Status == status);

        if (search.Length > 0)
        {
            var term = search.ToLowerInvariant();
            var hasId = int.TryParse(term.TrimStart('#'), out var bookingId);   // "12" or "#12" finds booking 12
            filtered = filtered.Where(b =>
                (hasId && b.BookingId == bookingId)
                || (b.Customer.User.FirstName + " " + b.Customer.User.LastName).ToLower().Contains(term)
                || (b.Provider.User.FirstName + " " + b.Provider.User.LastName).ToLower().Contains(term)
                || b.Provider.BusinessName.ToLower().Contains(term)
                || b.Services.Any(s => s.Service.ServiceName.ToLower().Contains(term)));
        }

        var matching = await filtered.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize));
        var page = Math.Clamp(query.Page, 1, pageCount);

        // ---- This page (newest booking first) ----
        var rows = await filtered
            .OrderByDescending(b => b.BookingId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(b => new
            {
                b.BookingId, b.BookingDate, b.BookingTime, b.Status, b.PaymentStatus,
                CustomerFirst = b.Customer.User.FirstName, CustomerLast = b.Customer.User.LastName,
                ProviderFirst = b.Provider.User.FirstName, ProviderLast = b.Provider.User.LastName
            })
            .ToListAsync(ct);
        var titles = await _db.GetBookingTitlesAsync(rows.Select(r => r.BookingId).ToList(), ct);

        return new BookingsPageViewModel
        {
            Shell = shell,
            Q = search,
            Status = status,
            Tabs = tabs,
            CountAll = total,
            TotalMatching = matching,
            Page = page,
            PageCount = pageCount,
            Rows = rows.Select(r => new BookingRowViewModel
            {
                BookingId = r.BookingId,
                Service = titles.TryGetValue(r.BookingId, out var t) ? t : "Service booking",
                CustomerName = $"{r.CustomerFirst} {r.CustomerLast}".Trim(),
                ProviderName = $"{r.ProviderFirst} {r.ProviderLast}".Trim(),
                Date = r.BookingDate,
                Time = r.BookingTime,
                Payment = PaymentDisplay.For(r.PaymentStatus),
                Status = AdminBookingDisplay.For(r.Status)
            }).ToList()
        };
    }
}
