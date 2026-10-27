using System.Globalization;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

public enum DashboardState
{
    NoBooking,
    WaitingForQuotation,
    QuotationReceived,
    BookingConfirmed,
    ServiceInProgress,
    ServiceCompleted
}

/// What the "current booking" card shows for one state.
public sealed record DashboardStateInfo(
    string Label,          // badge text
    string Tone,           // css tone: neutral | warning | success | info
    string Message,        // footer sentence
    string? ActionLabel,   // primary button text (null = no button)
    int ActiveStep,        // 1..6 (the highlighted step)
    bool AllDone);         // true = every step shows a check mark

public static class DashboardStates
{
    public static readonly string[] StepLabels =
        { "Requested", "Quotation", "Paid", "Scheduled", "In Progress", "Completed" };

    /// Turns the REAL booking columns (STATUS + PAYMENT_STATUS, plus the PAYMENTS record) into a dashboard state.
    /// paymentStatus is PaymentProgress.Effective(...): "PendingCollection" when the customer chose cash on completion.
    ///   Pending                          -> Waiting for Quotation
    ///   Accepted  + nothing chosen yet   -> Quotation Received  (the provider accepted at the quoted fee)
    ///   Accepted  + Paid / cash promised -> Booking Confirmed   (Paid / Scheduled)
    ///   In Progress                      -> Service In Progress
    ///   Completed                        -> Service Completed
    ///   Rejected / Cancelled             -> No active booking
    public static DashboardState FromBooking(string status, string paymentStatus) => status switch
    {
        BookingStatuses.Pending => DashboardState.WaitingForQuotation,
        BookingStatuses.Accepted => PaymentProgress.IsArranged(paymentStatus)
            ? DashboardState.BookingConfirmed
            : DashboardState.QuotationReceived,
        BookingStatuses.InProgress => DashboardState.ServiceInProgress,
        BookingStatuses.Completed => DashboardState.ServiceCompleted,
        _ => DashboardState.NoBooking
    };

    /// cashOnCompletion = the customer chose to pay cash when the job is done (and has not paid it yet).
    public static DashboardStateInfo Describe(DashboardState state, decimal fee, string providerName, bool cashOnCompletion = false)
    {
        var amount = "₱" + fee.ToString("#,##0.##", CultureInfo.InvariantCulture);
        if (state == DashboardState.BookingConfirmed && cashOnCompletion)
            return new("Booking Confirmed", "success",
                $"Your booking is confirmed. Pay {amount} in cash to {providerName} when the job is done.", "Track Booking", 4, false);
        return state switch
        {
            DashboardState.WaitingForQuotation => new(
                "Waiting for Quotation", "neutral",
                "Waiting for provider to send a quotation...", null, 1, false),
            DashboardState.QuotationReceived => new(
                "Quotation Received", "warning",
                $"{amount} quotation received — review and confirm to proceed.", "Approve & Pay", 2, false),
            DashboardState.BookingConfirmed => new(
                "Booking Confirmed", "success",
                "Your booking is confirmed. The provider will arrive on schedule.", "Track Booking", 4, false),
            DashboardState.ServiceInProgress => new(
                "Service In Progress", "info",
                "Service is currently in progress at your location.", "Track Service", 5, false),
            DashboardState.ServiceCompleted => new(
                "Service Completed", "success",
                $"Service complete — how was your experience with {providerName}?", "Leave a Review", 6, true),
            _ => new("No Booking", "neutral", "", null, 0, false)
        };
    }
}

public class CurrentBookingViewModel
{
    public int BookingId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    /// The provider's USER id (what the Messages page uses to open a conversation).
    public int ProviderUserId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public decimal Fee { get; set; }
    /// Only filled when the booking is confirmed, e.g. "Tomorrow · 2:00 PM — Pipe Repair with Juan Plumbing".
    public string? UpcomingText { get; set; }

    /// How it is paid ("Cash on completion · ...", "Paid via GCash (test) · ..."), or null before Approve & Pay.
    public PaymentNote? Payment { get; set; }
    /// Cash on completion, not handed over yet.
    public bool CashToCollect { get; set; }
}

public class RecentBookingViewModel
{
    public int BookingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string Status { get; set; } = string.Empty;

    /// For "Book Again": the same provider, with the same services ticked.
    public int ProviderId { get; set; }
    public IReadOnlyList<int> ServiceIds { get; set; } = Array.Empty<int>();
}

public class RecommendedProviderViewModel
{
    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public decimal? AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public string? CategoryName { get; set; }
    public string? AvailabilityText { get; set; }
}

public class CustomerDashboardViewModel
{
    public PortalShellViewModel Shell { get; set; } = new();
    public DashboardState State { get; set; } = DashboardState.NoBooking;
    public CurrentBookingViewModel? Current { get; set; }
    public IReadOnlyList<RecentBookingViewModel> Recent { get; set; } = Array.Empty<RecentBookingViewModel>();
    public IReadOnlyList<RecommendedProviderViewModel> Recommended { get; set; } = Array.Empty<RecommendedProviderViewModel>();
}
