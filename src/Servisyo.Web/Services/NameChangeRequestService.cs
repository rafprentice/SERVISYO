using System.Data;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

/// Database v2.8: name change requests. An APPROVED provider cannot edit their name (ProviderLockRules), so they ask;
/// an administrator approves (the new name is copied to USERS) or rejects (nothing changes). Every answer is an
/// ADMIN_ACTIONS row saved in the same transaction, and the request points to it.
public interface INameChangeRequestService
{
    /// The "Name on your account" card of My Profile. Null when the signed-in user is not an Active, Approved provider.
    Task<NameChangeSectionViewModel?> GetSectionAsync(int userId, CancellationToken ct = default);

    /// The signed-in provider asks for a new name. Everything is checked first; nothing is saved when something is wrong.
    Task<NameChangeResult> RequestAsync(int userId, NameChangeForm form, CancellationToken ct = default);

    /// The signed-in provider cancels their own request while it is still Pending.
    Task<NameChangeResult> CancelAsync(int userId, int requestId, CancellationToken ct = default);

    /// Admin > Name Changes. Null when the signed-in user is not an Active administrator.
    Task<NameChangesPageViewModel?> GetListAsync(int adminUserId, string? status, int page, CancellationToken ct = default);

    /// Approve or reject a Pending request (Active administrators only).
    Task<NameChangeDecisionResult> DecideAsync(int adminUserId, int requestId, bool approve, CancellationToken ct = default);
}

public class NameChangeRequestService : INameChangeRequestService
{
    public const int PageSize = 20;

    private readonly ServisyoDbContext _db;
    private readonly ILogger<NameChangeRequestService> _logger;
    private readonly INameChangeLiveUpdates? _live;

    public NameChangeRequestService(ServisyoDbContext db, ILogger<NameChangeRequestService>? logger = null, INameChangeLiveUpdates? live = null)
    {
        _db = db;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NameChangeRequestService>.Instance;
        _live = live;
    }

    private Task PushAsync(int providerId, int providerUserId, string what) =>
        _live?.NameChangeChangedAsync(providerId, providerUserId, what) ?? Task.CompletedTask;

