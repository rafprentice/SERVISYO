using System.Globalization;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// The four things a provider can do to a booking from the Booking Requests page.
/// (Customers cancel their own Pending bookings from their side; that is not a provider action.)
public enum BookingAction { Accept, Reject, Start, Complete }

/// How one action looks on its button, and the rules for it. The rules mirror usp_UpdateBookingStatus in the PDF:
/// Pending -> Accepted | Rejected, Accepted -> In Progress, In Progress -> Completed. Nothing else is allowed.
public sealed record BookingActionInfo(BookingAction Action, string Label, string FromStatus, string ToStatus, string Css, string? Icon, bool Confirm)
{
    public static readonly BookingActionInfo Accept = new(BookingAction.Accept, "Accept", BookingStatuses.Pending, BookingStatuses.Accepted, "sv-btn--primary", "check", false);
    public static readonly BookingActionInfo Reject = new(BookingAction.Reject, "Reject", BookingStatuses.Pending, BookingStatuses.Rejected, "sv-btn--danger-outline", null, true);
    public static readonly BookingActionInfo Start = new(BookingAction.Start, "Mark In Progress", BookingStatuses.Accepted, BookingStatuses.InProgress, "sv-btn--primary", "clock", false);
    public static readonly BookingActionInfo Complete = new(BookingAction.Complete, "Mark Completed", BookingStatuses.InProgress, BookingStatuses.Completed, "sv-btn--primary", "check", false);

    public static BookingActionInfo For(BookingAction action) => action switch
    {
        BookingAction.Accept => Accept,
        BookingAction.Reject => Reject,
        BookingAction.Start => Start,
        BookingAction.Complete => Complete,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown booking action.")
    };

    /// Like ForStatus, but an Accepted booking only offers "Mark In Progress" once the customer has approved the quotation
    /// and paid (or chosen cash on completion). paymentStatus is PaymentProgress.Effective(...).
    public static IReadOnlyList<BookingActionInfo> ForBooking(string status, string paymentStatus) =>
        status == BookingStatuses.Accepted && !PaymentProgress.IsArranged(paymentStatus)
            ? Array.Empty<BookingActionInfo>()
            : ForStatus(status);

    /// The buttons that make sense for a booking in this status (empty once it is finished or cancelled).
    public static IReadOnlyList<BookingActionInfo> ForStatus(string status) => status switch
    {
        BookingStatuses.Pending => new[] { Accept, Reject },
        BookingStatuses.Accepted => new[] { Start },
        BookingStatuses.InProgress => new[] { Complete },
        _ => Array.Empty<BookingActionInfo>()
    };
}

/// One filter tab: the address key, the label, and the booking status it filters by (null = every status).
public sealed record ProviderBookingTab(string Key, string Label, string? Status)
{
    public const string AllKey = "all";

    public static readonly IReadOnlyList<ProviderBookingTab> All = new[]
    {
        new ProviderBookingTab(AllKey, "All", null),
        new ProviderBookingTab("pending", "Pending", BookingStatuses.Pending),
        new ProviderBookingTab("accepted", "Accepted", BookingStatuses.Accepted),
        new ProviderBookingTab("in-progress", "In Progress", BookingStatuses.InProgress),
        new ProviderBookingTab("completed", "Completed", BookingStatuses.Completed),
        new ProviderBookingTab("rejected", "Rejected", BookingStatuses.Rejected),
        new ProviderBookingTab("cancelled", "Cancelled", BookingStatuses.Cancelled)
    };

    /// Anything the browser sends that is not one of the known keys falls back to "All".
    public static ProviderBookingTab Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

/// How a booking status is shown on this page: the badge text is the status itself ("Accepted", "Rejected", ...).
public static class ProviderBookingBadge
{
    public static string Tone(string status) => status switch
    {
        BookingStatuses.Pending => "pending",
        BookingStatuses.Accepted => "accepted",
        BookingStatuses.InProgress => "progress",
        BookingStatuses.Completed => "completed",
        BookingStatuses.Rejected => "danger",
        _ => "muted"
    };
}

public static class BookingFormat
{
    private const int ManilaOffsetHours = 8;      // the Philippines has no daylight saving

