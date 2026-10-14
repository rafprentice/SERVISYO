using System.ComponentModel.DataAnnotations;
using Servisyo.Web.Models;
using Servisyo.Web.Services;

namespace Servisyo.Web.ViewModels;

/// What the page asks for (all optional; anything odd falls back to a safe default).
public class UserListQuery
{
    public string? Q { get; set; }
    public string? Status { get; set; }      // All | Active | Suspended | Deactivated
    public int Page { get; set; } = 1;
}

/// The values posted by the confirmation dialog.
public class ChangeStatusForm
{
    public int UserId { get; set; }
    public string? Action { get; set; }

    [StringLength(255, ErrorMessage = "The reason can be at most 255 characters.")]
    public string? Reason { get; set; }

    // remembered so the admin comes back to the same filtered list
    public string? Q { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
}

/// Button text, colour and the confirmation wording for each action.
public sealed record UserActionInfo(string Action, string Label, string Tone, bool AsksReason, string Title, string Text)
{
    public static UserActionInfo For(AccountAction action) => action switch
    {
        AccountAction.Suspend => new(nameof(AccountAction.Suspend), "Suspend", "warn", true,
            "Suspend this account?",
            "They will not be able to sign in or use their account until you reactivate it."),
        AccountAction.Deactivate => new(nameof(AccountAction.Deactivate), "Deactivate", "danger", true,
            "Deactivate this account?",
            "The account will be closed and they will not be able to sign in. You can restore it later."),
        AccountAction.Reactivate => new(nameof(AccountAction.Reactivate), "Reactivate", "ok", false,
            "Reactivate this account?",
            "They will be able to sign in and use their account again."),
        _ => new(nameof(AccountAction.Restore), "Restore", "ok", false,
            "Restore this account?",
            "They will be able to sign in and use their account again.")
    };
}

public class UserRowViewModel
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? City { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusTone => Status switch
    {
        AccountStatuses.Active => "completed",     // green
        AccountStatuses.Suspended => "pending",    // yellow
        AccountStatuses.Deactivated => "danger",   // red
        _ => "muted"
    };
    public IReadOnlyList<UserActionInfo> Actions { get; set; } = Array.Empty<UserActionInfo>();
}

public class UsersPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };

    public string Q { get; set; } = string.Empty;
    public string Status { get; set; } = "All";        // normalised: All | Active | Suspended | Deactivated

    public int CountAll { get; set; }
    public int CountActive { get; set; }
    public int CountSuspended { get; set; }
    public int CountDeactivated { get; set; }

    public int TotalMatching { get; set; }
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public IReadOnlyList<UserRowViewModel> Rows { get; set; } = Array.Empty<UserRowViewModel>();

    // one-time message after an action (set by the controller)
    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }
}

public enum ChangeStatusOutcome { Success, NotAllowed, NotFound, InvalidTransition, ReasonTooLong }

public sealed record ChangeStatusResult(ChangeStatusOutcome Outcome, string Message);
