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

    /// Everything the "View" pop-up shows for ONE customer: contact details, saved address and their recent bookings.
    /// NotAllowed when the signed-in user is not an Active administrator; NotFound for anything that is not a customer.
    Task<CustomerDetailsLookup> GetCustomerDetailsAsync(int adminUserId, int userId, CancellationToken ct = default);

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
                u.UserId, u.FirstName, u.LastName, u.Email, u.AccountStatus
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
                UnreadNotifications = await _db.CountUnreadNotificationsAsync(adminUserId, ct)
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
                    Status = r.AccountStatus,
                    Actions = AccountTransitions.AvailableFor(r.AccountStatus).Select(UserActionInfo.For).ToList()
                };
            }).ToList()
        };
    }

    // ------------------------------------------------------------------ "View" pop-up

    /// How many of the customer's bookings the pop-up lists (newest first); the total is counted separately.
    public const int RecentBookingsShown = 5;
    private const int ManilaOffsetHours = 8;          // the Philippines has no daylight saving

    public async Task<CustomerDetailsLookup> GetCustomerDetailsAsync(int adminUserId, int userId, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return new(CustomerDetailsOutcome.NotAllowed, null);

        // only CUSTOMER accounts are shown here (the user id comes from the address, so it is checked)
        var c = await _db.Customers.AsNoTracking()
            .Where(x => x.UserId == userId && x.User.Role == Roles.Customer)
            .Select(x => new
            {
                x.CustomerId, x.Address, x.User.FirstName, x.User.LastName, x.User.Email, x.User.PhoneNumber,
                x.User.AccountStatus, x.User.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
        if (c is null) return new(CustomerDetailsOutcome.NotFound, null);

        var total = await _db.Bookings.AsNoTracking().CountAsync(b => b.CustomerId == c.CustomerId, ct);
        var recent = await _db.Bookings.AsNoTracking()
            .Where(b => b.CustomerId == c.CustomerId)
            .OrderByDescending(b => b.BookingDate).ThenByDescending(b => b.BookingTime).ThenByDescending(b => b.BookingId)
            .Take(RecentBookingsShown)
            .Select(b => new
            {
                b.BookingId, b.BookingDate, b.BookingTime, b.TotalFee, b.Status, Provider = b.Provider.BusinessName,
                Services = b.Services.OrderBy(s => s.BookingServiceId).Select(s => s.Service.ServiceName).ToList()
            })
            .ToListAsync(ct);

        var name = $"{c.FirstName} {c.LastName}".Trim();
        return new(CustomerDetailsOutcome.Ok, new CustomerDetailsViewModel
        {
            Shell = shell,
            UserId = userId,
            Name = name,
            Initials = NameFormat.Initials(name),
            Status = c.AccountStatus,
            Email = c.Email,
            Phone = string.IsNullOrWhiteSpace(c.PhoneNumber) ? null : c.PhoneNumber.Trim(),
            Address = string.IsNullOrWhiteSpace(c.Address) ? null : c.Address.Trim(),
            MemberSince = c.CreatedAt.AddHours(ManilaOffsetHours).ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture),
            TotalBookings = total,
            RecentBookings = recent.Select(b => new CustomerBookingLine(
                b.BookingId,
                b.Services.Count == 0 ? "Service" : b.Services.Count == 1 ? b.Services[0] : $"{b.Services[0]} +{b.Services.Count - 1} more",
                b.Provider,
                BookingFormat.DateTimeText(b.BookingDate, b.BookingTime),
                ServiceFormat.Peso(b.TotalFee),
                b.Status,
                ProviderBookingBadge.Tone(b.Status))).ToList()
        });
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

        // 2. The target must be a CUSTOMER account (never an administrator or a provider from this page),
        //    with its CUSTOMERS row: the log points to that row (ADMIN_ACTIONS.TARGET_CUSTOMER_ID).
        var target = await _db.Users.Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.UserId == targetUserId && u.Role == Roles.Customer, ct);
        if (target?.Customer is null)
            return new(ChangeStatusOutcome.NotFound, "That customer account was not found.");

        var name = $"{target.FirstName} {target.LastName}".Trim();

        // 3. The change must be allowed from the account's CURRENT status.
        var newStatus = AccountTransitions.TargetStatus(target.AccountStatus, action);
        if (newStatus is null)
            return new(ChangeStatusOutcome.InvalidTransition,
                $"{name}'s account is currently {target.AccountStatus.ToLowerInvariant()}, so that action isn't available. The list shows the latest status.");

        // 4. Save the new status and the log entry together (one SaveChanges = one database transaction).
        //    Same log as usp_UpdateAccountStatus: 'Account <status>', target = the CUSTOMER (database v2.2).
        target.AccountStatus = newStatus;
        target.Customer.AccountStatus = newStatus;     // the copy on CUSTOMERS
        _db.AdminActions.Add(AdminAction.ForCustomer(adminUserId, "Account " + newStatus, target.Customer.CustomerId, cleanReason));
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
