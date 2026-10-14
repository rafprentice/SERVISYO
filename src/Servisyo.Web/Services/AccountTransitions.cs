using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

public enum AccountAction { Suspend, Deactivate, Reactivate, Restore }

/// The ONLY place that says which account-status changes are allowed.
///   Active      -> Suspended | Deactivated
///   Suspended   -> Active (Reactivate) | Deactivated
///   Deactivated -> Active (Restore)
public static class AccountTransitions
{
    /// The status the account would have after the action, or null when the action is not allowed.
    public static string? TargetStatus(string current, AccountAction action) => (current, action) switch
    {
        (AccountStatuses.Active, AccountAction.Suspend) => AccountStatuses.Suspended,
        (AccountStatuses.Active, AccountAction.Deactivate) => AccountStatuses.Deactivated,
        (AccountStatuses.Suspended, AccountAction.Reactivate) => AccountStatuses.Active,
        (AccountStatuses.Suspended, AccountAction.Deactivate) => AccountStatuses.Deactivated,
        (AccountStatuses.Deactivated, AccountAction.Restore) => AccountStatuses.Active,
        _ => null
    };

    /// Which buttons to offer for an account that currently has this status.
    public static IReadOnlyList<AccountAction> AvailableFor(string status) => status switch
    {
        AccountStatuses.Active => new[] { AccountAction.Suspend, AccountAction.Deactivate },
        AccountStatuses.Suspended => new[] { AccountAction.Reactivate, AccountAction.Deactivate },
        AccountStatuses.Deactivated => new[] { AccountAction.Restore },
        _ => Array.Empty<AccountAction>()
    };
}
