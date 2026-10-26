using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public enum ApprovalOutcome { Success, NotFound, NotPending, Invalid, NotAllowed }

public sealed record ApprovalResult(ApprovalOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == ApprovalOutcome.Success;
}

public sealed record ApplicationLookup(ApprovalOutcome Outcome, ProviderApplicationViewModel? Application);

public interface IProviderApprovalService
{
    /// The application shown in the review dialog.
    /// Outcome NotAllowed = the signed-in user is not an Active administrator; NotFound = no such provider.
    Task<ApplicationLookup> GetApplicationAsync(int adminUserId, int providerId, CancellationToken ct = default);

    /// Approve or reject a Pending application (same rules as usp_ApproveProvider in the PDF).
    Task<ApprovalResult> DecideAsync(int adminUserId, int providerId, ApprovalDecision decision, string? reason, CancellationToken ct = default);
}

public class ProviderApprovalService : IProviderApprovalService
{
    public const int MaxReasonLength = 255;
    private const int ManilaOffsetHours = 8;

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProviderApprovalService> _logger;

    public ProviderApprovalService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<ProviderApprovalService>? logger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderApprovalService>.Instance;
    }

    // ------------------------------------------------------------------ read

    public async Task<ApplicationLookup> GetApplicationAsync(int adminUserId, int providerId, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return new(ApprovalOutcome.NotAllowed, null);

        var row = await _db.Providers.AsNoTracking()
            .Where(p => p.ProviderId == providerId)
            .Select(p => new
            {
                p.ProviderId, p.BusinessName, p.Description, p.Address, p.City, p.BaseFee,
                p.ApprovalStatus, p.DateApplied,
                First = p.User.FirstName, Last = p.User.LastName,
                Mail = p.User.Email, Phone = p.User.PhoneNumber, Account = p.User.AccountStatus
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return new(ApprovalOutcome.NotFound, null);

        // "Services offered": the categories the provider is in; until they exist, the names of their active services.
        var categories = await _db.ProviderCategories.AsNoTracking()
            .Where(pc => pc.ProviderId == providerId)
            .OrderBy(pc => pc.ProviderCategoryId)
            .Select(pc => pc.Category.CategoryName)
            .ToListAsync(ct);
        var services = categories;
        if (services.Count == 0)
        {
            services = await _db.Services.AsNoTracking()
                .Where(s => s.ProviderId == providerId && s.IsActive)
                .OrderBy(s => s.ServiceId)
                .Select(s => s.ServiceName)
                .ToListAsync(ct);
        }

        var name = $"{row.First} {row.Last}".Trim();
        var location = string.Join(", ", new[] { row.Address, row.City }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim()));

        return new(ApprovalOutcome.Success, new ProviderApplicationViewModel
        {
            Shell = shell,
            ProviderId = row.ProviderId,
            ApplicantNumber = ApplicantNumber.For(row.ProviderId, row.DateApplied),
            Name = name,
            Initials = NameFormat.Initials(name),
            BusinessName = row.BusinessName,
            Location = location.Length > 0 ? location : null,
            Services = services,
            Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
            BaseFee = row.BaseFee,
            Email = row.Mail,
            Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
            AppliedText = row.DateApplied.AddHours(ManilaOffsetHours).ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
            ApprovalStatus = row.ApprovalStatus,
            AccountStatus = row.Account
        });
    }

    // ----------------------------------------------------------------- write

    public async Task<ApprovalResult> DecideAsync(int adminUserId, int providerId, ApprovalDecision decision, string? reason, CancellationToken ct = default)
    {
        // 1. Is the signed-in user really an ACTIVE administrator? (the browser can't claim this)
        var isAdmin = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == adminUserId
                                                               && u.Role == Roles.Administrator
                                                               && u.AccountStatus == AccountStatuses.Active, ct);
        if (!isAdmin)
            return new(ApprovalOutcome.NotAllowed, "Your administrator account is not allowed to do this.");

        // 2. Is the request itself sensible?
        if (!Enum.IsDefined(decision))
            return new(ApprovalOutcome.Invalid, "That action is not recognised.");

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is { Length: > MaxReasonLength })
            return new(ApprovalOutcome.Invalid, $"The note can be at most {MaxReasonLength} characters.");

        // 3. Does the application exist, and is it still waiting?
        var info = await _db.Providers.AsNoTracking()
            .Where(p => p.ProviderId == providerId)
            .Select(p => new { p.ApprovalStatus, p.UserId, First = p.User.FirstName, Last = p.User.LastName })
            .FirstOrDefaultAsync(ct);
        if (info is null)
            return new(ApprovalOutcome.NotFound, "That application no longer exists.");

        var name = $"{info.First} {info.Last}".Trim();
        if (info.ApprovalStatus != ApprovalStatuses.Pending)
            return AlreadyDecided(name);

        // 4. Make the change. Same rules as usp_ApproveProvider in the PDF:
        //    status + DATE_APPROVED (approval only), a log entry, and a notification for the provider.
        var approve = decision == ApprovalDecision.Approve;
        var newStatus = approve ? ApprovalStatuses.Approved : ApprovalStatuses.Rejected;
        DateTime? approvedAt = approve ? _clock.GetUtcNow().UtcDateTime : null;

        // On SQL Server everything below runs in ONE database transaction, and the status is changed with a
        // single "UPDATE ... WHERE APPROVAL_STATUS = 'Pending'". If two administrators click at the same moment,
        // only one UPDATE finds a Pending row; the other changes nothing and is told so (no double log, no double notification).
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(ct) : null;

        int changed;
        if (relational)
        {
            changed = await _db.Providers
                .Where(p => p.ProviderId == providerId && p.ApprovalStatus == ApprovalStatuses.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.ApprovalStatus, newStatus)
                    .SetProperty(p => p.DateApproved, approvedAt), ct);
        }
        else
        {
            // The in-memory database used by the unit tests has no ExecuteUpdate or transactions.
            var provider = await _db.Providers.FirstAsync(p => p.ProviderId == providerId, ct);
            provider.ApprovalStatus = newStatus;
            provider.DateApproved = approvedAt;
            changed = 1;
        }

        if (changed == 0)
            return AlreadyDecided(name);          // the transaction is rolled back when it is disposed

        _db.AdminActions.Add(new AdminAction
        {
            AdminUserId = adminUserId,
            ActionType = newStatus,               // 'Approved' / 'Rejected' (as in the PDF)
            TargetType = "SERVICE_PROVIDER",
            TargetId = providerId,
            Reason = cleanReason
        });
        _db.Notifications.Add(new Notification
        {
            UserId = info.UserId,
            Message = $"Your provider application was {newStatus}."
        });
        await _db.SaveChangesAsync(ct);

        if (tx is not null) await tx.CommitAsync(ct);

        _logger.LogInformation("Admin {AdminUserId} set provider application {ProviderId} to {Status}.",
            adminUserId, providerId, newStatus);

        return new(ApprovalOutcome.Success, approve
            ? $"{name} has been approved as an active service provider."
            : $"{name}'s application has been rejected.");
    }

    private static ApprovalResult AlreadyDecided(string name) =>
        new(ApprovalOutcome.NotPending, $"{name}'s application has already been decided, so nothing was changed.");
}
