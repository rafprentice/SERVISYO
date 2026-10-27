using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public enum PaymentOutcome { Paid, NotAllowed, NotFound, NotPayable, AlreadyPaid, Changed, InvalidInput }

public record PaymentResult(PaymentOutcome Outcome, string Message, string? Reference = null)
{
    public bool Succeeded => Outcome == PaymentOutcome.Paid;
}

public interface IBookingPaymentService
{
    /// DEMO payment ("Approve & Pay"): no money moves and no payment gateway is called.
    /// The customer accepts the provider's quotation and chooses how to pay:
    ///   Cash on Service Completion -> a PAYMENTS row "PendingCollection" (+ CASH_PAYMENT_DETAILS); the booking stays Unpaid
    ///                                 until the provider confirms the cash, but it is scheduled now.
    ///   Mock E-Wallet (GCash/Maya)  -> a PAYMENTS row "Paid"; the booking's PAYMENT_STATUS becomes Paid.
    Task<PaymentResult> ApproveAndPayAsync(int userId, int bookingId, PaymentChoiceForm? choice, CancellationToken ct = default);

    /// "Confirm Cash Received": the assigned provider confirms the cash for a Completed Cash-on-Completion booking.
    /// The cash record is marked collected, the payment and the booking become Paid.
    Task<BookingActionResult> ConfirmCashReceivedAsync(int userId, int bookingId, CancellationToken ct = default);
}

/// The booking STATUS column keeps the six values from the project PDF (Pending, Accepted, Rejected, In Progress,
/// Completed, Cancelled). Payment lives in BOOKINGS.PAYMENT_STATUS (Unpaid / Paid / Refunded, as in the PDF) plus the
/// PAYMENTS record, so the progress meter reads both: Accepted + nothing chosen = Quotation, Accepted + paid or
/// cash promised = Paid / Scheduled, then In Progress and Completed.
public class BookingPaymentService : IBookingPaymentService
{
    private const int ManilaOffsetHours = 8;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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

    // ================================================================== customer: Approve & Pay

    public async Task<PaymentResult> ApproveAndPayAsync(int userId, int bookingId, PaymentChoiceForm? choice, CancellationToken ct = default)
    {
        // 1. who: from the sign-in cookie; only an Active customer account
        var customerId = await _db.Customers.AsNoTracking()
            .Where(c => c.UserId == userId && c.User.Role == Roles.Customer && c.User.AccountStatus == AccountStatuses.Active)
            .Select(c => (int?)c.CustomerId)
            .FirstOrDefaultAsync(ct);
        if (customerId is null) return new(PaymentOutcome.NotAllowed, string.Empty);

        // 2. what was chosen: only the two known options, checked here (never trust the window's own checks)
        choice ??= new PaymentChoiceForm();
        var method = PaymentMethods.Normalize(choice.Method);
        if (method is null) return new(PaymentOutcome.InvalidInput, "Please choose how you want to pay: cash on completion or the test e-wallet.");

        string? wallet = null;
        if (method == PaymentMethods.MockEWallet)
        {
            wallet = MockWallets.Normalize(choice.Wallet);
            if (wallet is null) return new(PaymentOutcome.InvalidInput, "Please choose GCash or Maya for the test e-wallet payment.");
            if (!MockWallets.IsMobileNumber(choice.MobileNumber))
                return new(PaymentOutcome.InvalidInput, "Please enter a valid mobile number for the test e-wallet, like 0917-000-0000.");
        }

        // 3. the booking must be THIS customer's; someone else's looks exactly like a missing one
        var booking = await _db.Bookings
            .Include(b => b.Provider)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.CustomerId == customerId, ct);
        if (booking is null)
        {
            _logger.LogWarning("Customer {CustomerId} tried to pay booking {BookingId}, which is not theirs or does not exist.", customerId, bookingId);
            return new(PaymentOutcome.NotFound, "That booking could not be found.");
        }

        // 4. only a quotation the provider has given (Accepted) that has no payment yet
        if (booking.Payment is not null)
            return new(PaymentOutcome.AlreadyPaid, booking.Payment.PaymentMethod == PaymentMethods.CashOnCompletion
                ? $"Booking #{bookingId} is already approved: you chose to pay in cash when the job is done."
                : $"Booking #{bookingId} is already paid.");
        if (booking.PaymentStatus == PaymentStatuses.Paid)
            return new(PaymentOutcome.AlreadyPaid, $"Booking #{bookingId} is already paid.");
        if (booking.Status != BookingStatuses.Accepted || booking.PaymentStatus != PaymentStatuses.Unpaid)
            return new(PaymentOutcome.NotPayable, booking.Status == BookingStatuses.Pending
                ? $"Booking #{bookingId} has no quotation yet. You can pay once the provider accepts it."
                : $"Booking #{bookingId} is {booking.Status.ToLowerInvariant()}, so it can't be paid.");

