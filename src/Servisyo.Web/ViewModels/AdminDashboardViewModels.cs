using Servisyo.Web.Helpers;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// How one booking status is shown to the administrator (badge text + colour).
public sealed record AdminBookingDisplay(string Badge, string Tone)
{
    public static AdminBookingDisplay For(string status) => status switch
    {
        BookingStatuses.Pending => new("Pending", "pending"),
        BookingStatuses.Accepted => new("Accepted", "accepted"),
        BookingStatuses.InProgress => new("In Progress", "progress"),
        BookingStatuses.Completed => new("Completed", "completed"),
        BookingStatuses.Rejected => new("Rejected", "danger"),
        BookingStatuses.Cancelled => new("Cancelled", "muted"),
        _ => new(status, "muted")
    };
}

/// What a button in the "Needs Admin Attention" list does. None = the screen is not built yet (button stays greyed out).
public enum AdminAttentionAction { None, ReviewApplication, ApplicationList, ModerateReviews }

/// One row of "Needs Admin Attention": the shared row (text, colour, button label) plus where its button goes.
public class AdminAttentionItem : AttentionItemViewModel
{
    public AdminAttentionAction Action { get; set; } = AdminAttentionAction.None;
    public int? TargetId { get; set; }          // ReviewApplication: the provider whose application opens
}

public static class AdminAttention
{
    /// Builds the "Needs Admin Attention" rows. A row only exists when its count is above zero,
    /// so the list never shows something that is not really waiting.
    /// firstPendingProviderId = the oldest waiting application: "Review Applications" opens that one first (fair queue).
    public static List<AdminAttentionItem> Build(
        int pendingApplications, int flaggedReviews, int suspendedAccounts, int openReports,
        int? firstPendingProviderId = null)
    {
        var rows = new List<AdminAttentionItem>();

        if (pendingApplications > 0)
            rows.Add(new()
            {
                Tone = "alert",
                Text = $"{Plural.Of(pendingApplications, "provider application")} waiting for review",
                ActionLabel = "Review Applications",
                Action = firstPendingProviderId is null ? AdminAttentionAction.ApplicationList : AdminAttentionAction.ReviewApplication,
                TargetId = firstPendingProviderId
            });

        if (flaggedReviews > 0)
            rows.Add(new()
            {
                Tone = "warning",
                Text = flaggedReviews == 1
                    ? "1 flagged review needs moderation"
                    : $"{flaggedReviews} flagged reviews need moderation",
                ActionLabel = "Moderate Reviews",
                Action = AdminAttentionAction.ModerateReviews
            });

        if (suspendedAccounts > 0)
            rows.Add(new()
            {
                Tone = "danger",
                Text = $"{Plural.Of(suspendedAccounts, "suspended account")} requiring review",
                ActionLabel = "View Accounts"
            });

        if (openReports > 0)
            rows.Add(new()
            {
                Tone = "violet",
                Text = $"{Plural.Of(openReports, "reported booking issue")} pending resolution",
                ActionLabel = openReports == 1 ? "View Booking" : "View Bookings"
            });

        return rows;
    }
}

public class AdminBookingRowViewModel
{
    public int BookingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public AdminBookingDisplay Display { get; set; } = AdminBookingDisplay.For(string.Empty);
}

public class PendingApplicationViewModel
{
    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;     // e.g. "Plumbing · Mandaue"
}

public class AdminDashboardViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };

    public int TotalUsers { get; set; }
    public int Customers { get; set; }
    public int Providers { get; set; }
    public int PendingApprovals { get; set; }
    public int TotalBookings { get; set; }
    public int FlaggedReviews { get; set; }

    public IReadOnlyList<AdminAttentionItem> Attention { get; set; } = Array.Empty<AdminAttentionItem>();
    public IReadOnlyList<AdminBookingRowViewModel> RecentBookings { get; set; } = Array.Empty<AdminBookingRowViewModel>();
    public IReadOnlyList<PendingApplicationViewModel> PendingApplications { get; set; } = Array.Empty<PendingApplicationViewModel>();
}
