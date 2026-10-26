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

    public static string Classify(string? message)
    {
        var m = (message ?? string.Empty).Trim();
        if (m.StartsWith("You have a new booking request", StringComparison.OrdinalIgnoreCase)) return BookingNew;
        if (m.Contains("was cancelled by the customer", StringComparison.OrdinalIgnoreCase)) return BookingCancelled;
        if (m.StartsWith("Your booking #", StringComparison.OrdinalIgnoreCase)) return BookingUpdate;
        if (m.StartsWith("New provider application", StringComparison.OrdinalIgnoreCase)) return ApplicationNew;
        if (m.StartsWith("Your provider application", StringComparison.OrdinalIgnoreCase)) return Application;
        return General;
    }

    public static string TitleOf(string kind) => kind switch
    {
        BookingNew => "New Booking Request",
        BookingCancelled => "Booking Cancelled",
        BookingUpdate => "Booking Update",
        ApplicationNew => "New Provider Application",
        Application => "Provider Application",
        _ => "Notification"
    };

    /// The icon name (same names as the Icons helper).
    public static string IconOf(string kind) => kind switch
    {
        BookingNew or BookingCancelled or BookingUpdate => "calendar",
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