        // 5. the payment record. The amount always comes from the booking, never from the browser.
        var now = _clock.GetUtcNow().UtcDateTime;
        var amount = PaymentNotes.Money(booking.TotalFee);
        var isCash = method == PaymentMethods.CashOnCompletion;
        var reference = isCash
            ? $"CASH-{now.AddHours(ManilaOffsetHours).Year}-{booking.BookingId:D4}"     // one payment per booking, so this is unique
            : await WalletReferenceAsync(wallet!, choice.Reference, ct);

        var payment = new Payment
        {
            BookingId = booking.BookingId,
            Amount = booking.TotalFee,
            PaymentMethod = method,
            PaymentStatus = isCash ? PaymentRecordStatuses.PendingCollection : PaymentRecordStatuses.Paid,
            TransactionReference = reference,
            CreatedAt = now,
            PaidAt = isCash ? null : now,
            CashDetails = isCash ? new CashPaymentDetails { IsCollectedByProvider = false, CollectedAt = null } : null
        };
        booking.Payment = payment;
        if (!isCash) booking.PaymentStatus = PaymentStatuses.Paid;   // the money is "in" at once (test e-wallet)
        booking.UpdatedAt = now;                                       // always touch the row: its concurrency token guards the change

        _db.Notifications.Add(new Notification
        {
            UserId = booking.Provider.UserId,
            Message = isCash
                ? $"Booking #{booking.BookingId} was approved. The customer will pay {amount} in cash when the job is done. It is now scheduled."
                : $"Booking #{booking.BookingId} was approved and paid by {wallet} (test, {amount}, ref {reference}). It is now scheduled.",
            CreatedAt = now
        });

