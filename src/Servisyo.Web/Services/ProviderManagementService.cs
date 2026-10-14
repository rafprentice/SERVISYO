using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IProviderManagementService
{
    /// The provider cards for the admin Providers page. Returns null when the signed-in user is not an Active administrator.
    Task<ProvidersPageViewModel?> GetProvidersAsync(int adminUserId, ProviderListQuery query, CancellationToken ct = default);
}

public class ProviderManagementService : IProviderManagementService
{
    public const int PageSize = 12;      // 4 rows of 3 cards

    private const int TagsShown = 2;

    private readonly ServisyoDbContext _db;

    public ProviderManagementService(ServisyoDbContext db) => _db = db;

    public async Task<ProvidersPageViewModel?> GetProvidersAsync(int adminUserId, ProviderListQuery query, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return null;

        var all = _db.Providers.AsNoTracking();

        // ---- Tab numbers (one provider = one tab, so they add up to "All") ----
        var groups = await all
            .GroupBy(p => new { p.ApprovalStatus, Account = p.User.AccountStatus })
            .Select(g => new { g.Key.ApprovalStatus, g.Key.Account, Count = g.Count() })
            .ToListAsync(ct);
        int CountOf(string key) =>
            groups.Where(g => ProviderStatuses.Primary(g.ApprovalStatus, g.Account) == key).Sum(g => g.Count);

        var status = ProviderStatuses.Normalize(query.Status);
        var filtered = ApplyFilter(all, status);

        var matching = await filtered.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize));
        var page = Math.Clamp(query.Page, 1, pageCount);

        // ---- This page's providers (oldest application first) ----
        var rows = await filtered
            .OrderBy(p => p.DateApplied).ThenBy(p => p.ProviderId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(p => new
            {
                p.ProviderId, p.BusinessName, p.City, p.ApprovalStatus, p.AvgRating, p.ReviewCount,
                First = p.User.FirstName, Last = p.User.LastName, Account = p.User.AccountStatus
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.ProviderId).ToList();

        // Services (active ones only) and categories for just these providers.
        var services = await _db.Services.AsNoTracking()
            .Where(s => ids.Contains(s.ProviderId) && s.IsActive)
            .OrderBy(s => s.ServiceId)
            .Select(s => new { s.ProviderId, s.ServiceName, Category = s.Category.CategoryName })
            .ToListAsync(ct);
        var declared = await _db.ProviderCategories.AsNoTracking()
            .Where(pc => ids.Contains(pc.ProviderId))
            .OrderBy(pc => pc.ProviderCategoryId)
            .Select(pc => new { pc.ProviderId, Category = pc.Category.CategoryName })
            .ToListAsync(ct);

        var cards = rows.Select(r =>
        {
            var name = $"{r.First} {r.Last}".Trim();
            var myServices = services.Where(s => s.ProviderId == r.ProviderId).ToList();

            // Category: what the provider declared, otherwise the category of their first service.
            var category = declared.Where(d => d.ProviderId == r.ProviderId).Select(d => d.Category).FirstOrDefault()
                           ?? myServices.Select(s => s.Category).FirstOrDefault();
            var where = new[] { category, r.City }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            var badges = new List<ProviderBadge> { ApprovalBadge(r.ApprovalStatus) };
            if (r.Account == AccountStatuses.Suspended) badges.Add(new("Suspended", "danger"));
            else if (r.Account == AccountStatuses.Deactivated) badges.Add(new("Deactivated", "muted"));

            return new ProviderCardViewModel
            {
                ProviderId = r.ProviderId,
                Name = name,
                Initials = NameFormat.Initials(name),
                BusinessName = r.BusinessName,
                Details = where.Count == 0 ? null : string.Join(" · ", where),
                ApprovalStatus = r.ApprovalStatus,
                Rating = r.ReviewCount > 0 ? r.AvgRating : null,
                ReviewCount = r.ReviewCount,
                Badges = badges,
                ServiceTags = myServices.Take(TagsShown).Select(s => NameFormat.Truncate(s.ServiceName, 24)).ToList(),
                ExtraServices = Math.Max(0, myServices.Count - TagsShown)
            };
        }).ToList();

        return new ProvidersPageViewModel
        {
            Shell = shell,
            Status = status,
            CountAll = groups.Sum(g => g.Count),
            CountPending = CountOf(ApprovalStatuses.Pending),
            CountApproved = CountOf(ApprovalStatuses.Approved),
            CountSuspended = CountOf(AccountStatuses.Suspended),
            CountRejected = CountOf(ApprovalStatuses.Rejected),
            CountDeactivated = CountOf(AccountStatuses.Deactivated),
            TotalMatching = matching,
            Page = page,
            PageCount = pageCount,
            Cards = cards
        };
    }

    /// Same rule as ProviderStatuses.Primary, written so the database can run it.
    private static IQueryable<Provider> ApplyFilter(IQueryable<Provider> providers, string status) => status switch
    {
        ApprovalStatuses.Pending => providers.Where(p => p.User.AccountStatus == AccountStatuses.Active && p.ApprovalStatus == ApprovalStatuses.Pending),
        ApprovalStatuses.Approved => providers.Where(p => p.User.AccountStatus == AccountStatuses.Active && p.ApprovalStatus == ApprovalStatuses.Approved),
        ApprovalStatuses.Rejected => providers.Where(p => p.User.AccountStatus == AccountStatuses.Active && p.ApprovalStatus == ApprovalStatuses.Rejected),
        AccountStatuses.Suspended => providers.Where(p => p.User.AccountStatus == AccountStatuses.Suspended),
        AccountStatuses.Deactivated => providers.Where(p => p.User.AccountStatus == AccountStatuses.Deactivated),
        _ => providers
    };

    private static ProviderBadge ApprovalBadge(string approvalStatus) => approvalStatus switch
    {
        ApprovalStatuses.Approved => new("Approved", "completed"),   // green
        ApprovalStatuses.Pending => new("Pending", "pending"),       // yellow
        ApprovalStatuses.Rejected => new("Rejected", "danger"),      // red
        _ => new(approvalStatus, "muted")
    };
}
