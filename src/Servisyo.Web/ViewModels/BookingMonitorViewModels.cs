using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

public class BookingListQuery
{
    public string? Q { get; set; }
    public string? Status { get; set; }      // All | Pending | Accepted | In Progress | Completed | Rejected | Cancelled
    public int Page { get; set; } = 1;
}

public static class BookingFilters
{
    public const string All = "All";

    /// Accepts "In Progress", "in-progress", "inprogress"... and falls back to All for anything unknown.
    public static string NormalizeStatus(string? status)
    {
        var key = new string((status ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return key switch
        {
            "pending" => BookingStatuses.Pending,
            "accepted" => BookingStatuses.Accepted,
            "inprogress" => BookingStatuses.InProgress,
            "completed" => BookingStatuses.Completed,
            "rejected" => BookingStatuses.Rejected,
            "cancelled" or "canceled" => BookingStatuses.Cancelled,
            _ => All
        };
    }
}

/// How a payment status is shown: text + colour.
public sealed record PaymentDisplay(string Text, string Tone)
{
    /// paymentStatus may be PaymentProgress.Effective(...): "PendingCollection" = cash on completion, not collected yet.
    public static PaymentDisplay For(string paymentStatus) => paymentStatus switch
    {
        PaymentStatuses.Paid => new("Paid", "completed"),         // green
        PaymentStatuses.Refunded => new("Refunded", "refunded"),  // purple
        PaymentStatuses.Unpaid => new("Unpaid", "muted"),         // grey
        PaymentRecordStatuses.PendingCollection => new("Cash due", "pending"),   // amber
        _ => new(paymentStatus, "muted")
    };
}

public sealed record BookingTabViewModel(string Key, string Label, int Count);

public class BookingRowViewModel
{
    public int BookingId { get; set; }
    public string Service { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public PaymentDisplay Payment { get; set; } = PaymentDisplay.For(string.Empty);
    public AdminBookingDisplay Status { get; set; } = AdminBookingDisplay.For(string.Empty);
    /// Database v2.12: cancelled because the provider did not start it within 2 hours (a "No-show" tag in the list).
    public bool IsNoShow { get; set; }
}

public class BookingsPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };
    public string Q { get; set; } = string.Empty;
    public string Status { get; set; } = BookingFilters.All;        // normalised
    public IReadOnlyList<BookingTabViewModel> Tabs { get; set; } = Array.Empty<BookingTabViewModel>();
    public int CountAll { get; set; }
    public int TotalMatching { get; set; }
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public IReadOnlyList<BookingRowViewModel> Rows { get; set; } = Array.Empty<BookingRowViewModel>();
}

/// One service line of a booking (the price and duration are the snapshot taken when the booking was made).
public sealed record BookingDetailsServiceLine(string Name, string Category, string? Description, decimal Price, int DurationMinutes);

/// Admin > Bookings > "View": everything about one booking, read-only (shown in a pop-up, or as a page without JavaScript).
public class BookingDetailsViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };

    public int BookingId { get; set; }
    public string Reference { get; set; } = string.Empty;          // "BK-2026-0027", the same reference the customer sees
    public AdminBookingDisplay Status { get; set; } = AdminBookingDisplay.For(string.Empty);

    // 1. general
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public DateTime CreatedAtCebu { get; set; }                     // stored in UTC, shown in Cebu time (UTC+8)

    // 2. service summary
    public IReadOnlyList<BookingDetailsServiceLine> Services { get; set; } = Array.Empty<BookingDetailsServiceLine>();
    public string? RequestDescription { get; set; }                 // what the customer wrote when booking
    public decimal QuotedTotal { get; set; }                        // BOOKINGS.TOTAL_FEE

    // database v2.11: the provider's proof of the finished job
    public bool IsCompleted { get; set; }
    public CompletionProofViewModel? Proof { get; set; }            // null = not completed, or completed before v2.11
    public StartInfoViewModel? StartInfo { get; set; }              // database v2.12: when the job started (and how late, why)
    public bool IsNoShow { get; set; }                               // database v2.12: cancelled, not started within 2 hours

    // 3. customer
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? ServiceAddress { get; set; }

    // 4. service provider
    public string ProviderName { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string ProviderEmail { get; set; } = string.Empty;
    public string? ProviderPhone { get; set; }

    // 5. financial and payment
    public decimal TotalAmount { get; set; }                        // the payment's amount, or the booking fee before payment
    public string? PaymentMethod { get; set; }                      // "Cash on Completion" / "Mock E-Wallet (GCash)"; null = not chosen yet
    public string? TransactionReference { get; set; }
    public PaymentDisplay Payment { get; set; } = PaymentDisplay.For(string.Empty);

    // where "Close" goes back to when this is shown as a page (the same tab, search and page of the list)
    public string ReturnStatus { get; set; } = BookingFilters.All;
    public string? ReturnQ { get; set; }
    public int ReturnPage { get; set; } = 1;

    public static string ReferenceFor(int bookingId, DateTime createdAtUtc) => $"BK-{createdAtUtc.Year}-{bookingId:D4}";

    /// "CashOnCompletion" -> "Cash on Completion"; "MockEWallet" + "GCASH-..." -> "Mock E-Wallet (GCash)".
    public static string? PaymentMethodText(string? method, string? reference) => method switch
    {
        null => null,
        PaymentMethods.CashOnCompletion => "Cash on Completion",
        PaymentMethods.MockEWallet => MockWallets.FromReference(reference) is string wallet ? $"Mock E-Wallet ({wallet})" : "Mock E-Wallet",
        _ => method
    };
}

/// NotAllowed = the signed-in user is not an Active administrator; Details null = no such booking.
public sealed record BookingDetailsLookup(bool Allowed, BookingDetailsViewModel? Details);
