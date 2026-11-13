using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

/// Database v2.10: the texts the APP writes into SYSTEM_LOGS, in the same standard format as the audit triggers
/// (Data/AuditStandardSql.cs). INSERT / UPDATE lines always come from the triggers; the app only writes LOGIN lines and
/// the one DELETE line of the inactive-account clean-up (the triggers are switched off for that one change).
public static class AuditFormat
{
    public const int MaxLength = 500;                  // SYSTEM_LOGS.DESCRIPTION
    public const string GuestRole = "Guest";          // someone who is not signed in
    public const string SystemRole = "System";        // the app itself (a scheduled job)

    /// "First Middle Last" (the middle name only when there is one), like dbo.fn_GetFullName.
    public static string FullName(string? first, string? middle, string? last) =>
        string.Join(" ", new[] { first, middle, last }.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray());

    /// LOGIN: "Ana Reyes (ana@gmail.com) signed in."
    public static string SignedIn(User user) =>
        Fit($"{FullName(user.FirstName, user.MiddleName, user.LastName)} ({user.Email}) signed in.");

    /// LOGIN (failed): "Failed login attempt for ana@gmail.com." - what was typed, trimmed, lower case, at most 100 characters.
    public static string FailedLogin(string? typedEmail)
    {
        var email = (typedEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length == 0) email = "(none)";
        else if (email.Length > 100) email = email[..97] + "...";
        return Fit($"Failed login attempt for {email}.");
    }

    /// DELETE: "{Role} {Full Name} deleted {Entity} #{Id}: {summary}." (the actor is only the role when nobody is signed in).
    public static string Deleted(string actor, string entityName, int entityId, string summary) =>
        Fit($"{actor} deleted {entityName} #{entityId}: {summary}.");

    /// Never longer than the column (the database would refuse the whole save).
    public static string Fit(string line) => line.Length <= MaxLength ? line : line[..(MaxLength - 3)] + "...";
}

public record SystemLogsPageResult(bool Allowed, SystemLogsPageViewModel? Page);

public interface IAuditLogService
{
    /// Admin > System Logs (Active administrators only): one page of 20, newest first, read without change tracking.
    Task<SystemLogsPageResult> GetPageAsync(int adminUserId, SystemLogQuery query, CancellationToken ct = default);

    /// Writes a LOGIN line (the database cannot see sign-ins). Never throws: signing in must not fail because of the log.
    Task LogLoginAsync(User user, CancellationToken ct = default);

    /// Writes a LOGIN line for a wrong email or password (USER_ID empty, role Guest). Never throws.
    Task LogFailedLoginAsync(string? typedEmail, CancellationToken ct = default);
}

public class AuditLogService : IAuditLogService
{
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);     // the Philippines has no daylight saving

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<AuditLogService>? logger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditLogService>.Instance;
    }

    public Task LogLoginAsync(User user, CancellationToken ct = default) =>
        WriteAsync(new SystemLog
        {
            UserId = user.UserId,
            UserRole = user.Role,
            ActionType = AuditActionType.Login,
            EntityName = "User",
            EntityId = user.UserId,
            Description = AuditFormat.SignedIn(user)
        }, ct);

    public Task LogFailedLoginAsync(string? typedEmail, CancellationToken ct = default) =>
        WriteAsync(new SystemLog
        {
            UserId = null,
            UserRole = AuditFormat.GuestRole,
            ActionType = AuditActionType.Login,
            EntityName = "User",
            EntityId = null,
            Description = AuditFormat.FailedLogin(typedEmail)
        }, ct);

    private async Task WriteAsync(SystemLog line, CancellationToken ct)
    {
        try
        {
            line.CreatedAt = _clock.GetUtcNow().UtcDateTime;
            _db.SystemLogs.Add(line);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not write the {ActionType} log line.", line.ActionType);
            _db.Entry(line).State = EntityState.Detached;          // do not try to save it again with something else
        }
    }

    public async Task<SystemLogsPageResult> GetPageAsync(int adminUserId, SystemLogQuery query, CancellationToken ct = default)
    {
        var admin = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == adminUserId && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active)
            .Select(u => new { u.FirstName })
            .FirstOrDefaultAsync(ct);
        if (admin is null) return new(false, null);

        // only known values are used; anything else typed in the address bar is ignored
        var actionType = AuditActionType.All.FirstOrDefault(a => string.Equals(a, query.ActionType?.Trim(), StringComparison.OrdinalIgnoreCase));
        var role = SystemLogsPageViewModel.Roles.FirstOrDefault(r => string.Equals(r, query.Role?.Trim(), StringComparison.OrdinalIgnoreCase));
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search is { Length: > 100 }) search = search[..100];
        var (from, to) = (query.From, query.To);
        if (from is not null && to is not null && from > to) (from, to) = (to, from);

        // every filter except the action type (the pills count inside these)
        var filtered = _db.SystemLogs.AsNoTracking();
        if (role is not null) filtered = filtered.Where(l => l.UserRole == role);
        if (search is not null) filtered = filtered.Where(l => l.Description.Contains(search) || l.EntityName.Contains(search));
        if (from is not null)
        {
            var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue) - Manila;          // the dates are Cebu dates; the column is UTC
            filtered = filtered.Where(l => l.CreatedAt >= fromUtc);
        }
        if (to is not null)
        {
            var toUtc = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue) - Manila;
            filtered = filtered.Where(l => l.CreatedAt < toUtc);
        }

        // the numbers on the pills: ONE grouped COUNT (uses IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT)
        var counts = await filtered
            .GroupBy(l => l.ActionType)
            .Select(g => new { ActionType = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var tabs = new List<SystemLogTab> { new(null, "All", counts.Sum(c => c.Count)) };
        tabs.AddRange(AuditActionType.All.Select(a => new SystemLogTab(a, a, counts.Where(c => c.ActionType == a).Sum(c => c.Count))));

        // the page itself: newest first, 20 rows, counted and cut by the database
        var shown = actionType is null ? filtered : filtered.Where(l => l.ActionType == actionType);
        var page = await shown
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.SystemLogId)
            .ToPagedResultAsync(query.Page, SystemLogsPageViewModel.PageSize, ct);

        var userIds = page.Items.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .Select(u => new { u.UserId, u.FirstName, u.LastName })
            .ToDictionaryAsync(u => u.UserId, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var vm = new SystemLogsPageViewModel
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
            Query = new SystemLogQuery { ActionType = actionType, Search = search, Role = role, From = from, To = to, Page = page.Page },
            Tabs = tabs,
            Logs = page.Map(l => new SystemLogRowViewModel
            {
                Id = l.SystemLogId,
                TimeText = (l.CreatedAt + Manila).ToString("MMM d, yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
                UserText = l.UserId is int id
                    ? (names.TryGetValue(id, out var n) ? $"{n} (#{id})" : $"User #{id}")
                    : "—",
                Role = l.UserRole,
                EntityText = l.EntityId is int eid ? $"{l.EntityName} #{eid}" : l.EntityName,
                Action = l.ActionType,
                ActionTone = SystemLogsPageViewModel.ToneOf(l.ActionType),
                Description = l.Description
            })
        };
        return new(true, vm);
    }
}
