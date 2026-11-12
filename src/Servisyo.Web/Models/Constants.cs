namespace Servisyo.Web.Models;

public static class Roles
{
    public const string Customer = "Customer";
    public const string ServiceProvider = "Service Provider";
    public const string Administrator = "Administrator";
}

public static class AccountStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Deactivated = "Deactivated";
}

public static class ApprovalStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class BookingStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string InProgress = "In Progress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class PaymentStatuses
{
    public const string Unpaid = "Unpaid";
    public const string Paid = "Paid";
    public const string Refunded = "Refunded";
}

/// A provider's day-off request (database v2.7, PROVIDER_DAY_OFF_REQUESTS.STATUS). Pending holds its dates (no other
/// provider can ask for them) but does not stop bookings; Approved blocks bookings on its dates; Rejected gives the
/// dates back (its days are deleted, the request stays as the record of the decision).
public static class DayOffStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// An approved provider's request to change the name on their account (database v2.8,
/// PROVIDER_NAME_CHANGE_REQUESTS.STATUS). Pending changes nothing yet; Approved copies the new name to USERS;
/// Rejected keeps the old name. Only one request per provider can be Pending at a time.
public static class NameChangeStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static readonly IReadOnlyList<string> All = new[] { Pending, Approved, Rejected };
}

public static class ReviewStatuses
{
    public const string Visible = "Visible";
    public const string Flagged = "Flagged";
    public const string Removed = "Removed";
}

/// The limit of a provider's "Maximum jobs per day" (database v2.6). The same number is in the provider registration
/// form, the Availability page, the database CHECK CK_PROVIDERS_LIMITS (ServisyoDbContext.ProviderLimitsCheck) and
/// usp_RegisterProvider (MaxJobsSql) - the tests check that they all agree.
public static class ProviderLimits
{
    public const int MaxJobsPerDay = 20;
    public const string MaxJobsMessage = "Enter a whole number of jobs from 1 to 20.";
}
