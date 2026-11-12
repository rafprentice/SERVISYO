using System.Globalization;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// Database v2.8: an approved provider's name is locked (ProviderLockRules), so they ASK an administrator to change it.
/// The rules for the new name are the same as at provider registration (AuthService): first, middle and last name,
/// letters only, a middle name of at least 2 characters, at most 100 characters each.
public static class NameChangeRules
{
    public const int MaxNameLength = InputRules.MaxNameLength;      // USERS.FIRST_NAME / MIDDLE_NAME / LAST_NAME (100)
    public const int MaxReasonLength = 255;                         // PROVIDER_NAME_CHANGE_REQUESTS.REASON

    public const string PendingLabel = "Pending Admin Approval";
    public const string ApprovedLabel = "Approved";
    public const string RejectedLabel = "Not approved";

    public const string FirstMissing = "Enter your first name.";
    public const string MiddleMissing = "Enter your middle name.";
    public const string LastMissing = "Enter your last name.";
    public const string TooLong = "A name can be at most 100 characters.";
    public const string SameName = "That is already the name on your account. Change at least one of the names.";
    public const string ReasonMissing = "Tell the administrator why your name should change (for example: married, or a spelling mistake).";
    public const string ReasonTooLong = "The reason can be at most 255 characters.";
    public const string OnePending = "You already have a name change request waiting for an administrator. Cancel it first if you want to send a different one.";
    public const string Sent = "Your name change request was sent. An administrator will review it; your name stays the same until it is approved.";
    public const string Cancelled = "Your name change request was cancelled.";
    public const string NotFound = "That name change request was not found. It may have been answered already.";

    /// "Juan Reyes Santos" (the middle name only when there is one).
    public static string FullName(string? first, string? middle, string? last) =>
        string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);    // the Philippines has no daylight saving

    /// A UTC time shown in Cebu time: "Nov 12, 2026, 9:05 AM".
    public static string When(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(Manila).ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture);

    /// A UTC time shown as a Cebu date: "Nov 12, 2026".
    public static string Date(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(Manila).ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    /// The badge of a request: (text, colour).
    public static (string Label, string Tone) Badge(string status) => status switch
    {
        NameChangeStatuses.Pending => (PendingLabel, "pending"),
        NameChangeStatuses.Approved => (ApprovedLabel, "completed"),
        _ => (RejectedLabel, "danger")
    };
}

/// The "Request Name Change" form. Nothing here says WHO the provider is: that always comes from the sign-in cookie.
public class NameChangeForm
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Reason { get; set; }
}

/// One request as the provider sees it on My Profile.
public sealed record NameChangeLine(int RequestId, string NewName, string Reason, string Status, string Label, string Tone, string WhenText);

/// The "Name on your account" card of My Profile (approved providers only).
public class NameChangeSectionViewModel
{
    public string CurrentName { get; set; } = string.Empty;

    /// The saved name, one box each (the page warns before sending the same name again).
    public string CurrentFirst { get; set; } = string.Empty;
    public string? CurrentMiddle { get; set; }
    public string CurrentLast { get; set; } = string.Empty;

    /// The request that waits for an administrator (only one at a time).
    public NameChangeLine? Pending { get; set; }

    /// The answer to the latest request, when none is waiting.
    public NameChangeLine? LastAnswer { get; set; }

    public bool CanRequest => Pending is null;

    // the pop-up: the current name to start from, or what was typed when the server refused it
    public NameChangeForm Form { get; set; } = new();
    public IReadOnlyDictionary<string, string> FieldErrors { get; set; } = new Dictionary<string, string>();
    public string? FormError { get; set; }
    public bool OpenDialog { get; set; }

    public string? ErrorFor(string field) => FieldErrors.TryGetValue(field, out var message) ? message : null;
}

public enum NameChangeOutcome { Success, NotAllowed, Invalid, NotFound }

public sealed record NameChangeResult(NameChangeOutcome Outcome, string Message, IReadOnlyDictionary<string, string> FieldErrors)
{
    public bool Done => Outcome == NameChangeOutcome.Success;
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();
    public static NameChangeResult Ok(string message) => new(NameChangeOutcome.Success, message, None);
    public static NameChangeResult Refused(NameChangeOutcome outcome, string message) => new(outcome, message, None);
}

// ---------------------------------------------------------------------------------------------- administrator

/// The tabs of Admin > Name Changes.
public static class NameChangeTabs
{
    public const string All = "All";

    /// Unknown or empty = Pending (the requests that need an answer come first).
    public static string Normalize(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "approved" => NameChangeStatuses.Approved,
        "rejected" => NameChangeStatuses.Rejected,
        "all" => All,
        _ => NameChangeStatuses.Pending
    };
}

public class NameChangeRowViewModel
{
    public int RequestId { get; set; }
    public int ProviderId { get; set; }
    public string CurrentName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? AccountNote { get; set; }               // "Suspended" / "Deactivated"
    public string NewName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string RequestedText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusTone { get; set; } = string.Empty;
    public string? DecidedText { get; set; }               // "Approved by Maria on Nov 12, 2026"
    public bool CanDecide => Status == NameChangeStatuses.Pending;
}

public class NameChangesPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };
    public string Status { get; set; } = NameChangeStatuses.Pending;
    public int CountPending { get; set; }
    public int CountApproved { get; set; }
    public int CountRejected { get; set; }
    public int CountAll => CountPending + CountApproved + CountRejected;
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public IReadOnlyList<NameChangeRowViewModel> Rows { get; set; } = Array.Empty<NameChangeRowViewModel>();
}

/// "Approve" / "Reject" on Admin > Name Changes. The administrator comes from the sign-in cookie; the request is in the address.
public class NameChangeDecisionForm
{
    public string? Decision { get; set; }
    public string? ReturnStatus { get; set; }
    public int ReturnPage { get; set; } = 1;
}

public enum NameChangeDecisionOutcome { Success, NotAllowed, NotFound, Invalid }

public sealed record NameChangeDecisionResult(NameChangeDecisionOutcome Outcome, string Message);
