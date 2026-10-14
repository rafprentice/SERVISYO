using System.Globalization;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

public class ProviderListQuery
{
    public string? Status { get; set; }      // All | Pending | Approved | Suspended | Rejected | Deactivated
    public int Page { get; set; } = 1;
}

/// The one "primary" status of a provider, used for the filter tabs.
/// An account that is Suspended/Deactivated counts under that, otherwise the application status
/// (Pending / Approved / Rejected) decides. Every provider is in exactly one tab, so the tab numbers add up.
public static class ProviderStatuses
{
    public const string All = "All";

    public static string Primary(string approvalStatus, string accountStatus) => accountStatus switch
    {
        AccountStatuses.Suspended => AccountStatuses.Suspended,
        AccountStatuses.Deactivated => AccountStatuses.Deactivated,
        _ => approvalStatus      // Pending | Approved | Rejected
    };

    public static string Normalize(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "pending" => ApprovalStatuses.Pending,
        "approved" => ApprovalStatuses.Approved,
        "rejected" => ApprovalStatuses.Rejected,
        "suspended" => AccountStatuses.Suspended,
        "deactivated" => AccountStatuses.Deactivated,
        _ => All
    };
}

public sealed record ProviderBadge(string Text, string Tone);

public class ProviderCardViewModel
{
    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;           // the provider's own name
    public string Initials { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? Details { get; set; }                        // "Plumbing · Cebu City"
    public string ApprovalStatus { get; set; } = string.Empty;
    public decimal? Rating { get; set; }
    public int ReviewCount { get; set; }
    public IReadOnlyList<ProviderBadge> Badges { get; set; } = Array.Empty<ProviderBadge>();
    public IReadOnlyList<string> ServiceTags { get; set; } = Array.Empty<string>();
    public int ExtraServices { get; set; }

    public bool HasRating => Rating is not null && ReviewCount > 0;
    public string RatingText => Rating?.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;   // 4.9 -> "4.9", 5.0 -> "5"
    public string ReviewsText => ReviewCount == 1 ? "1 review" : $"{ReviewCount} reviews";
    public int StarsFilled => Rating is null ? 0
        : Math.Clamp((int)Math.Round((double)Rating.Value, MidpointRounding.AwayFromZero), 0, 5);
}

public class ProvidersPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };
    public string Status { get; set; } = ProviderStatuses.All;   // normalised

    public int CountAll { get; set; }
    public int CountPending { get; set; }
    public int CountApproved { get; set; }
    public int CountSuspended { get; set; }
    public int CountRejected { get; set; }
    public int CountDeactivated { get; set; }

    public int TotalMatching { get; set; }
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public IReadOnlyList<ProviderCardViewModel> Cards { get; set; } = Array.Empty<ProviderCardViewModel>();
}
