using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public enum ReviewOutcome { Success, NotFound, NoChange, Invalid, NotAllowed }

public sealed record ReviewResult(ReviewOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == ReviewOutcome.Success;
}

public interface IReviewModerationService
{
    /// The reviews list for the admin. Returns null when the signed-in user is not an Active administrator.
    Task<ReviewsPageViewModel?> GetReviewsAsync(int adminUserId, ReviewListQuery query, CancellationToken ct = default);

    Task<ReviewResult> ModerateAsync(int adminUserId, int reviewId, ReviewAction action, string? reason, CancellationToken ct = default);
}

public class ReviewModerationService : IReviewModerationService
{
    public const int PageSize = 20;
    public const int MaxReasonLength = 255;
    private const int ManilaOffsetHours = 8;

    private readonly ServisyoDbContext _db;
    private readonly ILogger<ReviewModerationService> _logger;

    public ReviewModerationService(ServisyoDbContext db, ILogger<ReviewModerationService>? logger = null)
    {
        _db = db;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ReviewModerationService>.Instance;
    }

    // ------------------------------------------------------------------ read

    public async Task<ReviewsPageViewModel?> GetReviewsAsync(int adminUserId, ReviewListQuery query, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return null;

        var all = _db.Reviews.AsNoTracking();

        // tab numbers always count every review
        var counts = await all.GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int CountOf(string s) => counts.Where(c => c.Status == s).Select(c => c.Count).FirstOrDefault();
        var total = counts.Sum(c => c.Count);

        var tabs = new List<ReviewTabViewModel>
        {
            new(ReviewFilters.All, "All", total),
            new(ReviewFilters.Flagged, "Flagged", CountOf(ReviewStatuses.Flagged)),
            new(ReviewFilters.Hidden, "Hidden", CountOf(ReviewStatuses.Removed))
        };

        var tab = ReviewFilters.Normalize(query.Status);
        var filtered = tab switch
        {
            ReviewFilters.Flagged => all.Where(r => r.Status == ReviewStatuses.Flagged),
            ReviewFilters.Hidden => all.Where(r => r.Status == ReviewStatuses.Removed),
            _ => all
        };

        var matching = await filtered.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize));
        var page = Math.Clamp(query.Page, 1, pageCount);

        var rows = await filtered
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ReviewId)      // newest first
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(r => new
            {
                r.ReviewId, r.BookingId, r.Rating, r.Comment, r.Status, r.CreatedAt,
                CustomerFirst = r.Booking.Customer.User.FirstName, CustomerLast = r.Booking.Customer.User.LastName,
                ProviderFirst = r.Booking.Provider.User.FirstName, ProviderLast = r.Booking.Provider.User.LastName
            })
            .ToListAsync(ct);
        var titles = await _db.GetBookingTitlesAsync(rows.Select(r => r.BookingId).ToList(), ct);

        return new ReviewsPageViewModel
        {
            Shell = shell,
            Status = tab,
            Tabs = tabs,
            CountAll = total,
            TotalMatching = matching,
            Page = page,
            PageCount = pageCount,
            Cards = rows.Select(r => new ReviewCardViewModel
            {
                ReviewId = r.ReviewId,
                Rating = r.Rating,
                Comment = r.Comment,
                Status = r.Status,
                CustomerName = $"{r.CustomerFirst} {r.CustomerLast}".Trim(),
                ProviderName = $"{r.ProviderFirst} {r.ProviderLast}".Trim(),
                Service = titles.TryGetValue(r.BookingId, out var t) ? t : "Service booking",
                SubmittedOn = DateOnly.FromDateTime(r.CreatedAt.AddHours(ManilaOffsetHours))     // stored in UTC, shown in Cebu time
            }).ToList()
        };
    }

    // ----------------------------------------------------------------- write

    public async Task<ReviewResult> ModerateAsync(int adminUserId, int reviewId, ReviewAction action, string? reason, CancellationToken ct = default)
    {
        var isAdmin = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == adminUserId
                                                               && u.Role == Roles.Administrator
                                                               && u.AccountStatus == AccountStatuses.Active, ct);
        if (!isAdmin)
            return new(ReviewOutcome.NotAllowed, "Your administrator account is not allowed to do this.");

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is { Length: > MaxReasonLength })
            return new(ReviewOutcome.Invalid, $"The reason can be at most {MaxReasonLength} characters.");

        var review = await _db.Reviews.Include(r => r.Booking).FirstOrDefaultAsync(r => r.ReviewId == reviewId, ct);
        if (review is null)
            return new(ReviewOutcome.NotFound, "That review no longer exists.");

        // Allowed moves (same rules as the buttons on the page): Keep: Flagged -> Visible. Hide: Visible/Flagged -> Removed. Restore: Removed -> Visible.
        string newStatus;
        string[] allowedFrom;
        string verb;
        switch (action)
        {
            case ReviewAction.Keep:
                newStatus = ReviewStatuses.Visible; allowedFrom = new[] { ReviewStatuses.Flagged }; verb = "kept";
                break;
            case ReviewAction.Hide:
                newStatus = ReviewStatuses.Removed; allowedFrom = new[] { ReviewStatuses.Visible, ReviewStatuses.Flagged }; verb = "hidden";
                break;
            case ReviewAction.Restore:
                newStatus = ReviewStatuses.Visible; allowedFrom = new[] { ReviewStatuses.Removed }; verb = "restored";
                break;
            default:
                return new(ReviewOutcome.Invalid, "That action is not recognised.");
        }

        if (!allowedFrom.Contains(review.Status))
        {
            var now = review.Status == ReviewStatuses.Removed ? "hidden" : review.Status.ToLowerInvariant();
            return new(ReviewOutcome.NoChange, $"Review #{review.ReviewId} is currently {now}, so nothing was changed. The list has been refreshed.");
        }

        var provider = await _db.Providers.FirstAsync(p => p.ProviderId == review.Booking.ProviderId, ct);

        // Provider rating = average of the VISIBLE reviews only (same rule as fn_GetProviderAvgRating in the PDF).
        // The other visible reviews are read from the database; this review is added if it becomes visible.
        var visibleRatings = await _db.Reviews.AsNoTracking()
            .Where(r => r.Booking.ProviderId == provider.ProviderId
                     && r.ReviewId != review.ReviewId
                     && r.Status == ReviewStatuses.Visible)
            .Select(r => (int)r.Rating)
            .ToListAsync(ct);
        if (newStatus == ReviewStatuses.Visible) visibleRatings.Add(review.Rating);

        review.Status = newStatus;
        provider.ReviewCount = visibleRatings.Count;
        provider.AvgRating = visibleRatings.Count == 0
            ? null
            : Math.Round((decimal)visibleRatings.Sum() / visibleRatings.Count, 1, MidpointRounding.AwayFromZero);

        // Same log format as usp_ModerateReview in the PDF: 'Review <status>', 'REVIEW'.
        _db.AdminActions.Add(new AdminAction
        {
            AdminUserId = adminUserId,
            ActionType = "Review " + newStatus,
            TargetType = "REVIEW",
            TargetId = review.ReviewId,
            Reason = cleanReason
        });

        await _db.SaveChangesAsync(ct);       // review + provider rating + log entry: one database transaction

        _logger.LogInformation("Admin {AdminUserId} {Verb} review {ReviewId}; provider {ProviderId} now has {Count} visible reviews.",
            adminUserId, verb, review.ReviewId, provider.ProviderId, provider.ReviewCount);

        return new(ReviewOutcome.Success, $"Review #{review.ReviewId} was {verb}.");
    }
}
