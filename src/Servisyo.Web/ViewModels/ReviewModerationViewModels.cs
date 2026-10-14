using System.Globalization;
using System.ComponentModel.DataAnnotations;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

public class ReviewListQuery
{
    public string? Status { get; set; }      // All | Flagged | Hidden
    public int Page { get; set; } = 1;
}

public static class ReviewFilters
{
    public const string All = "All";
    public const string Flagged = "Flagged";
    public const string Hidden = "Hidden";    // = reviews with the database status "Removed"

    public static string Normalize(string? status)
    {
        var key = new string((status ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return key switch
        {
            "flagged" => Flagged,
            "hidden" or "removed" => Hidden,
            _ => All
        };
    }
}

/// What the administrator can do to a review.
public enum ReviewAction { Keep, Hide, Restore }

/// Text, colour and confirmation wording for one moderation button.
public sealed record ReviewActionInfo(string Action, string Label, string Tone, string Title, string Text)
{
    public static ReviewActionInfo For(ReviewAction action) => action switch
    {
        ReviewAction.Keep => new(nameof(ReviewAction.Keep), "Keep Review", "ok",
            "Keep this review?",
            "The review stays visible to everyone and counts towards the provider's rating."),
        ReviewAction.Hide => new(nameof(ReviewAction.Hide), "Hide Review", "danger",
            "Hide this review?",
            "It will no longer be shown publicly and will not count towards the provider's rating. You can restore it later."),
        _ => new(nameof(ReviewAction.Restore), "Restore Review", "ok",
            "Restore this review?",
            "It will be shown publicly again and count towards the provider's rating.")
    };

    /// Which buttons a review in this status gets (the same rules the server enforces).
    public static IReadOnlyList<ReviewActionInfo> ForStatus(string status) => status switch
    {
        ReviewStatuses.Flagged => new[] { For(ReviewAction.Keep), For(ReviewAction.Hide) },
        ReviewStatuses.Removed => new[] { For(ReviewAction.Restore) },
        _ => new[] { For(ReviewAction.Hide) }
    };
}

/// The values posted by the confirmation dialog.
public class ReviewModerationForm
{
    public int ReviewId { get; set; }
    public string? Action { get; set; }

    [StringLength(255, ErrorMessage = "The reason can be at most 255 characters.")]
    public string? Reason { get; set; }

    // remembered so the admin comes back to the same tab and page
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record ReviewTabViewModel(string Key, string Label, int Count);

public class ReviewCardViewModel
{
    public int ReviewId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Status { get; set; } = ReviewStatuses.Visible;      // database status
    public DateOnly SubmittedOn { get; set; }
    public IReadOnlyList<ReviewActionInfo> Actions => ReviewActionInfo.ForStatus(Status);

    public string RatingText => Rating.ToString("0.0", CultureInfo.InvariantCulture);
    public string StatusText => Status == ReviewStatuses.Removed ? "Hidden" : Status;
    public string StatusTone => Status switch
    {
        ReviewStatuses.Flagged => "pending",      // yellow
        ReviewStatuses.Removed => "muted",        // grey
        _ => "completed"                          // green
    };
}

public class ReviewsPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };
    public string Status { get; set; } = ReviewFilters.All;          // normalised tab
    public IReadOnlyList<ReviewTabViewModel> Tabs { get; set; } = Array.Empty<ReviewTabViewModel>();
    public int CountAll { get; set; }
    public int TotalMatching { get; set; }
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public IReadOnlyList<ReviewCardViewModel> Cards { get; set; } = Array.Empty<ReviewCardViewModel>();

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }
}
