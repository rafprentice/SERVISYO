using Servisyo.Web.Models;

namespace Servisyo.Web.Data;

public static class ProviderQueryExtensions
{
    /// The single rule for "who may be shown to customers": an Approved provider
    /// whose user account is Active. Used by the home page and the dashboards.
    public static IQueryable<Provider> WhereEligible(this IQueryable<Provider> providers) =>
        providers.Where(p => p.ApprovalStatus == ApprovalStatuses.Approved
                          && p.User.AccountStatus == AccountStatuses.Active);
}
