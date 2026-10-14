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

public static class ReviewStatuses
{
    public const string Visible = "Visible";
    public const string Flagged = "Flagged";
    public const string Removed = "Removed";
}

public static class ReportStatuses
{
    public const string Open = "Open";
    public const string UnderReview = "Under Review";
    public const string Resolved = "Resolved";
}