    /// The provider of the SIGNED-IN user, only when the account is Active and the application Approved.
    private Task<Provider?> ApprovedProviderAsync(int userId, bool tracking, CancellationToken ct)
    {
        var query = _db.Providers.Include(p => p.User)
            .Where(p => p.UserId == userId
                     && p.User.Role == Roles.ServiceProvider
                     && p.User.AccountStatus == AccountStatuses.Active
                     && p.ApprovalStatus == ApprovalStatuses.Approved);
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    // ------------------------------------------------------------------ provider: My Profile

    public async Task<NameChangeSectionViewModel?> GetSectionAsync(int userId, CancellationToken ct = default)
    {
        var provider = await ApprovedProviderAsync(userId, tracking: false, ct);
        if (provider is null) return null;

        var latest = await _db.NameChangeRequests.AsNoTracking()
            .Where(r => r.ProviderId == provider.ProviderId)
            .OrderByDescending(r => r.Status == NameChangeStatuses.Pending)          // a waiting request first
            .ThenByDescending(r => r.RequestedAt).ThenByDescending(r => r.RequestId)
            .Select(r => new
            {
                r.RequestId, r.FirstName, r.MiddleName, r.LastName, r.Reason, r.Status, r.RequestedAt,
                DecidedAt = r.DecidedBy != null ? (DateTime?)r.DecidedBy.CreatedAt : null
            })
            .FirstOrDefaultAsync(ct);

        NameChangeLine? line = null;
        if (latest is not null)
        {
            var (label, tone) = NameChangeRules.Badge(latest.Status);
            var when = latest.Status == NameChangeStatuses.Pending || latest.DecidedAt is null
                ? "Sent " + NameChangeRules.When(latest.RequestedAt)
                : (latest.Status == NameChangeStatuses.Approved ? "Approved " : "Answered ") + NameChangeRules.Date(latest.DecidedAt.Value);
            line = new NameChangeLine(latest.RequestId, NameChangeRules.FullName(latest.FirstName, latest.MiddleName, latest.LastName),
                latest.Reason, latest.Status, label, tone, when);
        }

        var user = provider.User;
        return new NameChangeSectionViewModel
        {
            CurrentName = NameChangeRules.FullName(user.FirstName, user.MiddleName, user.LastName),
            CurrentFirst = user.FirstName,
            CurrentMiddle = user.MiddleName,
            CurrentLast = user.LastName,
            Pending = line is { Status: NameChangeStatuses.Pending } ? line : null,
            LastAnswer = line is { Status: not NameChangeStatuses.Pending } ? line : null,
            // the pop-up starts from the current name
            Form = new NameChangeForm { FirstName = user.FirstName, MiddleName = user.MiddleName, LastName = user.LastName }
        };
    }

    public async Task<NameChangeResult> RequestAsync(int userId, NameChangeForm form, CancellationToken ct = default)
    {
        // 1. Only an Active, Approved provider (found from the SIGNED-IN user id, never from the browser).
        var provider = await ApprovedProviderAsync(userId, tracking: false, ct);
        if (provider is null)
        {
            _logger.LogWarning("Name change request refused: user {UserId} is not an active, approved provider.", userId);
            return NameChangeResult.Refused(NameChangeOutcome.NotAllowed, "Only approved service providers can request a name change.");
        }

        // 2. The new name: the same rules as at provider registration (AuthService.CheckCommon + the middle name).
        var first = ProfileRules.NormalizeName(form.FirstName);
        var middle = ProfileRules.NormalizeName(form.MiddleName);
        var last = ProfileRules.NormalizeName(form.LastName);
        var reason = ProfileRules.NormalizeText(form.Reason);

        var errors = new Dictionary<string, string>();
        void CheckName(string field, string value, string missing)
        {
            if (value.Length == 0) errors[field] = missing;
            else if (value.Length > NameChangeRules.MaxNameLength) errors[field] = NameChangeRules.TooLong;
            else if (!InputRules.IsName(value)) errors[field] = InputRules.NameMessage;
        }
        CheckName("FirstName", first, NameChangeRules.FirstMissing);
        CheckName("MiddleName", middle, NameChangeRules.MiddleMissing);
        if (!errors.ContainsKey("MiddleName") && middle.Length < InputRules.MinMiddleNameLength)
            errors["MiddleName"] = InputRules.MiddleNameTooShortMessage;
        CheckName("LastName", last, NameChangeRules.LastMissing);

        if (reason is null) errors["Reason"] = NameChangeRules.ReasonMissing;
        else if (reason.Length > NameChangeRules.MaxReasonLength) errors["Reason"] = NameChangeRules.ReasonTooLong;

        if (errors.Count > 0)
            return new NameChangeResult(NameChangeOutcome.Invalid, "Please fix the highlighted fields.", errors);

        // 3. It must really be a change (a different spelling or capital letter counts).
        var user = provider.User;
        if (first == ProfileRules.NormalizeName(user.FirstName)
            && middle == ProfileRules.NormalizeName(user.MiddleName)
            && last == ProfileRules.NormalizeName(user.LastName))
            return NameChangeResult.Refused(NameChangeOutcome.Invalid, NameChangeRules.SameName);

        // 4. One waiting request at a time: checked and saved as ONE step (serializable); the filtered UNIQUE index
        //    UX_PROVIDER_NAME_CHANGE_REQUESTS_ONE_PENDING refuses a second one even from two clicks at the same moment.
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        if (await _db.NameChangeRequests.AnyAsync(r => r.ProviderId == provider.ProviderId && r.Status == NameChangeStatuses.Pending, ct))
            return NameChangeResult.Refused(NameChangeOutcome.Invalid, NameChangeRules.OnePending);

        _db.NameChangeRequests.Add(new ProviderNameChangeRequest
        {
            ProviderId = provider.ProviderId,
            FirstName = first,
            MiddleName = middle,
            LastName = last,
            Reason = reason!,
            Status = NameChangeStatuses.Pending,
            RequestedAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Name change request of provider {ProviderId} was not saved.", provider.ProviderId);
            return NameChangeResult.Refused(NameChangeOutcome.Invalid, NameChangeRules.OnePending);
        }

        _logger.LogInformation("Provider {ProviderId} requested a name change.", provider.ProviderId);
        await PushAsync(provider.ProviderId, userId, "requested");
        return NameChangeResult.Ok(NameChangeRules.Sent);
    }

    public async Task<NameChangeResult> CancelAsync(int userId, int requestId, CancellationToken ct = default)
    {
        var provider = await ApprovedProviderAsync(userId, tracking: false, ct);
        if (provider is null)
            return NameChangeResult.Refused(NameChangeOutcome.NotAllowed, "Only approved service providers can do that.");

        // only their OWN request, and only while nobody answered it yet
        var request = await _db.NameChangeRequests
            .FirstOrDefaultAsync(r => r.RequestId == requestId && r.ProviderId == provider.ProviderId, ct);
        if (request is null || request.Status != NameChangeStatuses.Pending)
            return NameChangeResult.Refused(NameChangeOutcome.NotFound, NameChangeRules.NotFound);

        _db.NameChangeRequests.Remove(request);       // nothing was decided, so there is nothing to keep
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Name change request {RequestId} was not cancelled.", requestId);
            return NameChangeResult.Refused(NameChangeOutcome.NotFound, NameChangeRules.NotFound);
        }

        _logger.LogInformation("Provider {ProviderId} cancelled name change request {RequestId}.", provider.ProviderId, requestId);
        await PushAsync(provider.ProviderId, userId, "cancelled");
        return NameChangeResult.Ok(NameChangeRules.Cancelled);
    }

    // ------------------------------------------------------------------ administrator

    public async Task<NameChangesPageViewModel?> GetListAsync(int adminUserId, string? status, int page, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return null;

        var counts = await _db.NameChangeRequests.AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int CountOf(string s) => counts.Where(c => c.Status == s).Select(c => c.Count).FirstOrDefault();

        var tab = NameChangeTabs.Normalize(status);
        var filtered = _db.NameChangeRequests.AsNoTracking();
        if (tab != NameChangeTabs.All) filtered = filtered.Where(r => r.Status == tab);

        // waiting requests: oldest first (a fair queue); answered ones: newest first
        filtered = tab == NameChangeStatuses.Pending
            ? filtered.OrderBy(r => r.RequestedAt).ThenBy(r => r.RequestId)
            : filtered.OrderByDescending(r => r.Status == NameChangeStatuses.Pending).ThenByDescending(r => r.RequestedAt).ThenByDescending(r => r.RequestId);

        var matching = await filtered.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize));
        var current = Math.Clamp(page, 1, pageCount);

