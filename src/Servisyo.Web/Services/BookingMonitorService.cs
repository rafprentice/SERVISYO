using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IBookingMonitorService
{
    /// The platform-wide booking list for the admin (read-only). Returns null when the signed-in user is not an Active administrator.
    Task<BookingsPageViewModel?> GetBookingsAsync(int adminUserId, BookingListQuery query, CancellationToken ct = default);

    /// Everything about one booking for the admin's "View" pop-up (read-only). Allowed = false when the signed-in user
    /// is not an Active administrator; Details = null when there is no such booking.
    Task<BookingDetailsLookup> GetDetailsAsync(int adminUserId, int bookingId, CancellationToken ct = default);
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

    private const int CebuOffsetHours = 8;      // the Philippines has no daylight saving

    private readonly ServisyoDbContext _db;

    public BookingMonitorService(ServisyoDbContext db) => _db = db;

    public async Task<BookingDetailsLookup> GetDetailsAsync(int adminUserId, int bookingId, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return new(false, null);

        var b = await _db.Bookings.AsNoTracking()
            .Where(x => x.BookingId == bookingId)
            .Select(x => new
            {
                x.BookingId, x.BookingDate, x.BookingTime, x.TotalDurationMinutes, x.ServiceLocation, x.ServiceDescription,
                x.TotalFee, x.Status, x.PaymentStatus, x.CreatedAt,
                CustomerFirst = x.Customer.User.FirstName, CustomerLast = x.Customer.User.LastName,
                CustomerEmail = x.Customer.User.Email, CustomerPhone = x.Customer.User.PhoneNumber,
                ProviderFirst = x.Provider.User.FirstName, ProviderLast = x.Provider.User.LastName,
                x.Provider.BusinessName, ProviderEmail = x.Provider.User.Email, ProviderPhone = x.Provider.User.PhoneNumber,
                PayAmount = x.Payment != null ? (decimal?)x.Payment.Amount : null,
                PayMethod = x.Payment != null ? x.Payment.PaymentMethod : null,
                PayReference = x.Payment != null ? x.Payment.TransactionReference : null,
                PayStatus = x.Payment != null ? x.Payment.PaymentStatus : null
            })
            .FirstOrDefaultAsync(ct);
        if (b is null) return new(true, null);

        var services = await _db.BookingServices.AsNoTracking()
            .Where(s => s.BookingId == bookingId)
            .OrderBy(s => s.BookingServiceId)
            .Select(s => new BookingDetailsServiceLine(
                s.Service.ServiceName, s.Service.Category.CategoryName, s.Service.Description, s.PriceSnapshot, s.DurationMinutesSnapshot))
            .ToListAsync(ct);

        return new(true, new BookingDetailsViewModel
        {
            Shell = shell,
            BookingId = b.BookingId,
            Reference = BookingDetailsViewModel.ReferenceFor(b.BookingId, b.CreatedAt),
            Status = AdminBookingDisplay.For(b.Status),
            Date = b.BookingDate,
            StartTime = b.BookingTime,
            EndTime = b.BookingTime.AddMinutes(b.TotalDurationMinutes),
            DurationMinutes = b.TotalDurationMinutes,
            CreatedAtCebu = b.CreatedAt.AddHours(CebuOffsetHours),
            Services = services,
            RequestDescription = string.IsNullOrWhiteSpace(b.ServiceDescription) ? null : b.ServiceDescription.Trim(),
            QuotedTotal = b.TotalFee,
            CustomerName = $"{b.CustomerFirst} {b.CustomerLast}".Trim(),
            CustomerEmail = b.CustomerEmail,
            CustomerPhone = string.IsNullOrWhiteSpace(b.CustomerPhone) ? null : b.CustomerPhone,
            ServiceAddress = string.IsNullOrWhiteSpace(b.ServiceLocation) ? null : b.ServiceLocation,
            ProviderName = $"{b.ProviderFirst} {b.ProviderLast}".Trim(),
            BusinessName = b.BusinessName,
            ProviderEmail = b.ProviderEmail,
            ProviderPhone = string.IsNullOrWhiteSpace(b.ProviderPhone) ? null : b.ProviderPhone,
            TotalAmount = b.PayAmount ?? b.TotalFee,
            PaymentMethod = BookingDetailsViewModel.PaymentMethodText(b.PayMethod, b.PayReference),
            TransactionReference = string.IsNullOrWhiteSpace(b.PayReference) ? null : b.PayReference,
            Payment = PaymentDisplay.For(PaymentProgress.Effective(b.PaymentStatus, b.PayStatus))
        });
    }

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
                PayStatus = b.Payment != null ? b.Payment.PaymentStatus : null,
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
                Payment = PaymentDisplay.For(PaymentProgress.Effective(r.PaymentStatus, r.PayStatus)),
                Status = AdminBookingDisplay.For(r.Status)
            }).ToList()
        };
    }
}
