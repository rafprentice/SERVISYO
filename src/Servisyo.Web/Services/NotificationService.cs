using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface INotificationService
{
    /// The newest notifications and the unread number, for the bell's dropdown. Null when the account may not use the site.
    Task<NotificationPanel?> GetPanelAsync(int userId, CancellationToken ct = default);

    /// The "all notifications" page (paged). Null when the account may not use the site.
    Task<NotificationsPageViewModel?> GetPageAsync(int userId, int page, CancellationToken ct = default);

    /// Marks every unread notification of THIS user as read. Returns the new unread number (always 0), or null when not allowed.
    Task<int?> MarkAllReadAsync(int userId, CancellationToken ct = default);

    /// Marks one notification of THIS user as read and returns it (null when it does not exist or belongs to someone else).
    Task<NotificationOpenResult> OpenAsync(int userId, int notificationId, CancellationToken ct = default);
}

public sealed record NotificationOpenResult(bool Allowed, NotificationItem? Item);

public class NotificationService : INotificationService
{
    public const int PanelSize = 8;
    public const int PageSize = 15;

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;

    public NotificationService(ServisyoDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
    }

    private sealed record Me(string Role, string FirstName, string LastName, string? BusinessName);

    /// Any ACTIVE account (customer, provider, administrator) may read its own notifications. The person is always the
    /// signed-in user; nothing from the browser says whose notifications to show.
    private Task<Me?> FindMeAsync(int userId, CancellationToken ct) =>
        _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId && u.AccountStatus == AccountStatuses.Active)
            .Select(u => new Me(u.Role, u.FirstName, u.LastName, u.Provider != null ? u.Provider.BusinessName : null))
            .FirstOrDefaultAsync(ct);

    private NotificationItem ToItem(int id, string message, bool isRead, DateTime createdUtc)
    {
        var kind = NotificationKinds.Classify(message);
        return new NotificationItem(id, kind, NotificationKinds.TitleOf(kind), message,
            NotificationFormat.TimeAgo(createdUtc, _clock.GetUtcNow()), isRead);
    }

    public async Task<NotificationPanel?> GetPanelAsync(int userId, CancellationToken ct = default)
    {
        if (await FindMeAsync(userId, ct) is null) return null;

        var unread = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
        var rows = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
            .Take(PanelSize)
            .Select(n => new { n.NotificationId, n.Message, n.IsRead, n.CreatedAt })
            .ToListAsync(ct);

        return new NotificationPanel(unread, rows.Select(r => ToItem(r.NotificationId, r.Message, r.IsRead, r.CreatedAt)).ToList());
    }

    public async Task<NotificationsPageViewModel?> GetPageAsync(int userId, int page, CancellationToken ct = default)
    {
        var me = await FindMeAsync(userId, ct);
        if (me is null) return null;

        var mine = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        var total = await mine.CountAsync(ct);
        var unread = await mine.CountAsync(n => !n.IsRead, ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pageCount);

        var rows = await mine
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(n => new { n.NotificationId, n.Message, n.IsRead, n.CreatedAt })
            .ToListAsync(ct);

        var fullName = $"{me.FirstName} {me.LastName}".Trim();
        var isAdmin = me.Role == Roles.Administrator;
        var isProvider = me.Role == Roles.ServiceProvider;

        return new NotificationsPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = isAdmin ? "Admin" : isProvider ? "Provider" : "Customer",
                FirstName = me.FirstName,
                FullName = isAdmin ? me.FirstName : fullName,
                Initials = NameFormat.Initials(isAdmin ? me.FirstName : fullName),
                Subtitle = isAdmin ? "System Administrator" : isProvider ? me.BusinessName ?? string.Empty : "Customer Account",
                UnreadNotifications = unread,
                UnreadMessages = isAdmin ? 0 : await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            Unread = unread,
            Total = total,
            Page = page,
            PageCount = pageCount,
            Items = rows.Select(r => ToItem(r.NotificationId, r.Message, r.IsRead, r.CreatedAt)).ToList()
        };
    }

    public async Task<int?> MarkAllReadAsync(int userId, CancellationToken ct = default)
    {
        if (await FindMeAsync(userId, ct) is null) return null;

        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        foreach (var n in unread) n.IsRead = true;
        if (unread.Count > 0) await _db.SaveChangesAsync(ct);
        return 0;
    }

    public async Task<NotificationOpenResult> OpenAsync(int userId, int notificationId, CancellationToken ct = default)
    {
        if (await FindMeAsync(userId, ct) is null) return new(false, null);

        // the owner is part of the lookup: somebody else's notification is simply "not found"
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.NotificationId == notificationId && x.UserId == userId, ct);
        if (n is null) return new(true, null);

        if (!n.IsRead)
        {
            n.IsRead = true;
            await _db.SaveChangesAsync(ct);
        }
        return new(true, ToItem(n.NotificationId, n.Message, true, n.CreatedAt));
    }
}
