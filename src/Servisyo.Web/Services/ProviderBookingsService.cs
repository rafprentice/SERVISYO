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

    /// Accept / reject / start one booking of the signed-in provider (same status rules as usp_UpdateBookingStatus in
    /// the PDF). The customer is told about the new status. Complete is refused here (ProofMissing): use CompleteAsync.
    /// Start is StartAsync without a late reason (so a late start is refused: LateReasonRequired).
    Task<BookingActionResult> ChangeStatusAsync(int userId, int bookingId, BookingAction action, CancellationToken ct = default);

    /// Database v2.12: Accepted -> In Progress inside the start window (Philippine time): not before the scheduled date
    /// and time (TooEarly), not more than 2 hours after it (StartWindowExpired), and more than 15 minutes late only
    /// with a reason (LateReasonRequired, at most 500 characters). Saves STARTED_AT (now, UTC) and the reason.
    Task<BookingActionResult> StartAsync(int userId, int bookingId, string? lateReason, CancellationToken ct = default);

    /// Database v2.11: In Progress -> Completed WITH proof: a completion message (required, at most 1000 characters)
    /// and a photo (JPG / PNG / WebP, at most 5 MB). Saves the photo, then BOOKINGS (message, photo, COMPLETED_AT,
    /// status) in one transaction; if the booking cannot be saved, the photo is deleted again.
    Task<BookingActionResult> CompleteAsync(int userId, int bookingId, string? completionMessage, IFormFile? proofPhoto, CancellationToken ct = default);
}

public class ProviderBookingsService : IProviderBookingsService
{
    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProviderBookingsService> _logger;

    private readonly IBookingLiveUpdates? _live;
    private readonly ICompletionPhotoStore? _photos;