        var rows = await filtered
            .Skip((current - 1) * PageSize).Take(PageSize)
            .Select(r => new
            {
                r.RequestId, r.ProviderId, r.FirstName, r.MiddleName, r.LastName, r.Reason, r.Status, r.RequestedAt,
                CurFirst = r.Provider.User.FirstName, CurMiddle = r.Provider.User.MiddleName, CurLast = r.Provider.User.LastName,
                r.Provider.BusinessName, Account = r.Provider.User.AccountStatus,
                DecidedAt = r.DecidedBy != null ? (DateTime?)r.DecidedBy.CreatedAt : null,
                DecidedBy = r.DecidedBy != null ? r.DecidedBy.Admin.FirstName : null
            })
            .ToListAsync(ct);

        return new NameChangesPageViewModel
        {
            Shell = shell,
            Status = tab,
            CountPending = CountOf(NameChangeStatuses.Pending),
            CountApproved = CountOf(NameChangeStatuses.Approved),
            CountRejected = CountOf(NameChangeStatuses.Rejected),
            Page = current,
            PageCount = pageCount,
            Rows = rows.Select(r =>
            {
                var currentName = NameChangeRules.FullName(r.CurFirst, r.CurMiddle, r.CurLast);
                var (label, tone) = NameChangeRules.Badge(r.Status);
                return new NameChangeRowViewModel
                {
                    RequestId = r.RequestId,
                    ProviderId = r.ProviderId,
                    CurrentName = currentName,
                    Initials = NameFormat.Initials(currentName),
                    BusinessName = r.BusinessName,
                    AccountNote = r.Account == AccountStatuses.Active ? null : r.Account,
                    NewName = NameChangeRules.FullName(r.FirstName, r.MiddleName, r.LastName),
                    Reason = r.Reason,
                    RequestedText = NameChangeRules.When(r.RequestedAt),
                    Status = r.Status,
                    StatusLabel = r.Status == NameChangeStatuses.Pending ? NameChangeRules.PendingLabel : label,
                    StatusTone = tone,
                    DecidedText = r.DecidedAt is null ? null
                        : $"{(r.Status == NameChangeStatuses.Approved ? "Approved" : "Rejected")} by {r.DecidedBy} on {NameChangeRules.Date(r.DecidedAt.Value)}"
                };
            }).ToList()
        };
    }

    public async Task<NameChangeDecisionResult> DecideAsync(int adminUserId, int requestId, bool approve, CancellationToken ct = default)
    {
        // 1. The actor must be an Active administrator (checked in the database, not trusted from the browser).
        var isAdmin = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == adminUserId
            && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active, ct);
        if (!isAdmin)
        {
            _logger.LogWarning("Name change decision refused: user {UserId} is not an active administrator.", adminUserId);
            return new(NameChangeDecisionOutcome.NotAllowed, "You don't have permission to do that.");
        }

        // Serializable: two administrators answering the same request at the same moment cannot both win.
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        var request = await _db.NameChangeRequests
            .Include(r => r.Provider).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, ct);
        if (request is null)
            return new(NameChangeDecisionOutcome.NotFound, "That name change request was not found. The provider may have cancelled it.");

        var user = request.Provider.User;
        var oldName = NameChangeRules.FullName(user.FirstName, user.MiddleName, user.LastName);
        var newName = NameChangeRules.FullName(request.FirstName, request.MiddleName, request.LastName);
        if (request.Status != NameChangeStatuses.Pending)
            return new(NameChangeDecisionOutcome.Invalid, $"The name change request of {oldName} was already {request.Status.ToLowerInvariant()}. The list shows the latest.");

        // 2. The admin log entry (REASON = "old name to new name") and the answer that points to it: one SaveChanges.
        var adminLog = AdminAction.ForProvider(adminUserId,
            approve ? NameChangeRequestsSql.ApprovedActionType : NameChangeRequestsSql.RejectedActionType,
            request.ProviderId, NameFormat.Truncate($"{oldName} to {newName}", 255));
        adminLog.CreatedAt = DateTime.UtcNow;
        _db.AdminActions.Add(adminLog);
        request.Status = approve ? NameChangeStatuses.Approved : NameChangeStatuses.Rejected;
        request.DecidedBy = adminLog;
        if (approve)
        {
            // the name lives in ONE place (USERS); the USERS audit trigger keeps the old one in SYSTEM_LOGS
            user.FirstName = request.FirstName;
            user.MiddleName = request.MiddleName;
            user.LastName = request.LastName;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "The decision on name change request {RequestId} was not saved.", requestId);
            return new(NameChangeDecisionOutcome.Invalid, $"The answer to {oldName}'s request could not be saved. Another administrator may have just answered it; the list shows the latest.");
        }

        _logger.LogInformation("Admin {AdminUserId} {Decision} name change request {RequestId} of provider {ProviderId}.",
            adminUserId, approve ? "approved" : "rejected", requestId, request.ProviderId);
        await PushAsync(request.ProviderId, user.UserId, approve ? "approved" : "rejected");
        return new(NameChangeDecisionOutcome.Success, approve
            ? $"Name changed: {oldName} is now {newName}."
            : $"The name change request of {oldName} (to {newName}) was rejected. Their name stays the same.");
    }
}
