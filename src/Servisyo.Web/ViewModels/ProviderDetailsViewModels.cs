using System.Globalization;
using System.ComponentModel.DataAnnotations;

namespace Servisyo.Web.ViewModels;

public sealed record ProviderServiceLine(string Name, string Category, string PriceText, string DurationText);

public class ProviderDetailsViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };

    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? Location { get; set; }                       // "Plumbing · Cebu City"
    public string ApprovalStatus { get; set; } = string.Empty;
    public string AccountStatus { get; set; } = string.Empty;
    public IReadOnlyList<ProviderBadge> Badges { get; set; } = Array.Empty<ProviderBadge>();

    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string MemberSince { get; set; } = string.Empty;
    public int CompletedJobs { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public decimal? Rating { get; set; }
    public int ReviewCount { get; set; }

    public IReadOnlyList<ProviderServiceLine> Services { get; set; } = Array.Empty<ProviderServiceLine>();

    /// Only the buttons that make sense for the account's CURRENT status.
    public IReadOnlyList<UserActionInfo> Actions { get; set; } = Array.Empty<UserActionInfo>();

    /// The provider's days off from today to December 31 (their own ones and extra days approved by an administrator).
    public IReadOnlyList<ProviderDayOffLine> DaysOff { get; set; } = Array.Empty<ProviderDayOffLine>();

    /// "Approve an extra day off" is offered for approved providers; the date box allows today to December 31.
    public bool CanApproveDayOff { get; set; }
    public string DayOffMin { get; set; } = string.Empty;    // "2026-11-11"
    public string DayOffMax { get; set; } = string.Empty;    // "2026-12-31"

    // remembered so "Close" and every action come back to the same filtered list
    public string ReturnStatus { get; set; } = ProviderStatuses.All;
    public int ReturnPage { get; set; } = 1;

    public bool AsksReason => Actions.Any(a => a.AsksReason);
    public string RatingText => ReviewCount > 0 && Rating is not null
        ? $"{Rating.Value.ToString("0.#", CultureInfo.InvariantCulture)} ({(ReviewCount == 1 ? "1 review" : $"{ReviewCount} reviews")})"
        : "No reviews yet";
}

public enum ProviderDetailsOutcome { Ok, NotAllowed, NotFound }

public sealed record ProviderDetailsLookup(ProviderDetailsOutcome Outcome, ProviderDetailsViewModel? Details);

/// What the browser posts to change a provider's account status. It says WHAT to do;
/// WHO is doing it comes from the sign-in cookie, and WHICH provider is in the address.
/// One day off in the admin pop-up: "Nov 20, 2026", the reason, and whether an administrator approved it as an extra day.
public sealed record ProviderDayOffLine(string DateText, string? Reason, bool ApprovedByAdmin);

/// "Approve an extra day off" (admin pop-up). The administrator comes from the sign-in cookie; the provider is in the address.
public class ExtraDayOffForm
{
    public DateOnly? Date { get; set; }
    public string? Reason { get; set; }
    public string? ReturnStatus { get; set; }
    public int ReturnPage { get; set; } = 1;
}

public enum ExtraDayOffOutcome { Success, NotAllowed, NotFound, Invalid }

public sealed record ExtraDayOffResult(ExtraDayOffOutcome Outcome, string Message);

public class ProviderStatusForm
{
    public string? Action { get; set; }

    [StringLength(255, ErrorMessage = "The reason can be at most 255 characters.")]
    public string? Reason { get; set; }

    public string? ReturnStatus { get; set; }
    public int ReturnPage { get; set; } = 1;
}
