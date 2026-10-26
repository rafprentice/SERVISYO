using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public enum ReviewEligibility { Ok, NotAllowed, NotFound, NotCompleted, AlreadyReviewed }

public record ReviewPageResult(ReviewEligibility Outcome, LeaveReviewPageViewModel? Page, string? Message);

public enum SubmitReviewOutcome { Ok, NotAllowed, NotFound, NotCompleted, AlreadyReviewed, Invalid }

public record SubmitReviewResult(SubmitReviewOutcome Outcome, string Message);

public interface ICustomerReviewService
{
    /// The "Leave a Review" page for one of the signed-in customer's own Completed bookings.
    Task<ReviewPageResult> GetPageAsync(int userId, int bookingId, CancellationToken ct = default);

    /// Saves the review (Visible), updates the provider's rating and review count, and tells the provider.
    Task<SubmitReviewResult> SubmitAsync(int userId, int bookingId, LeaveReviewForm form, CancellationToken ct = default);
}

/// Business rules (project PDF, "Reviews"):
///  - only the customer of the booking, only when it is Completed, only once per booking;
///  - rating 1 to 5;
///  - the provider's AVG_RATING / REVIEW_COUNT count VISIBLE reviews only (same rule as fn_GetProviderAvgRating);
///  - providers cannot write or change reviews (only Customer accounts reach this service).
public class CustomerReviewService : ICustomerReviewService
{
    public const int MaxCommentLength = 255;

    private const string NotYours = "That booking could not be found.";
    private const string NotDone = "You can review a booking once the service is completed.";
    private const string Done = "You already reviewed this booking. Thank you!";

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomerReviewService> _logger;

