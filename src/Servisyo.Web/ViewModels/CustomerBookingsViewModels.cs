using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// One filter tab on "My Bookings": the key used in the address, the label, and the booking status it filters by
/// (null = every status).
public sealed record CustomerBookingTab(string Key, string Label, string? Status)
{
    public const string AllKey = "all";

    /// In the order a booking moves through its life.
    public static readonly IReadOnlyList<CustomerBookingTab> All = new[]
    {
        new CustomerBookingTab(AllKey, "All", null),
        new CustomerBookingTab("pending", "Pending", BookingStatuses.Pending),
        new CustomerBookingTab("accepted", "Accepted", BookingStatuses.Accepted),
        new CustomerBookingTab("in-progress", "In Progress", BookingStatuses.InProgress),
        new CustomerBookingTab("completed", "Completed", BookingStatuses.Completed),
        new CustomerBookingTab("rejected", "Rejected", BookingStatuses.Rejected),
        new CustomerBookingTab("cancelled", "Cancelled", BookingStatuses.Cancelled)
    };

    /// Anything the browser sends that is not one of the known keys falls back to "All".
    public static CustomerBookingTab Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

public class CustomerBookingTabViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class CustomerBookingCardViewModel
{
    public int BookingId { get; set; }
    public string Title { get; set; } = string.Empty;           // "Pipe Repair" or "Pipe Repair & Waterproofing"
    public string ProviderName { get; set; } = string.Empty;    // the person: "Marco Reyes"
    public string? CategoryText { get; set; }                   // "Plumbing" (or "Plumbing, Cleaning")
    public string Status { get; set; } = string.Empty;
    public string Tone { get; set; } = "muted";                 // the .sv-tag--{tone} colour
    public string WhenText { get; set; } = string.Empty;        // "Sep 20, 2026 · 9:00 AM"
    public string? Location { get; set; }
    public string SubmittedText { get; set; } = string.Empty;   // "Sep 16, 2026"
    public string? Notes { get; set; }

    /// Only a Pending request can be cancelled by the customer (project rule).
    public bool CanCancel { get; set; }

    /// Completed and not reviewed yet.
    public bool CanReview { get; set; }

    /// Completed and the customer has already left a review (a booking can have only one).
    public bool IsReviewed { get; set; }

    /// Accepted (quotation given) and not paid yet: "Approve & Pay" is offered.
    public bool CanPay { get; set; }
    public bool IsPaid { get; set; }
    public decimal Fee { get; set; }

    /// "Marco Reyes · Plumbing"
    public string FromLine => string.IsNullOrWhiteSpace(CategoryText) ? ProviderName : $"{ProviderName} · {CategoryText}";
}

public class CustomerBookingsPageViewModel
{
    public const int PageSize = 10;

    public PortalShellViewModel Shell { get; set; } = new() { Role = "Customer" };
    public string ActiveTab { get; set; } = CustomerBookingTab.AllKey;
    public IReadOnlyList<CustomerBookingTabViewModel> Tabs { get; set; } = Array.Empty<CustomerBookingTabViewModel>();
    public IReadOnlyList<CustomerBookingCardViewModel> Cards { get; set; } = Array.Empty<CustomerBookingCardViewModel>();

    public int Page { get; set; } = 1;
    public int TotalMatching { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalMatching / (double)PageSize));

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }

    public bool HasAnyBookings => Tabs.FirstOrDefault(t => t.Key == CustomerBookingTab.AllKey)?.Count > 0;
}

public record CustomerBookingsResult(bool Allowed, CustomerBookingsPageViewModel? Page);

public enum CustomerCancelOutcome
{
    Success,
    NotFound,      // no such booking, or it belongs to another customer (the same answer on purpose)
    NotPending,    // only a Pending request can be cancelled
    Changed        // the provider answered at the same moment
}

public record CustomerCancelResult(bool Allowed, CustomerCancelOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == CustomerCancelOutcome.Success;
}

/// What the browser posts with the Cancel button. The booking id is in the address and the customer
/// always comes from the sign-in cookie; the form only remembers which tab and page to come back to.
public class CancelBookingForm
{
    public string? Tab { get; set; }
    public int Page { get; set; } = 1;
}
