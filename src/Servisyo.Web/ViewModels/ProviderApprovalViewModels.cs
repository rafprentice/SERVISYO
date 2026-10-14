using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// What the administrator can decide about a provider application.
public enum ApprovalDecision { Approve, Reject }

/// Which page the administrator was on when they opened the review dialog, so we can send them back there.
/// Only these two fixed values are ever accepted from the browser (no free-text redirects).
public static class ApplicationReturn
{
    public const string Dashboard = "dashboard";
    public const string Providers = "providers";

    public static string Normalize(string? key) =>
        string.Equals(key?.Trim(), Providers, StringComparison.OrdinalIgnoreCase) ? Providers : Dashboard;
}

public static class ApplicantNumber
{
    private const int ManilaOffsetHours = 8;

    /// "PRV-2026-001": the year the provider applied (Manila time) and the provider's id.
    /// It is built from real data each time, so nothing extra is stored in the database.
    public static string For(int providerId, DateTime appliedAtUtc) =>
        $"PRV-{appliedAtUtc.AddHours(ManilaOffsetHours).Year}-{providerId:000}";
}

/// Everything the "Review Provider Application" dialog shows.
public class ProviderApplicationViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };

    public int ProviderId { get; set; }
    public string ApplicantNumber { get; set; } = string.Empty;      // PRV-2026-001
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? Location { get; set; }                            // "Cebu City" or "Street, Cebu City"
    public IReadOnlyList<string> Services { get; set; } = Array.Empty<string>();
    public string? Description { get; set; }
    public decimal? BaseFee { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string AppliedText { get; set; } = string.Empty;          // "Oct 14, 2026"
    public string ApprovalStatus { get; set; } = ApprovalStatuses.Pending;
    public string AccountStatus { get; set; } = AccountStatuses.Active;

    // where to go back to (filled in by the controller from the query string)
    public string ReturnTo { get; set; } = ApplicationReturn.Dashboard;
    public string ReturnStatus { get; set; } = ProviderStatuses.All;
    public int ReturnPage { get; set; } = 1;

    /// Only a Pending application can be approved or rejected.
    public bool CanDecide => ApprovalStatus == ApprovalStatuses.Pending;

    public string BaseFeeText => BaseFee is null
        ? string.Empty
        : "₱" + BaseFee.Value.ToString("N2", CultureInfo.InvariantCulture);
}

/// The values posted by the dialog. The provider id comes from the URL, the administrator from the sign-in cookie.
public class ApplicationDecisionForm
{
    [Required]
    public string Decision { get; set; } = string.Empty;           // Approve | Reject

    [StringLength(255)]
    public string? Reason { get; set; }                            // optional note for the admin log

    public string? ReturnTo { get; set; }
    public string? ReturnStatus { get; set; }
    public int ReturnPage { get; set; } = 1;
}