    public CustomerReviewService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<CustomerReviewService>? logger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CustomerReviewService>.Instance;
    }

    private sealed class Who
    {
        public int CustomerId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    private sealed class BookingInfo
    {
        public int BookingId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int ProviderId { get; set; }
        public int ProviderUserId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool HasReview { get; set; }
    }

    // The customer always comes from the sign-in cookie (the user id), never from the browser.
    private Task<Who?> FindCustomerAsync(int userId, CancellationToken ct) =>
        _db.Customers.AsNoTracking()
            .Where(c => c.UserId == userId && c.User.Role == Roles.Customer && c.User.AccountStatus == AccountStatuses.Active)
            .Select(c => new Who { CustomerId = c.CustomerId, FirstName = c.User.FirstName, LastName = c.User.LastName })
            .FirstOrDefaultAsync(ct);

    // Someone else's booking is reported exactly like a missing one, so ids cannot be probed.
    private Task<BookingInfo?> FindBookingAsync(int customerId, int bookingId, CancellationToken ct) =>
        _db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId && b.CustomerId == customerId)
            .Select(b => new BookingInfo
            {
                BookingId = b.BookingId,
                Status = b.Status,
                ProviderId = b.ProviderId,
                ProviderUserId = b.Provider.UserId,
                ProviderName = b.Provider.BusinessName,
                Date = b.BookingDate,
                Time = b.BookingTime,
                CreatedAt = b.CreatedAt,
                HasReview = _db.Reviews.Any(r => r.BookingId == b.BookingId)
            })
            .FirstOrDefaultAsync(ct);

    private static ReviewEligibility Check(BookingInfo? b) =>
        b is null ? ReviewEligibility.NotFound
        : b.Status != BookingStatuses.Completed ? ReviewEligibility.NotCompleted
        : b.HasReview ? ReviewEligibility.AlreadyReviewed
        : ReviewEligibility.Ok;

    private static string MessageFor(ReviewEligibility e) => e switch
    {
        ReviewEligibility.NotCompleted => NotDone,
        ReviewEligibility.AlreadyReviewed => Done,
        _ => NotYours
    };

    public async Task<ReviewPageResult> GetPageAsync(int userId, int bookingId, CancellationToken ct = default)
    {
        var who = await FindCustomerAsync(userId, ct);
        if (who is null) return new(ReviewEligibility.NotAllowed, null, null);

        var booking = await FindBookingAsync(who.CustomerId, bookingId, ct);
        var eligibility = Check(booking);
        if (eligibility != ReviewEligibility.Ok) return new(eligibility, null, MessageFor(eligibility));

        var titles = await _db.GetBookingTitlesAsync(new[] { bookingId }, ct);
        var fullName = $"{who.FirstName} {who.LastName}".Trim();

        var page = new LeaveReviewPageViewModel
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
            BookingId = booking!.BookingId,
            Reference = $"BK-{booking.CreatedAt.Year}-{booking.BookingId:D4}",
            Title = titles.TryGetValue(bookingId, out var t) ? t : "Service booking",
            ProviderName = booking.ProviderName,
            Date = booking.Date,
            Time = booking.Time
        };
        return new(ReviewEligibility.Ok, page, null);
    }

    public async Task<SubmitReviewResult> SubmitAsync(int userId, int bookingId, LeaveReviewForm form, CancellationToken ct = default)
    {
        var who = await FindCustomerAsync(userId, ct);
        if (who is null) return new(SubmitReviewOutcome.NotAllowed, string.Empty);

        // Checked here too: the browser's own validation can be skipped.
        if (form.Rating < 1 || form.Rating > 5)
            return new(SubmitReviewOutcome.Invalid, "Please choose from 1 to 5 stars.");
        var comment = string.IsNullOrWhiteSpace(form.Comment) ? null : form.Comment.Trim();
        if (comment is not null && comment.Length > MaxCommentLength)
            return new(SubmitReviewOutcome.Invalid, $"Please keep your comment under {MaxCommentLength} characters.");

        var booking = await FindBookingAsync(who.CustomerId, bookingId, ct);
        var eligibility = Check(booking);
        if (eligibility != ReviewEligibility.Ok)
        {
            var outcome = eligibility switch
            {
                ReviewEligibility.NotCompleted => SubmitReviewOutcome.NotCompleted,
                ReviewEligibility.AlreadyReviewed => SubmitReviewOutcome.AlreadyReviewed,
                _ => SubmitReviewOutcome.NotFound
            };
            return new(outcome, MessageFor(eligibility));
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var review = new Review
        {
            BookingId = bookingId,
            Rating = (byte)form.Rating,
            Comment = comment,
            Status = ReviewStatuses.Visible,
            CreatedAt = now
        };
        _db.Reviews.Add(review);

        // Provider rating = average of VISIBLE reviews only. The other visible reviews come from the database; this one is added.
        var provider = await _db.Providers.FirstAsync(p => p.ProviderId == booking!.ProviderId, ct);
        var visible = await _db.Reviews.AsNoTracking()
            .Where(r => r.Booking.ProviderId == provider.ProviderId && r.Status == ReviewStatuses.Visible)
            .Select(r => (int)r.Rating)
            .ToListAsync(ct);
        visible.Add(form.Rating);
        provider.ReviewCount = visible.Count;
        provider.AvgRating = Math.Round((decimal)visible.Sum() / visible.Count, 1, MidpointRounding.AwayFromZero);

        var customerName = NameFormat.ShortName(who.FirstName, who.LastName);
        var text = $"{customerName} left a {form.Rating}-star review for booking #{bookingId}.";
        _db.Notifications.Add(new Notification { UserId = booking!.ProviderUserId, Message = NameFormat.Truncate(text, 255), CreatedAt = now });

        try
        {
            await _db.SaveChangesAsync(ct);   // review + provider rating + notification: one database transaction
        }
        catch (DbUpdateException ex)
        {
            // Two submits at the same moment: the unique index on REVIEWS.BookingId rejects the second one.
            _logger.LogWarning(ex, "Review for booking {BookingId} was refused by the database (probably already reviewed).", bookingId);
            return new(SubmitReviewOutcome.AlreadyReviewed, Done);
        }

        _logger.LogInformation("Customer {CustomerId} reviewed booking {BookingId} ({Rating} stars); provider {ProviderId} now has {Count} visible reviews.",
            who.CustomerId, bookingId, form.Rating, provider.ProviderId, provider.ReviewCount);
        return new(SubmitReviewOutcome.Ok, $"Thank you! Your {form.Rating}-star review for {booking.ProviderName} was posted.");
    }
}
