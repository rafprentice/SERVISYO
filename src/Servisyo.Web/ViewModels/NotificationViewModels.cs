using System.Globalization;

namespace Servisyo.Web.ViewModels;

/// The NOTIFICATIONS table only holds a message, so the kind of notification (and from it the title, the icon and
/// the page it leads to) is worked out from the wording the system itself writes. Anything unknown is "general".
public static class NotificationKinds
{
    public const string BookingNew = "booking-new";
    public const string BookingCancelled = "booking-cancelled";
    public const string BookingUpdate = "booking-update";
    public const string ApplicationNew = "application-new";
    public const string Application = "application";
    public const string General = "general";
    public const string DayOff = "day-off";              // v2.7: the provider's answer to a day-off request (or an extra day given)
    public const string DayOffNew = "day-off-new";       // v2.7: the administrator's list of requests waiting for an answer

    public static readonly IReadOnlyList<string> All = new[] { BookingNew, BookingCancelled, BookingUpdate, ApplicationNew, Application, General, DayOff, DayOffNew };

    /// The kind comes from dbo.usp_GetNotifications (database v2.3); anything unknown is shown as a general notification.
    public static string Normalize(string? kind) => All.Contains(kind ?? string.Empty) ? kind! : General;

    public static string TitleOf(string kind) => kind switch
    {
        BookingNew => "New Booking Request",
        BookingCancelled => "Booking Cancelled",
        BookingUpdate => "Booking Update",
        ApplicationNew => "New Provider Application",
        Application => "Provider Application",
        DayOff => "Day Off",
        DayOffNew => "Day Off Request",
        _ => "Notification"
    };

    /// The icon name (same names as the Icons helper).
    public static string IconOf(string kind) => kind switch
    {
        BookingNew or BookingCancelled or BookingUpdate or DayOff or DayOffNew => "calendar",
        ApplicationNew or Application => "user",
        _ => "bell"
    };
}

public static class NotificationFormat
{
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);     // the Philippines has no daylight saving

    /// "Just now", "10 mins ago", "3 hrs ago", "Yesterday", "4 days ago", "Oct 2, 2026".
    public static string TimeAgo(DateTime createdUtc, DateTimeOffset nowUtc)
    {
        var created = new DateTimeOffset(DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc));
        var age = nowUtc - created;
        if (age < TimeSpan.FromMinutes(1)) return "Just now";          // also covers a slightly-ahead server clock
        if (age < TimeSpan.FromHours(1))
        {
            var mins = (int)age.TotalMinutes;
            return mins == 1 ? "1 min ago" : $"{mins} mins ago";
        }

        var days = (nowUtc.ToOffset(Manila).Date - created.ToOffset(Manila).Date).Days;
        if (days <= 0)
        {
            var hours = (int)age.TotalHours;
            return hours == 1 ? "1 hr ago" : $"{hours} hrs ago";
        }
        if (days == 1) return "Yesterday";
        if (days < 7) return $"{days} days ago";
        return created.ToOffset(Manila).ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}

/// Id is only the position in the list (1 = newest): notifications are worked out, not stored, so they have no key.
public sealed record NotificationItem(int Id, string Kind, string Title, string Message, string TimeText, bool IsRead)
{
    public string Icon => NotificationKinds.IconOf(Kind);
}

/// What the dropdown under the bell needs.
public sealed record NotificationPanel(int Unread, IReadOnlyList<NotificationItem> Items);

public class NotificationsPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new();
    public int Unread { get; set; }
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public IReadOnlyList<NotificationItem> Items { get; set; } = Array.Empty<NotificationItem>();
}
