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
    public static PaymentDisplay For(string paymentStatus) => paymentStatus switch
    {
        PaymentStatuses.Paid => new("Paid", "completed"),         // green
        PaymentStatuses.Refunded => new("Refunded", "refunded"),  // purple
        PaymentStatuses.Unpaid => new("Unpaid", "muted"),         // grey
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