        // 6. booking + payment + notification: together or not at all (one SaveChanges = one transaction)
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Booking {BookingId} changed while customer {CustomerId} was paying.", bookingId, customerId);
            return new(PaymentOutcome.Changed, $"Booking #{bookingId} was just updated. Please check it and try again.");
        }
        catch (DbUpdateException ex)
        {
            // the unique index on PAYMENTS.BOOKING_ID: the same booking was paid a moment ago (two tabs, double click)
            _db.ChangeTracker.Clear();
            var paidMeanwhile = await _db.Payments.AsNoTracking().AnyAsync(p => p.BookingId == bookingId, ct);
            _logger.LogWarning(ex, "Saving the payment for booking {BookingId} failed (paid meanwhile: {Paid}).", bookingId, paidMeanwhile);
            return paidMeanwhile
                ? new(PaymentOutcome.AlreadyPaid, $"Booking #{bookingId} is already paid.")
                : new(PaymentOutcome.Changed, "The payment could not be saved. Please try again.");
        }

        _logger.LogInformation("Customer {CustomerId} approved booking {BookingId}: {Method}, {Amount}, ref {Reference} (demo payment).",
            customerId, bookingId, method, amount, reference);

        if (_live is not null)
            await _live.BookingChangedAsync(
                new BookingChange(booking.BookingId, booking.Status, PaymentProgress.Effective(booking.PaymentStatus, payment.PaymentStatus), "paid"),
                userId, booking.Provider.UserId);

        return isCash
            ? new(PaymentOutcome.Paid, $"Booking #{bookingId} is confirmed and scheduled. Pay {amount} in cash to {booking.Provider.BusinessName} when the job is done (ref {reference}).", reference)
            : new(PaymentOutcome.Paid, $"Test payment of {amount} received via {wallet} (ref {reference}). Your booking is confirmed and scheduled.", reference);
    }

    /// The test reference from the window is kept when it looks right (GCASH-/MAYA- + 8 digits, the chosen wallet's
    /// prefix) and was never used; otherwise the server makes a new one. The reference is only a label: the amount
    /// and the status are always decided here.
    private async Task<string> WalletReferenceAsync(string wallet, string? proposed, CancellationToken ct)
    {
        var prefix = MockWallets.Prefix(wallet);
        var candidate = proposed?.Trim().ToUpperInvariant();
        if (candidate is not null && MockWallets.ReferencePattern.IsMatch(candidate) && candidate.StartsWith(prefix + "-", StringComparison.Ordinal)
            && !await _db.Payments.AnyAsync(p => p.TransactionReference == candidate, ct))
            return candidate;

        for (var i = 0; i < 5; i++)
        {
            var made = prefix + "-" + RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", Inv);
            if (!await _db.Payments.AnyAsync(p => p.TransactionReference == made, ct)) return made;
        }
        return prefix + "-" + RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", Inv);   // the unique index is the last guard
    }

    // ================================================================== provider: Confirm Cash Received

    public async Task<BookingActionResult> ConfirmCashReceivedAsync(int userId, int bookingId, CancellationToken ct = default)
    {
        // 1. who: from the sign-in cookie; only an Active account with an Approved application (same rule as Booking Requests)
        var p = await _db.Providers.AsNoTracking()
            .Where(x => x.UserId == userId && x.User.Role == Roles.ServiceProvider && x.User.AccountStatus == AccountStatuses.Active)
            .Select(x => new { x.ProviderId, x.ApprovalStatus, x.BusinessName })
            .FirstOrDefaultAsync(ct);
        if (p is null) return new(ProviderDashboardAccess.NotAllowed, BookingActionOutcome.NotFound, string.Empty);
        if (p.ApprovalStatus == ApprovalStatuses.Pending) return new(ProviderDashboardAccess.Pending, BookingActionOutcome.NotFound, string.Empty);
        if (p.ApprovalStatus != ApprovalStatuses.Approved) return new(ProviderDashboardAccess.Rejected, BookingActionOutcome.NotFound, string.Empty);
        const ProviderDashboardAccess ok = ProviderDashboardAccess.Allowed;

        // 2. the booking must be THIS provider's; someone else's looks exactly like a missing one
        var booking = await _db.Bookings
            .Include(b => b.Customer)
            .Include(b => b.Payment).ThenInclude(pay => pay!.CashDetails)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.ProviderId == p.ProviderId, ct);
        if (booking is null)
        {
            _logger.LogWarning("Provider {ProviderId} tried to confirm cash for booking {BookingId}, which is not theirs or does not exist.", p.ProviderId, bookingId);
            return new(ok, BookingActionOutcome.NotFound, "That booking could not be found.");
        }

        // 3. only a Completed job whose customer chose cash, and only once
        var payment = booking.Payment;
        if (payment is null || payment.PaymentMethod != PaymentMethods.CashOnCompletion || payment.CashDetails is null)
            return new(ok, BookingActionOutcome.InvalidTransition, $"Booking #{bookingId} is not a cash-on-completion booking.");
        if (payment.CashDetails.IsCollectedByProvider || payment.PaymentStatus != PaymentRecordStatuses.PendingCollection)
            return new(ok, BookingActionOutcome.InvalidTransition, $"The cash for booking #{bookingId} was already confirmed.");
        if (booking.Status != BookingStatuses.Completed)
            return new(ok, BookingActionOutcome.InvalidTransition,
                $"Booking #{bookingId} is {booking.Status.ToLowerInvariant()}. Confirm the cash after you mark the job completed.");

        // 4. collected: the cash record, the payment and the booking, plus a note to the customer, together or not at all
        var now = _clock.GetUtcNow().UtcDateTime;
        var amount = PaymentNotes.Money(payment.Amount);
        payment.CashDetails.IsCollectedByProvider = true;
        payment.CashDetails.CollectedAt = now;
        payment.PaymentStatus = PaymentRecordStatuses.Paid;
        payment.PaidAt = now;
        booking.PaymentStatus = PaymentStatuses.Paid;
        booking.UpdatedAt = now;
        _db.Notifications.Add(new Notification
        {
            UserId = booking.Customer.UserId,
            Message = $"{p.BusinessName} confirmed receiving your {amount} cash payment for booking #{booking.BookingId} (ref {payment.TransactionReference}).",
            CreatedAt = now
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Booking {BookingId} changed while provider {ProviderId} was confirming the cash.", bookingId, p.ProviderId);
            return new(ok, BookingActionOutcome.Changed, $"Booking #{bookingId} was just changed. Review it and try again.");
        }

        _logger.LogInformation("Provider {ProviderId} confirmed {Amount} cash for booking {BookingId} (ref {Reference}).",
            p.ProviderId, amount, bookingId, payment.TransactionReference);

        if (_live is not null)
            await _live.BookingChangedAsync(new BookingChange(booking.BookingId, booking.Status, booking.PaymentStatus, "cash-received"),
                booking.Customer.UserId, userId);

        return new(ok, BookingActionOutcome.Success, $"Cash received for booking #{bookingId} ({amount}). The payment is now complete.");
    }
}
