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

    /// The "all notifications" page (the newest 25, paged). Null when the account may not use the site.
    Task<NotificationsPageViewModel?> GetPageAsync(int userId, int page, CancellationToken ct = default);

    /// Everything up to now counts as seen (USERS.NOTIFICATIONS_SEEN_AT). Returns the new unread number (always 0),
    /// or null when not allowed.
    Task<int?> MarkAllReadAsync(int userId, CancellationToken ct = default);
}

/// Where the rows come from. The real one runs dbo.usp_GetNotifications; tests can give their own rows.
public interface INotificationSource
{
    Task<IReadOnlyList<NotificationRow>> GetAsync(int userId, CancellationToken ct = default);
}

public sealed class StoredProcedureNotificationSource : INotificationSource
{
    private readonly ServisyoDbContext _db;
    public StoredProcedureNotificationSource(ServisyoDbContext db) => _db = db;
    public Task<IReadOnlyList<NotificationRow>> GetAsync(int userId, CancellationToken ct = default) => _db.GetNotificationsAsync(userId, ct);
}

/// Database v2.3: nothing is stored per notification. dbo.usp_GetNotifications works out the newest 25 from the
/// bookings, payments, reviews, administrator actions and applications; USERS.NOTIFICATIONS_SEEN_AT says which are new.
public class NotificationService : INotificationService
{
    public const int PanelSize = 8;
    public const int PageSize = 15;

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly INotificationSource _source;

    public NotificationService(ServisyoDbContext db, TimeProvider? clock = null, INotificationSource? source = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _source = source ?? new StoredProcedureNotificationSource(db);
    }

    private sealed record Me(string Role, string FirstName, string LastName, string? BusinessName);

    /// Any ACTIVE account (customer, provider, administrator) may read its own notifications. The person is always the
    /// signed-in user; nothing from the browser says whose notifications to show.
    private Task<Me?> FindMeAsync(int userId, CancellationToken ct) =>
        _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId && u.AccountStatus == AccountStatuses.Active)
            .Select(u => new Me(u.Role, u.FirstName, u.LastName, u.Provider != null ? u.Provider.BusinessName : null))
            .FirstOrDefaultAsync(ct);

    /// The rows, newest first (the procedure already sorts them; sorting again keeps the order certain).
    private async Task<List<NotificationRow>> RowsAsync(int userId, CancellationToken ct) =>
        (await _source.GetAsync(userId, ct))
            .OrderByDescending(r => r.CreatedAt)
            .Take(DerivedQueries.Top)
            .ToList();

    private NotificationItem ToItem(int position, NotificationRow r)
    {
        var kind = NotificationKinds.Normalize(r.Kind);
        return new NotificationItem(position, kind, NotificationKinds.TitleOf(kind), r.Message,
            NotificationFormat.TimeAgo(r.CreatedAt, _clock.GetUtcNow()), !r.IsUnread);
    }

    public async Task<NotificationPanel?> GetPanelAsync(int userId, CancellationToken ct = default)
    {
        if (await FindMeAsync(userId, ct) is null) return null;

        var rows = await RowsAsync(userId, ct);
        return new NotificationPanel(rows.Count(r => r.IsUnread), rows.Take(PanelSize).Select((r, i) => ToItem(i + 1, r)).ToList());
    }

    public async Task<NotificationsPageViewModel?> GetPageAsync(int userId, int page, CancellationToken ct = default)
    {
        var me = await FindMeAsync(userId, ct);
        if (me is null) return null;

        var rows = await RowsAsync(userId, ct);
        var total = rows.Count;
        var unread = rows.Count(r => r.IsUnread);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pageCount);

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
            Items = rows.Select((r, i) => ToItem(i + 1, r)).Skip((page - 1) * PageSize).Take(PageSize).ToList()
        };
    }

    public async Task<int?> MarkAllReadAsync(int userId, CancellationToken ct = default)
    {
        if (await FindMeAsync(userId, ct) is null) return null;

        await _db.MarkNotificationsSeenAsync(userId, _clock.GetUtcNow().UtcDateTime, ct);
        return 0;
    }
}
