using System.Text.RegularExpressions;

namespace Servisyo.Web.Models;

/// Database v2.3 - PAYMENTS: the list of payment methods (a lookup table). Fixed rows: 1 Cash on Completion,
/// 2 GCash, 3 Maya (see PaymentIds). DEMO ONLY: no money moves and no real GCash / Maya account is called.
public class Payment
{
    public int PaymentId { get; set; }
    public string MethodName { get; set; } = string.Empty;

    public ICollection<PaymentDetail> Details { get; set; } = new List<PaymentDetail>();
}

/// Database v2.3 - PAYMENTS_DTL: the bridge between BOOKINGS and PAYMENTS: one booking's payment (one per booking),
/// made when the customer approves the quotation ("Approve & Pay").
///   Cash on Completion -> PendingCollection, then Paid (PAID_AT) when the provider confirms the cash after the job
///   GCash / Maya       -> Paid at once (test e-wallet)
/// Not stored again (rules: one fact in one place, nothing that can be calculated):
///   the amount           = the booking's TOTAL_FEE
///   "cash collected"     = a cash payment whose status is Paid, collected at PAID_AT (was CASH_PAYMENT_DETAILS)
/// BOOKINGS.PAYMENT_STATUS keeps the three values from the project PDF (Unpaid, Paid, Refunded).
public class PaymentDetail
{
    public int PaymentDtlId { get; set; }
    public int BookingId { get; set; }
    public int PaymentId { get; set; }                                        // PAYMENTS: the method
    public string PaymentStatus { get; set; } = PaymentRecordStatuses.PendingCollection;
    public string TransactionReference { get; set; } = string.Empty;          // "CASH-2026-0042" or "GCASH-48213907"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }                                      // null while cash is still to be collected

    public Booking Booking { get; set; } = null!;
    public Payment Payment { get; set; } = null!;
}

/// The fixed rows of PAYMENTS (seeded by the DerivedData migration; CK_PAYMENTS_DTL_PENDING_CASH uses row 1).
public static class PaymentIds
{
    public const int Cash = 1;
    public const int GCash = 2;
    public const int Maya = 3;

    public const string CashName = "Cash on Completion";
    public const string GCashName = "GCash";
    public const string MayaName = "Maya";

    public static readonly IReadOnlyList<(int Id, string Name)> All = new[] { (Cash, CashName), (GCash, GCashName), (Maya, MayaName) };

    /// The row for what the customer chose: cash, or the test e-wallet with GCash / Maya.
    public static int For(string method, string? wallet) =>
        method == PaymentMethods.CashOnCompletion ? Cash : wallet == MockWallets.Maya ? Maya : GCash;

    /// The two choices of the payment window ("CashOnCompletion" / "MockEWallet"), as the screens and messages use them.
    public static string MethodOf(int paymentId) => paymentId == Cash ? PaymentMethods.CashOnCompletion : PaymentMethods.MockEWallet;

    public static string? WalletOf(int paymentId) => paymentId switch { GCash => MockWallets.GCash, Maya => MockWallets.Maya, _ => null };
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
