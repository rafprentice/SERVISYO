using System.Globalization;
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

    /// Everything the "View Details" pop-up shows. NotAllowed when the signed-in user is not an Active administrator.
    Task<ProviderDetailsLookup> GetDetailsAsync(int adminUserId, int providerId, CancellationToken ct = default);

    /// Suspend / deactivate / reactivate / restore a provider's ACCOUNT, and write the admin log entry in the same save.
    Task<ChangeStatusResult> ChangeStatusAsync(int adminUserId, int providerId, AccountAction action, string? reason, CancellationToken ct = default);
}

public class ProviderManagementService : IProviderManagementService
{
    public const int PageSize = 12;      // 4 rows of 3 cards

    private const int TagsShown = 2;

    public const int MaxReasonLength = 255;

    private const int ManilaOffsetHours = 8;          // the Philippines has no daylight saving

    private readonly ServisyoDbContext _db;
    private readonly ILogger<ProviderManagementService> _logger;

    public ProviderManagementService(ServisyoDbContext db, ILogger<ProviderManagementService>? logger = null)
    {
        _db = db;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderManagementService>.Instance;
    }

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


    // ------------------------------------------------------------------ details pop-up

    public async Task<ProviderDetailsLookup> GetDetailsAsync(int adminUserId, int providerId, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return new(ProviderDetailsOutcome.NotAllowed, null);

        var p = await _db.Providers.AsNoTracking()
            .Where(x => x.ProviderId == providerId && x.User.Role == Roles.ServiceProvider)
            .Select(x => new
            {
                x.ProviderId, x.BusinessName, x.Description, x.Address, x.City, x.ApprovalStatus, x.AvgRating, x.ReviewCount,
                First = x.User.FirstName, Last = x.User.LastName, x.User.Email, x.User.PhoneNumber,
                Account = x.User.AccountStatus, Joined = x.User.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
        if (p is null) return new(ProviderDetailsOutcome.NotFound, null);

        var services = await _db.Services.AsNoTracking()
            .Where(s => s.ProviderId == providerId && s.IsActive)
            .OrderBy(s => s.ServiceId)
            .Select(s => new { s.ServiceName, Category = s.Category.CategoryName, s.Price, s.DurationMinutes })
            .ToListAsync(ct);

        var declared = await _db.ProviderCategories.AsNoTracking()
            .Where(pc => pc.ProviderId == providerId)
            .OrderBy(pc => pc.ProviderCategoryId)
            .Select(pc => pc.Category.CategoryName)
            .FirstOrDefaultAsync(ct);

        var completed = await _db.Bookings.AsNoTracking()
            .CountAsync(b => b.ProviderId == providerId && b.Status == BookingStatuses.Completed, ct);

        var name = $"{p.First} {p.Last}".Trim();
        var category = declared ?? services.Select(s => s.Category).FirstOrDefault();
        var where = new[] { category, p.City }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        var badges = new List<ProviderBadge> { ApprovalBadge(p.ApprovalStatus) };
        if (p.Account == AccountStatuses.Suspended) badges.Add(new("Suspended", "danger"));
        else if (p.Account == AccountStatuses.Deactivated) badges.Add(new("Deactivated", "muted"));

        return new(ProviderDetailsOutcome.Ok, new ProviderDetailsViewModel
        {
            Shell = shell,
            ProviderId = p.ProviderId,
            Name = name,
            Initials = NameFormat.Initials(name),
            BusinessName = p.BusinessName,
            Location = where.Count == 0 ? null : string.Join(" · ", where),
            ApprovalStatus = p.ApprovalStatus,
            AccountStatus = p.Account,
            Badges = badges,
            Email = p.Email,
            Phone = string.IsNullOrWhiteSpace(p.PhoneNumber) ? null : p.PhoneNumber.Trim(),
            MemberSince = p.Joined.AddHours(ManilaOffsetHours).ToString("MMM d, yyyy", CultureInfo.InvariantCulture),   // stored in UTC, shown in Cebu time
            CompletedJobs = completed,
            Address = string.IsNullOrWhiteSpace(p.Address) ? null : p.Address.Trim(),
            Description = string.IsNullOrWhiteSpace(p.Description) ? null : p.Description.Trim(),
            Rating = p.ReviewCount > 0 ? p.AvgRating : null,
            ReviewCount = p.ReviewCount,
            Services = services.Select(s => new ProviderServiceLine(
                s.ServiceName, s.Category, ServiceFormat.Peso(s.Price), ServiceFormat.Duration(s.DurationMinutes))).ToList(),
            Actions = AccountTransitions.AvailableFor(p.Account).Select(UserActionInfo.For).ToList()
        });
    }

    // ------------------------------------------------------------------ suspend / deactivate / ...

    public async Task<ChangeStatusResult> ChangeStatusAsync(
        int adminUserId, int providerId, AccountAction action, string? reason, CancellationToken ct = default)
    {
        // 1. The actor must be an Active administrator (checked in the database, not trusted from the browser).
        var isAdmin = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == adminUserId
            && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active, ct);
        if (!isAdmin)
        {
            _logger.LogWarning("Provider account change refused: user {UserId} is not an active administrator.", adminUserId);
            return new(ChangeStatusOutcome.NotAllowed, "You don't have permission to do that.");
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is { Length: > MaxReasonLength })
            return new(ChangeStatusOutcome.ReasonTooLong, $"The reason can be at most {MaxReasonLength} characters.");

        // 2. The target must be a PROVIDER account (never an administrator or a customer from this page).
        var provider = await _db.Providers.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.ProviderId == providerId && p.User.Role == Roles.ServiceProvider, ct);
        if (provider is null)
            return new(ChangeStatusOutcome.NotFound, "That provider was not found.");

        var user = provider.User;
        var name = $"{user.FirstName} {user.LastName}".Trim();

        // 3. The change must be allowed from the account's CURRENT status.
        var newStatus = AccountTransitions.TargetStatus(user.AccountStatus, action);
        if (newStatus is null)
            return new(ChangeStatusOutcome.InvalidTransition,
                $"{name}'s account is currently {user.AccountStatus.ToLowerInvariant()}, so that action isn't available. The list shows the latest status.");

        // 4. The new status and the log entry are saved together (one SaveChanges = one database transaction).
        user.AccountStatus = newStatus;
        _db.AdminActions.Add(new AdminAction
        {
            AdminUserId = adminUserId,
            ActionType = "Account " + newStatus,
            TargetType = "USER",
            TargetId = user.UserId,
            Reason = cleanReason
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin {AdminUserId} changed provider account {TargetUserId} to {NewStatus}.", adminUserId, user.UserId, newStatus);

        var done = action switch
        {
            AccountAction.Suspend => "suspended",
            AccountAction.Deactivate => "deactivated",
            AccountAction.Reactivate => "reactivated",
            _ => "restored"
        };
        return new(ChangeStatusOutcome.Success, $"{name} was {done}.");
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
