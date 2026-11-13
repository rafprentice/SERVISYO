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

/// Where a button in the "Needs Admin Attention" list goes. None = no screen for it (the button stays greyed out).
public enum AdminAttentionAction
{
    None,
    ReviewApplication,       // the oldest provider application, in the review pop-up
    ApplicationList,         // Admin > Service Providers, Pending tab
    ModerateReviews,         // Admin > Reviews, Flagged tab
    BookingList,             // Admin > Bookings
    ReviewDayOffs,           // Admin > Service Providers, opening the provider of the oldest waiting day-off request (v2.7)
    NameChangeList,          // Admin > Name Changes, Pending tab (v2.8)
    SuspendedCustomers,      // Admin > Users, Suspended tab
    SuspendedProviders,      // Admin > Service Providers, Suspended tab
    UserList                 // Admin > Users (database v2.10: inactive accounts, report-only mode)
}

/// One row of "Needs Admin Attention": the shared row (text, colour, button label) plus where its button goes.
public class AdminAttentionItem : AttentionItemViewModel
{
    /// What kind of request the row is about (also the data-sv-attn attribute the browser checks can use).
    public string Key { get; set; } = string.Empty;
    public AdminAttentionAction Action { get; set; } = AdminAttentionAction.None;
    public int? TargetId { get; set; }          // ReviewApplication / ReviewDayOffs: the provider that opens first

    /// A second, smaller link (suspended accounts: customers AND providers are suspended, on two different pages).
    public string? SecondaryLabel { get; set; }
    public AdminAttentionAction SecondaryAction { get; set; } = AdminAttentionAction.None;
}

/// Everything that waits for an administrator, counted by AdminDashboardService.
public sealed record AdminAttentionCounts
{
    /// Database v2.7: day-off requests still Pending (with a day from today on).
    public int DayOffRequests { get; init; }
    /// The provider of the oldest waiting day-off request: "Review Day Offs" opens that one.
    public int? FirstDayOffProviderId { get; init; }
    /// Database v2.8: name change requests still Pending.
    public int NameChangeRequests { get; init; }
    public int PendingApplications { get; init; }
    /// The oldest waiting application: "Review Providers" opens that one first (fair queue).
    public int? FirstPendingProviderId { get; init; }
    public int SuspendedCustomers { get; init; }
    public int SuspendedProviders { get; init; }
    public int FlaggedReviews { get; init; }
    /// Database v2.3: dbo.usp_GetBookingsNeedingAttention (a flagged or 1-2 star review, or a passed date still open).
    public int BookingsNeedingAttention { get; init; }
    /// Database v2.10 (replaces the v2.9 REPORT lines): while automatic deletion is OFF, the accounts the daily clean-up
    /// would warn (335+ days without a sign-in) and how many of them are past a year. 0 when deletion is on.
    public int InactiveAccounts { get; init; }
    public int InactiveOverOneYear { get; init; }
}

public static class AdminAttention
{
    /// Shown when nothing waits.
    public const string AllCaughtUp = "All caught up! No pending requests requiring admin attention.";

    /// Builds the "Needs Admin Attention" rows. A row only exists when its count is above zero, so the list never
    /// shows something that is not really waiting. Red dot = an account or an applicant waits; amber = a request.
    public static List<AdminAttentionItem> Build(AdminAttentionCounts c)
    {
        var rows = new List<AdminAttentionItem>();

        if (c.DayOffRequests > 0)
            rows.Add(new()
            {
                Key = "day-off",
                Tone = "warning",
                Text = $"{Plural.Of(c.DayOffRequests, "provider day-off request")} awaiting approval",
                ActionLabel = "Review Day Offs",
                Action = AdminAttentionAction.ReviewDayOffs,
                TargetId = c.FirstDayOffProviderId
            });

        if (c.NameChangeRequests > 0)
            rows.Add(new()
            {
                Key = "name-change",
                Tone = "warning",
                Text = $"{Plural.Of(c.NameChangeRequests, "name change request")} pending review",
                ActionLabel = "Review Requests",
                Action = AdminAttentionAction.NameChangeList
            });

        if (c.PendingApplications > 0)
            rows.Add(new()
            {
                Key = "application",
                Tone = "danger",
                Text = $"{Plural.Of(c.PendingApplications, "new service provider application")} pending",
                ActionLabel = "Review Providers",
                Action = c.FirstPendingProviderId is null ? AdminAttentionAction.ApplicationList : AdminAttentionAction.ReviewApplication,
                TargetId = c.FirstPendingProviderId
            });

        var suspended = c.SuspendedCustomers + c.SuspendedProviders;
        if (suspended > 0)
        {
            var row = new AdminAttentionItem
            {
                Key = "suspended",
                Tone = "danger",
                Text = $"{Plural.Of(suspended, "suspended account")} requiring review",
                ActionLabel = "View Accounts",
                // customers are on Admin > Users, providers on Admin > Service Providers
                Action = c.SuspendedCustomers > 0 ? AdminAttentionAction.SuspendedCustomers : AdminAttentionAction.SuspendedProviders
            };
            if (c.SuspendedCustomers > 0 && c.SuspendedProviders > 0)
            {
                row.Text += $" ({Plural.Of(c.SuspendedCustomers, "customer")}, {Plural.Of(c.SuspendedProviders, "provider")})";
                row.SecondaryLabel = "Suspended providers";
                row.SecondaryAction = AdminAttentionAction.SuspendedProviders;
            }
            rows.Add(row);
        }

        if (c.FlaggedReviews > 0)
            rows.Add(new()
            {
                Key = "review",
                Tone = "warning",
                Text = c.FlaggedReviews == 1
                    ? "1 flagged review needs moderation"
                    : $"{c.FlaggedReviews} flagged reviews need moderation",
                ActionLabel = "Moderate Reviews",
                Action = AdminAttentionAction.ModerateReviews
            });

        if (c.BookingsNeedingAttention > 0)
            rows.Add(new()
            {
                Key = "booking",
                Tone = "violet",
                Text = c.BookingsNeedingAttention == 1
                    ? "1 booking needs attention (low rating or date passed)"
                    : $"{c.BookingsNeedingAttention} bookings need attention (low ratings or dates passed)",
                ActionLabel = c.BookingsNeedingAttention == 1 ? "View Booking" : "View Bookings",
                Action = AdminAttentionAction.BookingList
            });

        if (c.InactiveAccounts > 0)
            rows.Add(new()
            {
                Key = "inactive",
                Tone = "info",
                Text = $"{Plural.Of(c.InactiveAccounts, "inactive account")} with no sign-in for 335+ days"
                     + (c.InactiveOverOneYear > 0 ? $" ({c.InactiveOverOneYear} over a year)" : "")
                     + "; automatic deletion is off",
                ActionLabel = "View Users",
                Action = AdminAttentionAction.UserList
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