    public ProviderBookingsService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<ProviderBookingsService>? logger = null,
        IBookingLiveUpdates? live = null, ICompletionPhotoStore? photos = null)
    {
        _db = db;
        _live = live;
        _photos = photos;
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
        public decimal TotalFee { get; set; }
        public string? PayMethod { get; set; }
        public string? PayStatus { get; set; }
        public string? PayReference { get; set; }
        public string? CompletionMessage { get; set; }
        public string? CompletionPhotoUrl { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public string? LateReason { get; set; }
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
                ServiceDescription = b.ServiceDescription,
                TotalFee = b.TotalFee,
                PayMethod = b.PaymentDetail != null ? (b.PaymentDetail.PaymentId == PaymentIds.Cash ? PaymentMethods.CashOnCompletion : PaymentMethods.MockEWallet) : null,
                PayStatus = b.PaymentDetail != null ? b.PaymentDetail.PaymentStatus : null,
                PayReference = b.PaymentDetail != null ? b.PaymentDetail.TransactionReference : null,
                CompletionMessage = b.CompletionMessage,
                CompletionPhotoUrl = b.CompletionPhotoUrl,
                CompletedAt = b.CompletedAt,
                StartedAt = b.StartedAt,
                LateReason = b.LateReason
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
        var manilaNow = PhilippineTime.Now(_clock);       // database v2.12: the start windows are in Philippine time
        var vm = new ProviderBookingsPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Provider",
                FirstName = fullName.Split(' ')[0],
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                Subtitle = who.BusinessName,
                UnreadNotifications = await _db.CountUnreadNotificationsAsync(userId, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            ActiveTab = activeTab.Key,
            Tabs = tabs,
            Page = currentPage,
            TotalMatching = total,
            Cards = rows.Select(r =>
            {
                var pay = new PaymentFacts(r.Status, r.PaymentStatus, r.PayMethod, r.PayStatus, r.PayReference, r.TotalFee);
                var customerName = r.CustomerName.Trim();

                // database v2.12: a scheduled booking only offers its start button inside the start window
                var actions = BookingActionInfo.ForBooking(r.Status, pay.Effective);
                StartWindowViewModel? window = null;
                if (r.Status == BookingStatuses.Accepted && PaymentProgress.IsArranged(pay.Effective))
                {
                    window = StartWindowViewModel.For(r.BookingDate, r.BookingTime, manilaNow);
                    actions = BookingActionInfo.ForStart(window.State);
                }

                return new ProviderBookingCardViewModel
                {
                    BookingId = r.BookingId,
                    Title = titles.TryGetValue(r.BookingId, out var t) ? t : "Service booking",
                    CustomerName = customerName,
                    CategoryText = categories.TryGetValue(r.BookingId, out var c) ? c : null,
                    Status = r.Status,
                    Tone = ProviderBookingBadge.Tone(r.Status),
                    WhenText = BookingFormat.DateTimeText(r.BookingDate, r.BookingTime),
                    Location = string.IsNullOrWhiteSpace(r.ServiceLocation) ? null : r.ServiceLocation.Trim(),
                    SubmittedText = BookingFormat.SubmittedText(r.CreatedAt),
                    Notes = string.IsNullOrWhiteSpace(r.ServiceDescription) ? null : r.ServiceDescription.Trim(),
                    PaymentStatus = pay.Effective,
                    Fee = r.TotalFee,
                    Actions = actions,
                    Payment = PaymentNotes.ForProvider(pay, customerName),
                    CanConfirmCash = r.Status == BookingStatuses.Completed && pay.CashToCollect,
                    Proof = CompletionProofViewModel.From(r.Status, r.CompletionMessage, r.CompletionPhotoUrl, r.CompletedAt),
                    StartWindow = window,
                    StartInfo = StartInfoViewModel.From(r.Status, r.BookingDate, r.BookingTime, r.StartedAt, r.LateReason),
                    IsNoShow = StartRules.IsNoShow(r.Status, r.PayStatus is not null)
                };
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

    private static string SuccessText(int bookingId, BookingAction action, PaymentDetail? payment, decimal totalFee) => action switch
    {
        BookingAction.Accept => $"Booking #{bookingId} accepted. The customer has been notified.",
        BookingAction.Reject => $"Booking #{bookingId} rejected. The customer has been notified.",
        BookingAction.Start => $"Booking #{bookingId} is now in progress.",
        _ when payment is { PaymentStatus: PaymentRecordStatuses.PendingCollection } =>
            $"Booking #{bookingId} marked as completed. When the customer hands you the {PaymentNotes.Money(totalFee)} cash, press Confirm Cash Received.",
        _ => $"Booking #{bookingId} marked as completed."
    };

    public async Task<BookingActionResult> ChangeStatusAsync(int userId, int bookingId, BookingAction action, CancellationToken ct = default)
    {
        // database v2.12: starting has its own rules (the start window, a reason when late)
        if (action == BookingAction.Start) return await StartAsync(userId, bookingId, lateReason: null, ct);

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
            .Include(b => b.PaymentDetail)
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

        // 4a. database v2.11: a job is only completed WITH its proof (the "Confirm Service Completion" window, CompleteAsync)
        if (action == BookingAction.Complete)
            return new(access, BookingActionOutcome.ProofMissing, CompletionRules.ProofRequired);

        var payment = PaymentProgress.Effective(booking.PaymentStatus, booking.PaymentDetail?.PaymentStatus);

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

        // 6. change the status (database v2.3: the customer's bell shows "Your booking #N is now ..." from BOOKINGS itself)
        var now = _clock.GetUtcNow().UtcDateTime;
        booking.Status = info.ToStatus;
        booking.UpdatedAt = now;

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
            await _live.BookingChangedAsync(new BookingChange(booking.BookingId, booking.Status, payment, action.ToString().ToLowerInvariant()),
                booking.Customer.UserId, userId);
        return new(access, BookingActionOutcome.Success, SuccessText(bookingId, action, booking.PaymentDetail, booking.TotalFee));
    }

    // ------------------------------------------------- database v2.12: start inside the start window

    public async Task<BookingActionResult> StartAsync(int userId, int bookingId, string? lateReason, CancellationToken ct = default)
    {
        // 1. who is asking: found from the sign-in cookie, and only an Active + Approved provider may change anything
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, BookingActionOutcome.NotFound, string.Empty);

        // 2. the booking must belong to THIS provider; someone else's booking looks exactly like a missing one
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(ct) : null;
        var booking = await _db.Bookings
            .Include(b => b.Customer)
            .Include(b => b.PaymentDetail)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.ProviderId == who.ProviderId, ct);
        if (booking is null)
        {
            _logger.LogWarning("Provider {ProviderId} tried to start booking {BookingId}, which is not theirs or does not exist.", who.ProviderId, bookingId);
            return new(access, BookingActionOutcome.NotFound, "That booking could not be found.");
        }

        // 3. only Accepted -> In Progress, and only once the customer has approved the quotation and paid it, or chosen
        //    cash on completion (Paid -> Scheduled -> In Progress)
        if (booking.Status != BookingStatuses.Accepted)
        {
            _logger.LogInformation("Booking {BookingId} is {Status}; start refused.", bookingId, booking.Status);
            return new(access, BookingActionOutcome.InvalidTransition,
                $"Booking #{bookingId} is {booking.Status.ToLowerInvariant()}, so it can't be {Done(BookingAction.Start)}.");
        }
        var payment = PaymentProgress.Effective(booking.PaymentStatus, booking.PaymentDetail?.PaymentStatus);
        if (!PaymentProgress.IsArranged(payment))
        {
            _logger.LogInformation("Booking {BookingId} is not paid yet; start refused.", bookingId);
            return new(access, BookingActionOutcome.InvalidTransition,
                $"Booking #{bookingId} is waiting for the customer to approve and pay the quotation, so it can't start yet.");
        }

        // 4. the start window, in Philippine time. ONE reading of the clock: the same moment is checked and saved.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var minutes = StartRules.MinutesAfter(booking.BookingDate, booking.BookingTime, PhilippineTime.FromUtc(nowUtc));
        var state = StartRules.StateAt(minutes);
        if (state == StartWindowState.TooEarly)
        {
            _logger.LogWarning("Provider {ProviderId} tried to start booking {BookingId} {Minutes} minutes before its scheduled time.",
                who.ProviderId, bookingId, -minutes);
            return new(access, BookingActionOutcome.TooEarly, StartRules.TooEarlyMessage);
        }
        if (state == StartWindowState.Expired)
        {
            _logger.LogWarning("Provider {ProviderId} tried to start booking {BookingId} {Minutes} minutes after its scheduled time (window expired).",
                who.ProviderId, bookingId, minutes);
            return new(access, BookingActionOutcome.StartWindowExpired, StartRules.ExpiredMessage);
        }

        // 5. more than 15 minutes late: the reason is required (the customer and the administrators see it)
        var reason = StartRules.CleanReason(lateReason);
        if (state == StartWindowState.Late)
        {
            if (reason is null)
                return new(access, BookingActionOutcome.LateReasonRequired, StartRules.LateReasonRequired);
            if (reason.Length > StartRules.MaxLateReasonLength)
                return new(access, BookingActionOutcome.LateReasonRequired, StartRules.LateReasonTooLong);
        }
        else
        {
            reason = null;          // on time: nothing to explain (CK_BOOKINGS_START allows no reason then)
        }

        // 6. the start. The audit trigger writes the System Logs line, with "(late start: N minutes late, reason
        //    '...')" or "(started on time)"; the customer's bell says "Your booking #N is now In Progress."
        booking.Status = BookingStatuses.InProgress;
        booking.StartedAt = nowUtc;
        booking.LateReason = reason;
        booking.UpdatedAt = nowUtc;

        try
        {
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Booking {BookingId} changed while provider {ProviderId} was starting it.", bookingId, who.ProviderId);
            return new(access, BookingActionOutcome.Changed, $"Booking #{bookingId} was just changed by someone else. Review it and try again.");
        }

        if (reason is null)
            _logger.LogInformation("Provider {ProviderId} started booking {BookingId} on time ({Minutes} minutes after the scheduled time).",
                who.ProviderId, bookingId, minutes);
        else
            _logger.LogWarning("Provider {ProviderId} started booking {BookingId} {Minutes} minutes late. Reason given.", who.ProviderId, bookingId, minutes);

        // live: the customer's open dashboard / My Bookings page updates itself (and the provider's other tabs)
        if (_live is not null)
            await _live.BookingChangedAsync(new BookingChange(booking.BookingId, booking.Status, payment, "start"),
                booking.Customer.UserId, userId);

        return new(access, BookingActionOutcome.Success, reason is null
            ? $"Booking #{bookingId} is now in progress."
            : $"Booking #{bookingId} is now in progress. It started {Plural.Of(minutes, "minute")} late; your reason was saved and the customer can see it.");
    }

    // ------------------------------------------------- database v2.11: complete with proof

    public async Task<BookingActionResult> CompleteAsync(int userId, int bookingId, string? completionMessage, IFormFile? proofPhoto,
        CancellationToken ct = default)
    {
        // 1. who is asking: found from the sign-in cookie, and only an Active + Approved provider may change anything
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, BookingActionOutcome.NotFound, string.Empty);

        // 2. the proof itself: checked here whatever the browser did (both problems are reported at once)
        var message = completionMessage?.Replace("\r\n", "\n").Trim();      // one kind of line break: 1,000 in the box = 1,000 saved
        var messageError = string.IsNullOrEmpty(message) ? CompletionRules.MessageRequired
            : message.Length > CompletionRules.MaxMessageLength ? CompletionRules.MessageTooLong
            : null;
        var photo = await ReadPhotoAsync(proofPhoto, ct);
        var errors = new CompletionErrors(messageError, photo.Error);
        if (errors.Any)
            return new(access, BookingActionOutcome.ProofMissing, messageError ?? photo.Error!, errors);
        if (_photos is null) throw new InvalidOperationException("No ICompletionPhotoStore is registered.");

        // 3. the booking must belong to THIS provider and be In Progress (the same rules as every status change)
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(ct) : null;
        var booking = await _db.Bookings
            .Include(b => b.Customer)
            .Include(b => b.PaymentDetail)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.ProviderId == who.ProviderId, ct);
        if (booking is null)
        {
            _logger.LogWarning("Provider {ProviderId} tried to complete booking {BookingId}, which is not theirs or does not exist.", who.ProviderId, bookingId);
            return new(access, BookingActionOutcome.NotFound, "That booking could not be found.");
        }
        if (booking.Status != BookingStatuses.InProgress)
        {
            _logger.LogInformation("Booking {BookingId} is {Status}; complete refused.", bookingId, booking.Status);
            return new(access, BookingActionOutcome.InvalidTransition,
                $"Booking #{bookingId} is {booking.Status.ToLowerInvariant()}, so it can't be marked completed.");
        }

        // 4. the photo first; if the booking cannot be saved, the photo is deleted again (nothing half-done is kept)
        var url = await _photos.SaveAsync(bookingId, photo.Bytes!, photo.Extension!, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        booking.Status = BookingStatuses.Completed;
        booking.CompletionMessage = message;
        booking.CompletionPhotoUrl = url;
        booking.CompletedAt = now;
        booking.UpdatedAt = now;

        try
        {
            // the audit trigger writes the System Logs line: "... status 'In Progress' -> 'Completed' (proof photo and
            // completion message saved)." (database v2.11)
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _photos.Delete(url);
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Booking {BookingId} changed while provider {ProviderId} was completing it.", bookingId, who.ProviderId);
            return new(access, BookingActionOutcome.Changed, $"Booking #{bookingId} was just changed by someone else. Review it and try again.");
        }
        catch
        {
            _photos.Delete(url);
            throw;
        }

        _logger.LogInformation("Provider {ProviderId} completed booking {BookingId} with proof ({Bytes} bytes).", who.ProviderId, bookingId, photo.Bytes!.Length);

        // live: the customer's open dashboard / My Bookings page updates itself (and the provider's other tabs)
        var payment = PaymentProgress.Effective(booking.PaymentStatus, booking.PaymentDetail?.PaymentStatus);
        if (_live is not null)
            await _live.BookingChangedAsync(new BookingChange(booking.BookingId, booking.Status, payment, "complete"),
                booking.Customer.UserId, userId);
        return new(access, BookingActionOutcome.Success, SuccessText(bookingId, BookingAction.Complete, booking.PaymentDetail, booking.TotalFee));
    }

    private sealed record PhotoUpload(byte[]? Bytes, string? Extension, string? Error);

    /// Required; at most 5 MB; the name must end in .jpg / .jpeg / .png / .webp AND the file's first bytes must really
    /// be a JPG, PNG or WebP (the saved extension comes from the bytes, not from the name).
    private static async Task<PhotoUpload> ReadPhotoAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return new(null, null, CompletionRules.PhotoRequired);
        if (file.Length > CompletionRules.MaxPhotoBytes) return new(null, null, CompletionRules.PhotoTooLarge);
        var nameExtension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        if (!CompletionRules.AllowedExtensions.Contains(nameExtension)) return new(null, null, CompletionRules.PhotoNotAnImage);

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var extension = ProfilePhotoService.DetectImage(bytes);
        return extension is null ? new(null, null, CompletionRules.PhotoNotAnImage) : new(bytes, extension, null);
    }
}
