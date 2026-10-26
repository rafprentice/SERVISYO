using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface ICustomerBookingsService
{
    /// "My Bookings" for the signed-in customer: tab counts plus one page of bookings for the chosen tab.
    /// Allowed = false when that user is not an Active customer (the controller then ends the session).
    Task<CustomerBookingsResult> GetAsync(int userId, string? tab, int page, CancellationToken ct = default);

    /// Cancel one Pending booking of the signed-in customer. The provider is told about it.
    Task<CustomerCancelResult> CancelAsync(int userId, int bookingId, CancellationToken ct = default);
}

public class CustomerBookingsService : ICustomerBookingsService
{
    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomerBookingsService> _logger;

    private readonly IBookingLiveUpdates? _live;

    public CustomerBookingsService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<CustomerBookingsService>? logger = null, IBookingLiveUpdates? live = null)
    {
        _db = db;
        _live = live;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CustomerBookingsService>.Instance;
    }

    private sealed record Who(int CustomerId, string FirstName, string LastName);

    /// The customer is found from the SIGNED-IN user id only (never from the browser).
    /// Only an Active customer account may use the page.
    private async Task<Who?> FindAsync(int userId, CancellationToken ct) =>
        await _db.Customers.AsNoTracking()
            .Where(c => c.UserId == userId
                     && c.User.Role == Roles.Customer
                     && c.User.AccountStatus == AccountStatuses.Active)
            .Select(c => new Who(c.CustomerId, c.User.FirstName, c.User.LastName))
            .FirstOrDefaultAsync(ct);

    // ------------------------------------------------------------------ read

    private sealed class Row
    {
        public int BookingId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public decimal TotalFee { get; set; }
        public DateOnly BookingDate { get; set; }
        public TimeOnly BookingTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ProviderFirstName { get; set; } = string.Empty;
        public string ProviderLastName { get; set; } = string.Empty;
        public string? ServiceLocation { get; set; }
        public string? ServiceDescription { get; set; }
        public bool HasReview { get; set; }
    }

    public async Task<CustomerBookingsResult> GetAsync(int userId, string? tab, int page, CancellationToken ct = default)
    {
        var who = await FindAsync(userId, ct);
        if (who is null) return new(false, null);

        var activeTab = CustomerBookingTab.Find(tab);

        // Every query below starts from THIS customer's bookings, so nobody else's booking can ever show up.
        var mine = _db.Bookings.AsNoTracking().Where(b => b.CustomerId == who.CustomerId);

        // ---- tab counts: one grouped query over all of this customer's bookings ----
        var grouped = await mine.GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var byStatus = grouped.ToDictionary(g => g.Status, g => g.N);

        // "All" always; a status tab only when the customer has at least one booking in it (or it is the tab being viewed)
        var tabs = CustomerBookingTab.All
            .Select(t => new CustomerBookingTabViewModel
            {
                Key = t.Key,
                Label = t.Label,
                Count = t.Status is null ? byStatus.Values.Sum() : byStatus.GetValueOrDefault(t.Status)
            })
            .Where(t => t.Key == CustomerBookingTab.AllKey || t.Count > 0 || t.Key == activeTab.Key)
            .ToList();

        var total = tabs.First(t => t.Key == activeTab.Key).Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)CustomerBookingsPageViewModel.PageSize));
        var currentPage = Math.Clamp(page, 1, pageCount);

        // ---- one page of cards, newest request first ----
        var query = activeTab.Status is null ? mine : mine.Where(b => b.Status == activeTab.Status);
        var rows = await query
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BookingId)
            .Skip((currentPage - 1) * CustomerBookingsPageViewModel.PageSize)
            .Take(CustomerBookingsPageViewModel.PageSize)
            .Select(b => new Row
            {
                BookingId = b.BookingId,
                Status = b.Status,
                PaymentStatus = b.PaymentStatus,
                TotalFee = b.TotalFee,
                BookingDate = b.BookingDate,
                BookingTime = b.BookingTime,
                CreatedAt = b.CreatedAt,
                ProviderFirstName = b.Provider.User.FirstName,
                ProviderLastName = b.Provider.User.LastName,
                ServiceLocation = b.ServiceLocation,
                ServiceDescription = b.ServiceDescription,
                HasReview = _db.Reviews.Any(r => r.BookingId == b.BookingId)
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.BookingId).ToList();
        var titles = await _db.GetBookingTitlesAsync(ids, ct);

        // the category of a booking comes from the services in it (a booking has no category column of its own)
        var categoryRows = await _db.BookingServices.AsNoTracking()
            .Where(bs => ids.Contains(bs.BookingId))
            .OrderBy(bs => bs.BookingServiceId)
            .Select(bs => new { bs.BookingId, Name = bs.Service.Category.CategoryName })
            .ToListAsync(ct);
        var categories = categoryRows.GroupBy(x => x.BookingId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct()));

        var fullName = $"{who.FirstName} {who.LastName}".Trim();
        var vm = new CustomerBookingsPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Customer",
                Subtitle = "Customer Account",
                FirstName = who.FirstName,
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            ActiveTab = activeTab.Key,
            Tabs = tabs,
            Page = currentPage,
            TotalMatching = total,
            Cards = rows.Select(r => new CustomerBookingCardViewModel
            {
                BookingId = r.BookingId,
                Title = titles.TryGetValue(r.BookingId, out var t) ? t : "Service booking",
                ProviderName = $"{r.ProviderFirstName} {r.ProviderLastName}".Trim(),
                CategoryText = categories.TryGetValue(r.BookingId, out var c) ? c : null,
                Status = r.Status,
                Tone = ProviderBookingBadge.Tone(r.Status),         // the same status colours as the provider's page
                WhenText = BookingFormat.DateTimeText(r.BookingDate, r.BookingTime),
                Location = string.IsNullOrWhiteSpace(r.ServiceLocation) ? null : r.ServiceLocation.Trim(),
                SubmittedText = BookingFormat.SubmittedText(r.CreatedAt),
                Notes = string.IsNullOrWhiteSpace(r.ServiceDescription) ? null : r.ServiceDescription.Trim(),
                CanCancel = r.Status == BookingStatuses.Pending,
                CanReview = r.Status == BookingStatuses.Completed && !r.HasReview,
                IsReviewed = r.Status == BookingStatuses.Completed && r.HasReview,
                CanPay = r.Status == BookingStatuses.Accepted && r.PaymentStatus == PaymentStatuses.Unpaid,
                IsPaid = r.PaymentStatus == PaymentStatuses.Paid,
                Fee = r.TotalFee
            }).ToList()
        };

        return new(true, vm);
    }

    // ----------------------------------------------------------------- write

    public async Task<CustomerCancelResult> CancelAsync(int userId, int bookingId, CancellationToken ct = default)
    {
        // 1. who is asking: found from the sign-in cookie, and only an Active customer may cancel anything
        var who = await FindAsync(userId, ct);
        if (who is null) return new(false, CustomerCancelOutcome.NotFound, string.Empty);

        // 2. the booking must belong to THIS customer; someone else's booking looks exactly like a missing one
        var booking = await _db.Bookings
            .Include(b => b.Provider)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.CustomerId == who.CustomerId, ct);
        if (booking is null)
        {
            _logger.LogWarning("Customer {CustomerId} tried to cancel booking {BookingId}, which is not theirs or does not exist.",
                who.CustomerId, bookingId);
            return new(true, CustomerCancelOutcome.NotFound, "That booking could not be found.");
        }

        // 3. only a Pending request can be cancelled (project rule). Once the provider has answered, it is between them.
        if (booking.Status != BookingStatuses.Pending)
        {
            _logger.LogInformation("Booking {BookingId} is {Status}; cancel refused.", bookingId, booking.Status);
            return new(true, CustomerCancelOutcome.NotPending, NotPendingText(bookingId, booking.Status));
        }

        // 4. change the status and tell the provider, together or not at all
        var now = _clock.GetUtcNow().UtcDateTime;
        booking.Status = BookingStatuses.Cancelled;
        booking.UpdatedAt = now;
        _db.Notifications.Add(new Notification
        {
            UserId = booking.Provider.UserId,
            Message = $"Booking #{booking.BookingId} was cancelled by the customer.",
            CreatedAt = now
        });

        try
        {
            // the booking row carries a concurrency token, so if the provider accepted or rejected it a moment ago
            // this save is refused instead of silently overwriting their answer
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Booking {BookingId} changed while customer {CustomerId} was cancelling it.", bookingId, who.CustomerId);
            return new(true, CustomerCancelOutcome.Changed,
                $"Booking #{bookingId} was just updated by the provider. Check its new status below — a request that has been answered can no longer be cancelled here.");
        }

        _logger.LogInformation("Customer {CustomerId} cancelled booking {BookingId}.", who.CustomerId, bookingId);

        if (_live is not null)
            await _live.BookingChangedAsync(new BookingChange(booking.BookingId, booking.Status, booking.PaymentStatus, "cancelled"),
                booking.Provider.UserId, userId);
        return new(true, CustomerCancelOutcome.Success, $"Booking #{bookingId} has been cancelled. The provider has been notified.");
    }

    private static string NotPendingText(int bookingId, string status) => status switch
    {
        BookingStatuses.Cancelled => $"Booking #{bookingId} is already cancelled.",
        _ => $"Booking #{bookingId} is {status.ToLowerInvariant()}, so it can no longer be cancelled here. Only pending requests can be cancelled."
    };
}
