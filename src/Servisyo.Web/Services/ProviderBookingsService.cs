using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IProviderBookingsService
{
    /// The Booking Requests page for the signed-in provider: tab counts plus one page of bookings for the chosen tab.
    Task<ProviderBookingsResult> GetAsync(int userId, string? tab, int page, CancellationToken ct = default);

    /// Accept / reject / start / complete one booking of the signed-in provider (same status rules as
    /// usp_UpdateBookingStatus in the PDF). The customer is told about the new status.
    Task<BookingActionResult> ChangeStatusAsync(int userId, int bookingId, BookingAction action, CancellationToken ct = default);
}

public class ProviderBookingsService : IProviderBookingsService
{
    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProviderBookingsService> _logger;

    private readonly IBookingLiveUpdates? _live;

    public ProviderBookingsService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<ProviderBookingsService>? logger = null, IBookingLiveUpdates? live = null)
    {
        _db = db;
        _live = live;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderBookingsService>.Instance;
    }

    private sealed record Who(int ProviderId, string FullName, string BusinessName);

    /// The provider is found from the SIGNED-IN user id only (never from the browser).
    /// Only an Active account with an Approved application may work with booking requests.
    private async Task<(ProviderDashboardAccess Access, Who? Who)> FindAsync(int userId, CancellationToken ct)
    {
        var p = await _db.Providers.AsNoTracking()
            .Where(x => x.UserId == userId
                     && x.User.Role == Roles.ServiceProvider
                     && x.User.AccountStatus == AccountStatuses.Active)
            .Select(x => new { x.ProviderId, x.ApprovalStatus, x.User.FirstName, x.User.LastName, x.BusinessName })
            .FirstOrDefaultAsync(ct);

        if (p is null) return (ProviderDashboardAccess.NotAllowed, null);
        if (p.ApprovalStatus == ApprovalStatuses.Pending) return (ProviderDashboardAccess.Pending, null);
        if (p.ApprovalStatus != ApprovalStatuses.Approved) return (ProviderDashboardAccess.Rejected, null);

        return (ProviderDashboardAccess.Allowed, new Who(p.ProviderId, $"{p.FirstName} {p.LastName}".Trim(), p.BusinessName));
    }

    // ------------------------------------------------------------------ read

    private sealed class Row
    {
        public int BookingId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public DateOnly BookingDate { get; set; }
        public TimeOnly BookingTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? ServiceLocation { get; set; }
        public string? ServiceDescription { get; set; }
    }

    public async Task<ProviderBookingsResult> GetAsync(int userId, string? tab, int page, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, null);

        var activeTab = ProviderBookingTab.Find(tab);
        var mine = _db.Bookings.AsNoTracking().Where(b => b.ProviderId == who.ProviderId);

        // ---- tab counts: one grouped query over all of this provider's bookings ----
        var grouped = await mine.GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var byStatus = grouped.ToDictionary(g => g.Status, g => g.N);

        var tabs = ProviderBookingTab.All.Select(t => new ProviderBookingTabViewModel
        {
            Key = t.Key,
            Label = t.Label,
            Count = t.Status is null ? byStatus.Values.Sum() : byStatus.GetValueOrDefault(t.Status)
        }).ToList();

        var total = tabs.First(t => t.Key == activeTab.Key).Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)ProviderBookingsPageViewModel.PageSize));
        var currentPage = Math.Clamp(page, 1, pageCount);

        // ---- one page of cards, newest request first ----
        var query = activeTab.Status is null ? mine : mine.Where(b => b.Status == activeTab.Status);
        var rows = await query
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BookingId)
            .Skip((currentPage - 1) * ProviderBookingsPageViewModel.PageSize)
            .Take(ProviderBookingsPageViewModel.PageSize)
            .Select(b => new Row
            {
                BookingId = b.BookingId,
                Status = b.Status,
                PaymentStatus = b.PaymentStatus,
                BookingDate = b.BookingDate,
                BookingTime = b.BookingTime,
                CreatedAt = b.CreatedAt,
                CustomerName = b.Customer.User.FirstName + " " + b.Customer.User.LastName,
                ServiceLocation = b.ServiceLocation,
                ServiceDescription = b.ServiceDescription
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.BookingId).ToList();
        var titles = await _db.GetBookingTitlesAsync(ids, ct);

        // the category of a booking comes from the services in it (a booking has no category column of its own)
        var categoryRows = ids.Count == 0
            ? new List<(int BookingId, string Name)>()
            : (await _db.BookingServices.AsNoTracking()
                    .Where(bs => ids.Contains(bs.BookingId))
                    .OrderBy(bs => bs.BookingServiceId)
                    .Select(bs => new { bs.BookingId, Name = bs.Service.Category.CategoryName })
                    .ToListAsync(ct))
                .Select(x => (x.BookingId, x.Name)).ToList();
        var categories = categoryRows.GroupBy(x => x.BookingId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct()));

        var fullName = who.FullName;
        var vm = new ProviderBookingsPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Provider",
                FirstName = fullName.Split(' ')[0],
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                Subtitle = who.BusinessName,
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            ActiveTab = activeTab.Key,
            Tabs = tabs,
            Page = currentPage,
            TotalMatching = total,
            Cards = rows.Select(r => new ProviderBookingCardViewModel
            {
                BookingId = r.BookingId,
                Title = titles.TryGetValue(r.BookingId, out var t) ? t : "Service booking",
                CustomerName = r.CustomerName.Trim(),
                CategoryText = categories.TryGetValue(r.BookingId, out var c) ? c : null,
                Status = r.Status,
                Tone = ProviderBookingBadge.Tone(r.Status),
                WhenText = BookingFormat.DateTimeText(r.BookingDate, r.BookingTime),
                Location = string.IsNullOrWhiteSpace(r.ServiceLocation) ? null : r.ServiceLocation.Trim(),
                SubmittedText = BookingFormat.SubmittedText(r.CreatedAt),
                Notes = string.IsNullOrWhiteSpace(r.ServiceDescription) ? null : r.ServiceDescription.Trim(),
                PaymentStatus = r.PaymentStatus,
                Actions = BookingActionInfo.ForBooking(r.Status, r.PaymentStatus)
            }).ToList()
        };

        return new(ProviderDashboardAccess.Allowed, vm);
    }

    // ----------------------------------------------------------------- write

    private static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    private static string Done(BookingAction action) => action switch
    {
        BookingAction.Accept => "accepted",
        BookingAction.Reject => "rejected",
        BookingAction.Start => "marked in progress",
        _ => "marked completed"
    };

    private static string SuccessText(int bookingId, BookingAction action) => action switch
    {
        BookingAction.Accept => $"Booking #{bookingId} accepted. The customer has been notified.",
        BookingAction.Reject => $"Booking #{bookingId} rejected. The customer has been notified.",
        BookingAction.Start => $"Booking #{bookingId} is now in progress.",
        _ => $"Booking #{bookingId} marked as completed."
    };

    public async Task<BookingActionResult> ChangeStatusAsync(int userId, int bookingId, BookingAction action, CancellationToken ct = default)
    {
        // 1. who is asking: found from the sign-in cookie, and only an Active + Approved provider may change anything
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, BookingActionOutcome.NotFound, string.Empty);

        // the browser can post any number as the action; only the four real ones are accepted
        if (!Enum.IsDefined(action))
            return new(access, BookingActionOutcome.InvalidTransition, "That action is not available.");

        var info = BookingActionInfo.For(action);

        // 2. one provider's accepts run one after another, so two requests cannot both take the same hour
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(ct) : null;
        if (relational && action == BookingAction.Accept)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM [SERVICE_PROVIDERS] WITH (UPDLOCK, HOLDLOCK) WHERE [PROVIDER_ID] = {who.ProviderId}", ct);
        }

        // 3. the booking must belong to THIS provider; someone else's booking looks exactly like a missing one
        var booking = await _db.Bookings
            .Include(b => b.Customer)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.ProviderId == who.ProviderId, ct);
        if (booking is null)
        {
            _logger.LogWarning("Provider {ProviderId} tried to {Action} booking {BookingId}, which is not theirs or does not exist.",
                who.ProviderId, action, bookingId);
            return new(access, BookingActionOutcome.NotFound, "That booking could not be found.");
        }

        // 4. only the allowed status changes
        if (booking.Status != info.FromStatus)
        {
            _logger.LogInformation("Booking {BookingId} is {Status}; {Action} refused.", bookingId, booking.Status, action);
            return new(access, BookingActionOutcome.InvalidTransition,
                $"Booking #{bookingId} is {booking.Status.ToLowerInvariant()}, so it can't be {Done(action)}.");
        }

        // 4b. the job can only start once the customer has approved and paid the quotation (Paid -> Scheduled -> In Progress)
        if (action == BookingAction.Start && booking.PaymentStatus != PaymentStatuses.Paid)
        {
            _logger.LogInformation("Booking {BookingId} is not paid yet; start refused.", bookingId);
            return new(access, BookingActionOutcome.InvalidTransition,
                $"Booking #{bookingId} is waiting for the customer to approve and pay the quotation, so it can't start yet.");
        }

        // 5. accepting must not clash with a job the provider already accepted or started that day
        if (action == BookingAction.Accept)
        {
            var start = Minutes(booking.BookingTime);
            var end = start + booking.TotalDurationMinutes;

            var others = await _db.Bookings.AsNoTracking()
                .Where(b => b.ProviderId == who.ProviderId
                         && b.BookingId != booking.BookingId
                         && b.BookingDate == booking.BookingDate
                         && (b.Status == BookingStatuses.Accepted || b.Status == BookingStatuses.InProgress))
                .Select(b => new { b.BookingId, b.BookingTime, b.TotalDurationMinutes })
                .ToListAsync(ct);

            var clash = others.FirstOrDefault(o =>
            {
                var s = Minutes(o.BookingTime);
                return start < s + o.TotalDurationMinutes && s < end;      // the two time ranges touch
            });

            if (clash is not null)
            {
                var startText = clash.BookingTime.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
                var endText = clash.BookingTime.AddMinutes(clash.TotalDurationMinutes).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
                _logger.LogInformation("Accepting booking {BookingId} refused: it overlaps booking {OtherId}.", bookingId, clash.BookingId);
                return new(access, BookingActionOutcome.Overlap,
                    $"Booking #{bookingId} can't be accepted: it overlaps with booking #{clash.BookingId} ({startText} – {endText}), which is already on your schedule.");
            }
        }

        // 6. change the status and tell the customer, together or not at all
        var now = _clock.GetUtcNow().UtcDateTime;
        booking.Status = info.ToStatus;
        booking.UpdatedAt = now;
        _db.Notifications.Add(new Notification
        {
            UserId = booking.Customer.UserId,
            Message = $"Your booking #{booking.BookingId} is now {info.ToStatus}.",     // same wording as usp_UpdateBookingStatus
            CreatedAt = now
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // the row changed between reading and saving (for example the customer cancelled at the same moment)
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Booking {BookingId} changed while provider {ProviderId} was trying to {Action}.", bookingId, who.ProviderId, action);
            return new(access, BookingActionOutcome.Changed,
                $"Booking #{bookingId} was just changed by someone else. Review it and try again.");
        }

        _logger.LogInformation("Provider {ProviderId} moved booking {BookingId} to {Status}.", who.ProviderId, bookingId, info.ToStatus);

        // live: the customer's open dashboard / My Bookings page updates itself (and the provider's other tabs)
        if (_live is not null)
            await _live.BookingChangedAsync(new BookingChange(booking.BookingId, booking.Status, booking.PaymentStatus, action.ToString().ToLowerInvariant()),
                booking.Customer.UserId, userId);
        return new(access, BookingActionOutcome.Success, SuccessText(bookingId, action));
    }
}