    /// "Sep 20, 2026 · 9:00 AM"
    public static string DateTimeText(DateOnly date, TimeOnly time) =>
        $"{date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)} · {time.ToString("h:mm tt", CultureInfo.InvariantCulture)}";

    /// "Sep 16, 2026": the day the request was sent, in Cebu time (it is stored in UTC).
    public static string SubmittedText(DateTime createdAtUtc) =>
        createdAtUtc.AddHours(ManilaOffsetHours).ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}

public class ProviderBookingTabViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ProviderBookingCardViewModel
{
    public int BookingId { get; set; }
    public string Title { get; set; } = string.Empty;           // "Pipe Repair" or "Pipe Repair & Waterproofing"
    public string CustomerName { get; set; } = string.Empty;
    public string? CategoryText { get; set; }                   // "Plumbing" (or "Plumbing, Cleaning")
    public string Status { get; set; } = string.Empty;
    public string Tone { get; set; } = "muted";
    public string WhenText { get; set; } = string.Empty;        // "Sep 20, 2026 · 9:00 AM"
    public string? Location { get; set; }
    public string SubmittedText { get; set; } = string.Empty;   // "Sep 16, 2026"
    public string? Notes { get; set; }
    public IReadOnlyList<BookingActionInfo> Actions { get; set; } = Array.Empty<BookingActionInfo>();
    /// PaymentProgress.Effective(...): Unpaid, Paid, Refunded, or PendingCollection (cash on completion, not collected yet).
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Fee { get; set; }

    /// Accepted, quotation sent, customer has not approved and paid yet.
    public bool AwaitingPayment => Status == BookingStatuses.Accepted && !PaymentProgress.IsArranged(PaymentStatus);
    /// Accepted and paid (or cash promised): scheduled, ready to start on the day.
    public bool IsScheduled => Status == BookingStatuses.Accepted && PaymentProgress.IsArranged(PaymentStatus);

    /// The payment line on the card (null = nothing to say).
    public PaymentNote? Payment { get; set; }
    /// Completed + Cash on Completion + not confirmed yet: "Confirm Cash Received" is offered.
    public bool CanConfirmCash { get; set; }

    public string FromLine => string.IsNullOrWhiteSpace(CategoryText)
        ? $"from {CustomerName}"
        : $"from {CustomerName} · {CategoryText}";
}

public class ProviderBookingsPageViewModel
{
    public const int PageSize = 20;

    public PortalShellViewModel Shell { get; set; } = new() { Role = "Provider" };
    public string ActiveTab { get; set; } = ProviderBookingTab.AllKey;
    public IReadOnlyList<ProviderBookingTabViewModel> Tabs { get; set; } = Array.Empty<ProviderBookingTabViewModel>();
    public IReadOnlyList<ProviderBookingCardViewModel> Cards { get; set; } = Array.Empty<ProviderBookingCardViewModel>();

    public int Page { get; set; } = 1;
    public int TotalMatching { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalMatching / (double)PageSize));

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }
}

public record ProviderBookingsResult(ProviderDashboardAccess Access, ProviderBookingsPageViewModel? Page);

public enum BookingActionOutcome
{
    Success,
    NotFound,            // no such booking, or it belongs to another provider (the same answer on purpose)
    InvalidTransition,   // the booking is not in a status that allows this action
    Overlap,             // accepting would clash with another accepted / in-progress job
    Changed              // someone else (usually the customer cancelling) changed it a moment ago
}

public record BookingActionResult(ProviderDashboardAccess Access, BookingActionOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == BookingActionOutcome.Success;
}

/// What the browser posts with an action button. Only the action name is taken from the browser;
/// the booking id is in the address and the provider always comes from the sign-in cookie.
public class BookingActionForm
{
    public BookingAction? Action { get; set; }      // nullable on purpose: a missing value must never become "Accept"
    public string? Tab { get; set; }
    public int Page { get; set; } = 1;
}
