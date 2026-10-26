using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

public enum PaymentOutcome { Paid, NotAllowed, NotFound, NotPayable, AlreadyPaid, Changed }

public record PaymentResult(PaymentOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == PaymentOutcome.Paid;
}

public interface IBookingPaymentService
{
    /// DEMO payment ("Approve & Pay"): no money moves and no payment gateway is called.
    /// The customer accepts the provider's quotation, and the booking's PAYMENT_STATUS becomes Paid.
    Task<PaymentResult> ApproveAndPayAsync(int userId, int bookingId, CancellationToken ct = default);
}

/// The booking STATUS column keeps the six values from the project PDF (Pending, Accepted, Rejected, In Progress,
/// Completed, Cancelled). "Paid" is the separate PAYMENT_STATUS column, so the progress meter reads both:
/// Accepted + Unpaid = Quotation, Accepted + Paid = Paid / Scheduled, then In Progress and Completed.
public class BookingPaymentService : IBookingPaymentService
{
    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IBookingLiveUpdates? _live;
    private readonly ILogger<BookingPaymentService> _logger;

    public BookingPaymentService(ServisyoDbContext db, TimeProvider? clock = null, IBookingLiveUpdates? live = null,
        ILogger<BookingPaymentService>? logger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _live = live;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingPaymentService>.Instance;
    }

    public async Task<PaymentResult> ApproveAndPayAsync(int userId, int bookingId, CancellationToken ct = default)
    {
        // 1. who: from the sign-in cookie; only an Active customer account
        var customerId = await _db.Customers.AsNoTracking()
            .Where(c => c.UserId == userId && c.User.Role == Roles.Customer && c.User.AccountStatus == AccountStatuses.Active)
            .Select(c => (int?)c.CustomerId)
            .FirstOrDefaultAsync(ct);
        if (customerId is null) return new(PaymentOutcome.NotAllowed, string.Empty);

        // 2. the booking must be THIS customer's; someone else's looks exactly like a missing one
        var booking = await _db.Bookings
            .Include(b => b.Provider)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.CustomerId == customerId, ct);
        if (booking is null)
        {
            _logger.LogWarning("Customer {CustomerId} tried to pay booking {BookingId}, which is not theirs or does not exist.", customerId, bookingId);
            return new(PaymentOutcome.NotFound, "That booking could not be found.");
        }

        // 3. only a quotation the provider has given (Accepted) and that is still unpaid
        if (booking.PaymentStatus == PaymentStatuses.Paid)
            return new(PaymentOutcome.AlreadyPaid, $"Booking #{bookingId} is already paid.");
        if (booking.Status != BookingStatuses.Accepted || booking.PaymentStatus != PaymentStatuses.Unpaid)
            return new(PaymentOutcome.NotPayable, booking.Status == BookingStatuses.Pending
                ? $"Booking #{bookingId} has no quotation yet. You can pay once the provider accepts it."
                : $"Booking #{bookingId} is {booking.Status.ToLowerInvariant()}, so it can't be paid.");

        // 4. mark it paid and tell the provider, together or not at all
        var now = _clock.GetUtcNow().UtcDateTime;
        booking.PaymentStatus = PaymentStatuses.Paid;
        booking.UpdatedAt = now;
        var amount = "₱" + booking.TotalFee.ToString("#,##0.##", System.Globalization.CultureInfo.InvariantCulture);
        _db.Notifications.Add(new Notification
        {
            UserId = booking.Provider.UserId,
            Message = $"Booking #{booking.BookingId} was approved and paid ({amount}). It is now scheduled.",
            CreatedAt = now
        });

        try
        {
            await _db.SaveChangesAsync(ct);   // the booking row has a concurrency token: a change in between is refused
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Booking {BookingId} changed while customer {CustomerId} was paying.", bookingId, customerId);
            return new(PaymentOutcome.Changed, $"Booking #{bookingId} was just updated by the provider. Please check it and try again.");
        }

        _logger.LogInformation("Customer {CustomerId} paid booking {BookingId} (demo payment, {Amount}).", customerId, bookingId, amount);

        if (_live is not null)
            await _live.BookingChangedAsync(new BookingChange(booking.BookingId, booking.Status, booking.PaymentStatus, "paid"),
                userId, booking.Provider.UserId);

        return new(PaymentOutcome.Paid, $"Payment received for booking #{bookingId} ({amount}). Your booking is confirmed and scheduled.");
    }
}
