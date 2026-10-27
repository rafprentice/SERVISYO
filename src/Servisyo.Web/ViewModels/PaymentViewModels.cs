using System.Globalization;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// What the "Approve & Pay" window posts. The booking id is in the address, the customer comes from the sign-in
/// cookie and the amount from the booking itself: the browser only says HOW the customer wants to pay.
public class PaymentChoiceForm
{
    public string? Method { get; set; }          // CashOnCompletion | MockEWallet
    public string? Wallet { get; set; }          // GCash | Maya            (Mock E-Wallet only)
    public string? MobileNumber { get; set; }    // test number, e.g. 0917-000-0000 (checked, never saved)
    public string? Reference { get; set; }       // the test reference shown in the window (checked; replaced if not valid)
}

/// The "Approve & Pay" window for one booking (Views/Shared/_PayChoice.cshtml).
public class PayChoiceViewModel
{
    public int BookingId { get; set; }
    public decimal Amount { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// Where to come back to after paying: "dashboard" or "bookings" (the server only knows these two).
    public string ReturnTo { get; set; } = "dashboard";
    public string AmountText => PaymentNotes.Money(Amount);
}

/// One line about the payment on a booking card. Tone: "wait" (amber) or "ok" (green).
public sealed record PaymentNote(string Text, string Tone, string Icon, string? Reference);

/// The payment details of one booking, read together with the booking (null fields = no PAYMENTS row yet).
public sealed record PaymentFacts(string BookingStatus, string BookingPaymentStatus, string? Method, string? RecordStatus, string? Reference, decimal Amount)
{
    public string Effective => PaymentProgress.Effective(BookingPaymentStatus, RecordStatus);
    public bool IsCash => Method == PaymentMethods.CashOnCompletion;
    public bool CashToCollect => IsCash && RecordStatus == PaymentRecordStatuses.PendingCollection;
    public string? WalletName => MockWallets.FromReference(Reference);
}

public static class PaymentNotes
{
    public static string Money(decimal v) => "₱" + v.ToString("#,##0.##", CultureInfo.InvariantCulture);

    private static string WalletText(PaymentFacts f) => (f.WalletName ?? "e-wallet") + " (test)";

    /// The customer's line on the dashboard and on My Bookings. Null = nothing to say (no payment yet).
    public static PaymentNote? ForCustomer(PaymentFacts f, string providerName)
    {
        var amount = Money(f.Amount);
        if (f.RecordStatus == PaymentRecordStatuses.Refunded)
            return new($"Refunded · {amount}", "ok", "check", f.Reference);
        if (f.CashToCollect)
            return f.BookingStatus == BookingStatuses.Completed
                ? new($"Cash on completion · Please pay {amount} in cash to {providerName}.", "wait", "banknote", f.Reference)
                : new($"Cash on completion · You'll pay {amount} in cash to {providerName} when the job is done.", "wait", "banknote", f.Reference);
        if (f.IsCash && f.RecordStatus == PaymentRecordStatuses.Paid)
            return new($"Paid in cash · {amount} received by {providerName}", "ok", "check", f.Reference);
        if (f.Method == PaymentMethods.MockEWallet && f.RecordStatus == PaymentRecordStatuses.Paid)
            return new($"Paid via {WalletText(f)} · {amount}", "ok", "check", f.Reference);
        if (f.BookingPaymentStatus == PaymentStatuses.Paid)
            return new($"Paid · {amount}", "ok", "check", null);          // paid before payment records existed
        return null;
    }

    /// The provider's line on a Booking Requests card. Null = nothing to say.
    public static PaymentNote? ForProvider(PaymentFacts f, string customerName)
    {
        var amount = Money(f.Amount);
        var accepted = f.BookingStatus == BookingStatuses.Accepted;

        if (accepted && !PaymentProgress.IsArranged(f.Effective))
            return new("Quotation sent — waiting for the customer to approve and pay.", "wait", "clock", null);
        if (f.RecordStatus == PaymentRecordStatuses.Refunded)
            return new($"Refunded · {amount}", "ok", "check", f.Reference);

        if (f.CashToCollect)
        {
            return f.BookingStatus switch
            {
                BookingStatuses.Accepted => new($"Cash on completion — scheduled. Collect {amount} from {customerName} when the job is done. Mark it In Progress when you start.", "wait", "banknote", f.Reference),
                BookingStatuses.InProgress => new($"Cash on completion — collect {amount} from {customerName} when you finish.", "wait", "banknote", f.Reference),
                BookingStatuses.Completed => new($"Collect {amount} in cash from {customerName}, then press Confirm Cash Received.", "wait", "banknote", f.Reference),
                _ => new($"Cash on completion · {amount} not collected", "wait", "banknote", f.Reference)
            };
        }

        if (f.IsCash && f.RecordStatus == PaymentRecordStatuses.Paid)
            return new($"Cash received · {amount}", "ok", "check", f.Reference);

        if (f.Method == PaymentMethods.MockEWallet && f.RecordStatus == PaymentRecordStatuses.Paid)
            return accepted
                ? new($"Paid via {WalletText(f)} — scheduled. Mark it In Progress when you start the job.", "ok", "check", f.Reference)
                : new($"Paid via {WalletText(f)} · {amount}", "ok", "check", f.Reference);

        if (accepted && f.BookingPaymentStatus == PaymentStatuses.Paid)
            return new("Paid — scheduled. Mark it In Progress when you start the job.", "ok", "check", null);
        return null;
    }
}
