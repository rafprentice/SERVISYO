using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IUserManagementService
{
    /// The customer list for the admin Users page. Returns null when the signed-in user is not an Active administrator.
    Task<UsersPageViewModel?> GetCustomersAsync(int adminUserId, UserListQuery query, CancellationToken ct = default);

    /// Suspend / deactivate / reactivate / restore ONE customer account and record it in ADMIN_ACTIONS.
    Task<ChangeStatusResult> ChangeStatusAsync(int adminUserId, int targetUserId, AccountAction action,
                                               string? reason, CancellationToken ct = default);
}

public class UserManagementService : IUserManagementService
{
    public const int PageSize = 20;
    private const int MaxReasonLength = 255;     // ADMIN_ACTIONS.REASON is NVARCHAR(255)
    private const int MaxSearchLength = 100;

    private readonly ServisyoDbContext _db;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(ServisyoDbContext db, ILogger<UserManagementService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ------------------------------------------------------------------ list
    public async Task<UsersPageViewModel?> GetCustomersAsync(int adminUserId, UserListQuery query, CancellationToken ct = default)
    {
        var admin = await FindActiveAdminAsync(adminUserId, ct);
        if (admin is null) return null;

        // This page manages CUSTOMER accounts only (providers get their own page later).
        var customers = _db.Users.AsNoTracking().Where(u => u.Role == Roles.Customer);

        var counts = await customers.GroupBy(u => u.AccountStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int CountOf(string s) => counts.Where(c => c.Status == s).Select(c => c.Count).FirstOrDefault();

        var status = NormalizeStatus(query.Status);
        var search = (query.Q ?? string.Empty).Trim();
        if (search.Length > MaxSearchLength) search = search[..MaxSearchLength];

        var filtered = customers;
        if (status != "All")
            filtered = filtered.Where(u => u.AccountStatus == status);
        if (search.Length > 0)
        {
            var term = search.ToLowerInvariant();
            filtered = filtered.Where(u => (u.FirstName + " " + u.LastName).ToLower().Contains(term)
                                        || u.Email.ToLower().Contains(term));
        }

        var matching = await filtered.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize));
        var page = Math.Clamp(query.Page, 1, pageCount);

        var rows = await filtered
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.UserId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(u => new
            {
                u.UserId, u.FirstName, u.LastName, u.Email, u.AccountStatus,
                City = u.Customer != null ? u.Customer.City : null
            })
            .ToListAsync(ct);

        return new UsersPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Admin",
                FirstName = admin.FirstName,
                FullName = admin.FirstName,
                Initials = NameFormat.Initials(admin.FirstName),
                Subtitle = "System Administrator",
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == adminUserId && !n.IsRead, ct)
            },
            Q = search,
            Status = status,
            CountActive = CountOf(AccountStatuses.Active),
            CountSuspended = CountOf(AccountStatuses.Suspended),
            CountDeactivated = CountOf(AccountStatuses.Deactivated),
            CountAll = counts.Sum(c => c.Count),
            TotalMatching = matching,
            Page = page,
            PageCount = pageCount,
            Rows = rows.Select(r =>
            {
                var name = $"{r.FirstName} {r.LastName}".Trim();
                return new UserRowViewModel
                {
                    UserId = r.UserId,
                    Name = name,
                    Initials = NameFormat.Initials(name),
                    Email = r.Email,
                    City = string.IsNullOrWhiteSpace(r.City) ? null : r.City,
                    Status = r.AccountStatus,
                    Actions = AccountTransitions.AvailableFor(r.AccountStatus).Select(UserActionInfo.For).ToList()
                };
            }).ToList()
        };
    }

    // ---------------------------------------------------------------- change
    public async Task<ChangeStatusResult> ChangeStatusAsync(
        int adminUserId, int targetUserId, AccountAction action, string? reason, CancellationToken ct = default)
    {
        // 1. The actor must be an Active administrator (checked in the database, not trusted from the browser).
        var admin = await FindActiveAdminAsync(adminUserId, ct);
        if (admin is null)
        {
            _logger.LogWarning("Account status change refused: user {UserId} is not an active administrator.", adminUserId);
            return new(ChangeStatusOutcome.NotAllowed, "You don't have permission to do that.");
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is { Length: > MaxReasonLength })
            return new(ChangeStatusOutcome.ReasonTooLong, $"The reason can be at most {MaxReasonLength} characters.");

        // 2. The target must be a CUSTOMER account (never an administrator or a provider from this page).
        var target = await _db.Users.FirstOrDefaultAsync(u => u.UserId == targetUserId && u.Role == Roles.Customer, ct);
        if (target is null)
            return new(ChangeStatusOutcome.NotFound, "That customer account was not found.");

        var name = $"{target.FirstName} {target.LastName}".Trim();

        // 3. The change must be allowed from the account's CURRENT status.
        var newStatus = AccountTransitions.TargetStatus(target.AccountStatus, action);
        if (newStatus is null)
            return new(ChangeStatusOutcome.InvalidTransition,
                $"{name}'s account is currently {target.AccountStatus.ToLowerInvariant()}, so that action isn't available. The list shows the latest status.");

        // 4. Save the new status and the log entry together (one SaveChanges = one database transaction).
        //    Same log format as usp_UpdateAccountStatus in the PDF: 'Account <status>', 'USER'.
        target.AccountStatus = newStatus;
        _db.AdminActions.Add(new AdminAction
        {
            AdminUserId = adminUserId,
            ActionType = "Account " + newStatus,
            TargetType = "USER",
            TargetId = target.UserId,
            Reason = cleanReason
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin {AdminUserId} changed account {TargetUserId} to {NewStatus}.",
            adminUserId, target.UserId, newStatus);

        var done = action switch
        {
            AccountAction.Suspend => "suspended",
            AccountAction.Deactivate => "deactivated",
            AccountAction.Reactivate => "reactivated",
            _ => "restored"
        };
        return new(ChangeStatusOutcome.Success, $"{name} was {done}.");
    }

    // --------------------------------------------------------------- helpers
    private Task<AdminInfo?> FindActiveAdminAsync(int userId, CancellationToken ct) =>
        _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active)
            .Select(u => new AdminInfo(u.FirstName))
            .FirstOrDefaultAsync(ct);

    private sealed record AdminInfo(string FirstName);

    private static string NormalizeStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "active" => AccountStatuses.Active,
        "suspended" => AccountStatuses.Suspended,
        "deactivated" => AccountStatuses.Deactivated,
        _ => "All"
    };
}
