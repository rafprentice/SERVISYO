using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

public interface INoShowService
{
    /// Database v2.12: cancels every Accepted booking that the customer had paid (or promised to pay in cash) but the
    /// provider did not start within 2 hours of its scheduled time (Philippine time): a provider no-show. A test
    /// e-wallet payment is refunded in the same change; a cash promise simply ends (nothing was paid). Unpaid quotations
    /// are left alone (nothing was scheduled; the admin list shows them once their date has passed).
    /// Returns how many bookings were cancelled. Safe to run again at any time.
    Task<int> CancelMissedStartsAsync(CancellationToken ct = default);
}

public class NoShowService : INoShowService
{
    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<NoShowService> _logger;
    private readonly IBookingLiveUpdates? _live;

    public NoShowService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<NoShowService>? logger = null, IBookingLiveUpdates? live = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NoShowService>.Instance;
        _live = live;
    }

    public async Task<int> CancelMissedStartsAsync(CancellationToken ct = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var manilaNow = PhilippineTime.FromUtc(nowUtc);
        var today = DateOnly.FromDateTime(manilaNow);

        // the candidates are few (scheduled, not started, booked for today or earlier); the exact minutes are checked here
        var candidates = await _db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatuses.Accepted && b.StartedAt == null && b.PaymentDetail != null && b.BookingDate <= today)
            .Select(b => new { b.BookingId, b.BookingDate, b.BookingTime })
            .ToListAsync(ct);
        var due = candidates
            .Where(c => StartRules.MinutesAfter(c.BookingDate, c.BookingTime, manilaNow) > StartRules.NoShowAfterMinutes)
            .Select(c => c.BookingId)
            .OrderBy(id => id)
            .ToList();

        var cancelled = 0;
        foreach (var id in due)
        {
            if (await CancelOneAsync(id, nowUtc, manilaNow, ct)) cancelled++;
        }
        if (cancelled > 0)
            _logger.LogWarning("{Count} booking(s) cancelled automatically: {Reason}.", cancelled, StartRules.NoShowReason);
        return cancelled;
    }

    /// One booking, in its own save: a booking that changed meanwhile (started at the last second, or refused by the
    /// database) is skipped and does not stop the others.
    private async Task<bool> CancelOneAsync(int bookingId, DateTime nowUtc, DateTime manilaNow, CancellationToken ct)
    {
        try
        {
            var booking = await _db.Bookings
                .Include(b => b.PaymentDetail)
                .Include(b => b.Customer)
                .Include(b => b.Provider)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);
            // read again: the provider may have started it a moment ago
            if (booking is null || booking.Status != BookingStatuses.Accepted || booking.StartedAt is not null || booking.PaymentDetail is null
                || StartRules.MinutesAfter(booking.BookingDate, booking.BookingTime, manilaNow) <= StartRules.NoShowAfterMinutes)
                return false;

            // the System Logs lines come from the audit triggers (actor "System": no web request here):
            //   "System updated Booking #N: status 'Accepted' -> 'Cancelled' (Provider No-Show - missed scheduled start window)."
            booking.Status = BookingStatuses.Cancelled;
            booking.UpdatedAt = nowUtc;
            var refunded = false;
            if (booking.PaymentDetail.PaymentStatus == PaymentRecordStatuses.Paid)
            {
                booking.PaymentDetail.PaymentStatus = PaymentRecordStatuses.Refunded;    // test e-wallet: the money goes back
                refunded = true;
            }
            if (booking.PaymentStatus == PaymentStatuses.Paid)
                booking.PaymentStatus = PaymentStatuses.Refunded;

            await _db.SaveChangesAsync(ct);

            _logger.LogWarning("Booking {BookingId} cancelled automatically ({Reason}); scheduled {Date} {Time}, provider {ProviderId}, refunded: {Refunded}.",
                booking.BookingId, StartRules.NoShowReason, booking.BookingDate, booking.BookingTime, booking.ProviderId, refunded);

            if (_live is not null)
                await _live.BookingChangedAsync(
                    new BookingChange(booking.BookingId, booking.Status, PaymentProgress.Effective(booking.PaymentStatus, booking.PaymentDetail.PaymentStatus), "no-show"),
                    booking.Customer.UserId, booking.Provider.UserId);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // a concurrency conflict, or the database refused it (trg_Bookings_StatusFlow 50162: its clock says it is
            // not 2 hours yet): nothing was saved; the next run looks again
            _logger.LogWarning(ex, "Booking {BookingId}: the no-show cancellation was not saved; it will be tried again.", bookingId);
            return false;
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }
}

/// Database v2.12: runs the no-show check 1 minute after the website starts, then every 5 minutes.
/// Note: on hosting that stops the website when nobody visits (e.g. the MonsterASP free plan), it only runs while the
/// site is awake; the first visit wakes it and the check runs a minute later. Until then the provider's card already
/// shows that the start window was missed (and the start is refused).
public class NoShowCancellationService : BackgroundService
{
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NoShowCancellationService> _logger;

    public NoShowCancellationService(IServiceScopeFactory scopes, ILogger<NoShowCancellationService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
            await RunOnceAsync(stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // the website is stopping
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<INoShowService>().CancelMissedStartsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // never stop the website; the next run tries again
            _logger.LogError(ex, "The no-show check failed; it runs again in 5 minutes.");
        }
    }
}
