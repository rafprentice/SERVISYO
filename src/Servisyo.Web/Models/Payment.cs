using System.Text.RegularExpressions;

namespace Servisyo.Web.Models;

/// PAYMENTS: one payment record per booking, made when the customer approves the quotation ("Approve & Pay").
/// DEMO ONLY: no money moves and no real GCash / Maya account is called.
///   CashOnCompletion -> PendingCollection, then Paid when the provider confirms the cash after the job
///   MockEWallet      -> Paid at once (test GCash / Maya)
/// BOOKINGS.PAYMENT_STATUS keeps the three values from the project PDF (Unpaid, Paid, Refunded); it becomes Paid when
/// the money is really in (e-wallet at once, cash when the provider confirms it).
public class Payment
{
    public int PaymentId { get; set; }
    public int BookingId { get; set; }
    public decimal Amount { get; set; }                                        // copied from the booking's fee on the server
    public string PaymentMethod { get; set; } = PaymentMethods.CashOnCompletion;
    public string PaymentStatus { get; set; } = PaymentRecordStatuses.PendingCollection;
    public string TransactionReference { get; set; } = string.Empty;          // "CASH-2026-0042" or "GCASH-48213907"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }                                      // null while cash is still to be collected

    public Booking Booking { get; set; } = null!;
    public CashPaymentDetails? CashDetails { get; set; }                       // only for CashOnCompletion
}

/// CASH_PAYMENT_DETAILS: the cash side of a payment (one-to-one with PAYMENTS, same key).
public class CashPaymentDetails
{
    public int PaymentId { get; set; }
    public bool IsCollectedByProvider { get; set; }
    public DateTime? CollectedAt { get; set; }

    public Payment Payment { get; set; } = null!;
}

public static class PaymentMethods
{
    public const string CashOnCompletion = "CashOnCompletion";
    public const string MockEWallet = "MockEWallet";

    /// Only the two known values; anything else the browser sends is refused (null).
    public static string? Normalize(string? value) => value?.Trim() switch
    {
        CashOnCompletion => CashOnCompletion,
        MockEWallet => MockEWallet,
        _ => null
    };
}

/// PAYMENTS.PAYMENT_STATUS (not the same list as BOOKINGS.PAYMENT_STATUS).
public static class PaymentRecordStatuses
{
    public const string PendingCollection = "PendingCollection";
    public const string Paid = "Paid";
    public const string Refunded = "Refunded";
}

/// The two pretend wallets of the "Mock E-Wallet" option and their reference prefixes.
public static class MockWallets
{
    public const string GCash = "GCash";
    public const string Maya = "Maya";

    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "gcash" => GCash,
        "maya" => Maya,
        _ => null
    };

    public static string Prefix(string wallet) => wallet == Maya ? "MAYA" : "GCASH";

    /// "GCASH-48213907" -> "GCash" (null for a cash reference).
    public static string? FromReference(string? reference) =>
        reference is null ? null
        : reference.StartsWith("GCASH-", StringComparison.Ordinal) ? GCash
        : reference.StartsWith("MAYA-", StringComparison.Ordinal) ? Maya
        : null;

    /// A test reference: the prefix, a dash and exactly 8 digits.
    public static readonly Regex ReferencePattern = new(@"^(GCASH|MAYA)-[0-9]{8}$", RegexOptions.CultureInvariant);

    /// A Philippine mobile number: 09XXXXXXXXX (dashes and spaces allowed), or +639XXXXXXXXX.
    public static bool IsMobileNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var digits = value.Trim().Replace(" ", "").Replace("-", "");
        return Regex.IsMatch(digits, @"^(09[0-9]{9}|\+?639[0-9]{9})$", RegexOptions.CultureInvariant);
    }
}

/// What the progress meter and the provider's buttons read. A booking whose customer chose Cash on Completion stays
/// "Unpaid" in BOOKINGS until the provider confirms the cash, but the customer HAS approved the quotation, so the
/// booking is scheduled: here it counts as "PendingCollection", which is treated like Paid for the meter and for
/// "Mark In Progress".
public static class PaymentProgress
{
    public static string Effective(string bookingPaymentStatus, string? paymentRecordStatus) =>
        bookingPaymentStatus == PaymentStatuses.Unpaid && paymentRecordStatus == PaymentRecordStatuses.PendingCollection
            ? PaymentRecordStatuses.PendingCollection
            : bookingPaymentStatus;

    /// The customer approved the quotation and either paid or promised cash on completion.
    public static bool IsArranged(string effectivePaymentStatus) =>
        effectivePaymentStatus is PaymentStatuses.Paid or PaymentRecordStatuses.PendingCollection;
}
