using Microsoft.EntityFrameworkCore;

namespace Servisyo.Web.Data;

/// One row of dbo.usp_GetNotifications. The procedure names its columns exactly like these properties.
public sealed class NotificationRow
{
    public string Kind { get; set; } = string.Empty;        // booking-new, booking-update, booking-cancelled, application, application-new, general
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }                  // UTC
    public int? BookingId { get; set; }
    public bool IsUnread { get; set; }                       // newer than USERS.NOTIFICATIONS_SEEN_AT
}

/// One row of dbo.usp_GetBookingsNeedingAttention. The procedure names its columns exactly like these properties.
public sealed class AttentionRow
{
    public int BookingId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly BookingDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public int TotalCount { get; set; }                      // all bookings that need attention (can be more than the rows)
    public int LateStartCount { get; set; }                  // database v2.12: of them, started more than 15 minutes late
    public int NoShowCount { get; set; }                     // database v2.12: of them, cancelled as a provider no-show
}

/// Database v2.3: the two tables that only held derivable rows (NOTIFICATIONS, BOOKING_REPORTS) are gone. These read
/// the newest 25 rows that the stored procedures work out from the data (docs/sql/DatabaseDesign-v2.sql, PART 4).
/// The in-memory test database has no stored procedures: there they return nothing.
public static class DerivedQueries
{
    /// The most rows either procedure returns (rule: a derived list shows the newest 25).
    public const int Top = 25;

    /// The bell of THIS user: newest first, at most 25, each booking only its latest news.
    public static async Task<IReadOnlyList<NotificationRow>> GetNotificationsAsync(this ServisyoDbContext db, int userId, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return Array.Empty<NotificationRow>();
        return await db.Database
            .SqlQuery<NotificationRow>($"EXEC dbo.usp_GetNotifications @UserId = {userId}, @Top = {Top}")
            .ToListAsync(ct);
    }

    /// The red number on the bell.
    public static async Task<int> CountUnreadNotificationsAsync(this ServisyoDbContext db, int userId, CancellationToken ct = default) =>
        (await db.GetNotificationsAsync(userId, ct)).Count(n => n.IsUnread);

    /// "Opening the bell / Mark all as read": everything up to now is seen. Only this user's row changes.
    public static async Task MarkNotificationsSeenAsync(this ServisyoDbContext db, int userId, DateTime nowUtc, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null) return;
        user.NotificationsSeenAt = nowUtc;
        await db.SaveChangesAsync(ct);
    }

    /// The administrator's list of bookings that need a look (flagged or 1-2 star review, or a passed date still Pending / Accepted).
    public static async Task<IReadOnlyList<AttentionRow>> GetBookingsNeedingAttentionAsync(this ServisyoDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return Array.Empty<AttentionRow>();
        return await db.Database
            .SqlQuery<AttentionRow>($"EXEC dbo.usp_GetBookingsNeedingAttention @Top = {Top}")
            .ToListAsync(ct);
    }
}
