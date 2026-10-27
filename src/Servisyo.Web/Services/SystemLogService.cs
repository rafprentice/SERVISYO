using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public record SystemLogsPageResult(bool Allowed, SystemLogsPageViewModel? Page);

public interface ISystemLogService
{
    /// The System Logs page (Active administrators only), filtered and one page at a time.
    Task<SystemLogsPageResult> GetPageAsync(int adminUserId, SystemLogQuery query, CancellationToken ct = default);

    /// Writes a LOGIN line (the database triggers cannot see sign-ins). Never throws: signing in must not fail because of the log.
    Task LogLoginAsync(User user, CancellationToken ct = default);
}

public class SystemLogService : ISystemLogService
{
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);     // the Philippines has no daylight saving

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<SystemLogService> _logger;

    public SystemLogService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<SystemLogService>? logger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SystemLogService>.Instance;
    }

    public async Task LogLoginAsync(User user, CancellationToken ct = default)
    {
        try
        {
            _db.SystemLogs.Add(new SystemLog
            {
                UserId = user.UserId,
                UserRole = user.Role,
                ActionType = SystemLogActions.Login,
                EntityName = "User",
                EntityId = user.UserId,
                Description = NameFormat.Truncate($"{user.FirstName} {user.LastName} ({user.Email}) signed in.", 500),
                CreatedAt = _clock.GetUtcNow().UtcDateTime
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the LOGIN log line for user {UserId}.", user.UserId);
        }
    }

    public async Task<SystemLogsPageResult> GetPageAsync(int adminUserId, SystemLogQuery query, CancellationToken ct = default)
    {
        var admin = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == adminUserId && u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active)
            .Select(u => new { u.FirstName })
            .FirstOrDefaultAsync(ct);
        if (admin is null) return new(false, null);

        // only known filter values are used; anything else from the address bar is ignored
        var action = SystemLogActions.All.FirstOrDefault(a => string.Equals(a, query.Action, StringComparison.OrdinalIgnoreCase));
        var role = SystemLogsPageViewModel.Roles.FirstOrDefault(r => string.Equals(r, query.Role, StringComparison.OrdinalIgnoreCase));
        var search = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        if (search is { Length: > 100 }) search = search[..100];
        var (from, to) = (query.From, query.To);
        if (from is not null && to is not null && from > to) (from, to) = (to, from);

        var logs = _db.SystemLogs.AsNoTracking().AsQueryable();
        if (action is not null) logs = logs.Where(l => l.ActionType == action);
        if (role is not null) logs = logs.Where(l => l.UserRole == role);
        if (search is not null) logs = logs.Where(l => l.Description.Contains(search) || l.EntityName.Contains(search));
        // the dates are Cebu dates; the column is UTC
        if (from is not null)
        {
            var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue) - Manila;
            logs = logs.Where(l => l.CreatedAt >= fromUtc);
        }
        if (to is not null)
        {
            var toUtc = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue) - Manila;
            logs = logs.Where(l => l.CreatedAt < toUtc);
        }

        var total = await logs.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)SystemLogsPageViewModel.PageSize));
        var page = Math.Clamp(query.Page, 1, pageCount);

        var rows = await logs
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.SystemLogId)
            .Skip((page - 1) * SystemLogsPageViewModel.PageSize)
            .Take(SystemLogsPageViewModel.PageSize)
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
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
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == adminUserId && !n.IsRead, ct)
            },
            Query = new SystemLogQuery { From = from, To = to, Action = action, Role = role, Q = search, Page = page },
            Page = page,
            TotalMatching = total,
            Rows = rows.Select(l => new SystemLogRowViewModel
            {
                Id = l.SystemLogId,
                TimeText = (l.CreatedAt + Manila).ToString("MMM d, yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
                UserText = l.UserId is int id
                    ? (names.TryGetValue(id, out var n) ? $"{n} (#{id})" : $"User #{id}")
                    : "—",
                Role = l.UserRole,
                EntityText = l.EntityId is int eid ? $"{l.EntityName} #{eid}" : l.EntityName,
                Action = l.ActionType,
                ActionTone = l.ActionType switch
                {
                    SystemLogActions.Insert => "completed",
                    SystemLogActions.Update => "accepted",
                    SystemLogActions.Delete => "danger",
                    _ => "muted"
                },
                Description = l.Description
            }).ToList()
        };
        return new(true, vm);
    }
}
