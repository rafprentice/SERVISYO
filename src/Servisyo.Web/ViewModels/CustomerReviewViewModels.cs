using System.ComponentModel.DataAnnotations;

namespace Servisyo.Web.ViewModels;

/// What the customer types on the "Leave a Review" page.
public class LeaveReviewForm
{
    [Range(1, 5, ErrorMessage = "Please choose from 1 to 5 stars.")]
    public int Rating { get; set; }

    [StringLength(255, ErrorMessage = "Please keep your comment under 255 characters.")]
    public string? Comment { get; set; }
}

public class LeaveReviewPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new();
    public int BookingId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public LeaveReviewForm Form { get; set; } = new();
}
