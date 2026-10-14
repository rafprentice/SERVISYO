using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

public enum ProviderDashboardAccess
{
    Allowed,
    NotAllowed,   // not an Active service-provider account
    Pending,      // application not approved yet
    Rejected      // application was rejected
}

public sealed record ProviderDashboardResult(ProviderDashboardAccess Access, ProviderDashboardViewModel? Dashboard);

/// How one booking status is shown to the provider.
public sealed record ProviderBookingDisplay(string Badge, string Tone, string? ActionLabel, string? SideText)
{
    public static ProviderBookingDisplay For(string status) => status switch
    {
        BookingStatuses.Pending => new("New Request", "new", "Review Request", null),
        BookingStatuses.Accepted => new("Scheduled", "scheduled", "Start Service", null),
        BookingStatuses.InProgress => new("In Progress", "progress", "Complete Service", null),
        BookingStatuses.Completed => new("Completed", "muted", null, "Service Completed ✓"),
        BookingStatuses.Rejected => new("Declined", "muted", null, "You declined this request"),
        BookingStatuses.Cancelled => new("Cancelled", "muted", null, "Cancelled by the customer"),
        _ => new(status, "muted", null, null)
    };
}

public static class ProfileStrength
{
    /// 6 equal checks. Returns the percentage and what to do next (null when 100%).
    public static (int Percent, string? NextStep) Calculate(
        bool hasService, bool hasAvailability, bool hasDescription,
        bool hasPhoto, bool hasAddress, bool hasPhone)
    {
        var checks = new (bool Done, string Hint)[]
        {
            (hasService, "Add your first service"),
            (hasAvailability, "Set your weekly availability"),
            (hasDescription, "Add a business description"),
            (hasPhoto, "Add a profile photo"),
            (hasAddress, "Add your service address and city"),
            (hasPhone, "Add a phone number")
        };
        var done = checks.Count(c => c.Done);
        var percent = (int)Math.Round(done * 100.0 / checks.Length);
        var next = checks.FirstOrDefault(c => !c.Done).Hint;
        return (percent, done == checks.Length ? null : next);
    }
}

public class AttentionItemViewModel
{
    public string Text { get; set; } = string.Empty;
    public string Tone { get; set; } = "info";          // info | warning | success
    public string ActionLabel { get; set; } = string.Empty;
}

public class ProviderJobViewModel
{
    public int BookingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public string? Location { get; set; }
    public string Status { get; set; } = string.Empty;
    public ProviderBookingDisplay Display { get; set; } = ProviderBookingDisplay.For(string.Empty);
}

public class ScheduleItemViewModel
{
    public TimeOnly Time { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class ReviewItemViewModel
{
    public byte Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
}

public class ProviderDashboardViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Provider" };
    public string BusinessName { get; set; } = string.Empty;
    public string? City { get; set; }

    public int NewRequests { get; set; }
    public int Upcoming { get; set; }
    public int InProgress { get; set; }
    public decimal? AvgRating { get; set; }
    public int ReviewCount { get; set; }

    public IReadOnlyList<AttentionItemViewModel> Attention { get; set; } = Array.Empty<AttentionItemViewModel>();
    public IReadOnlyList<ProviderJobViewModel> RecentRequests { get; set; } = Array.Empty<ProviderJobViewModel>();
    public IReadOnlyList<ScheduleItemViewModel> Today { get; set; } = Array.Empty<ScheduleItemViewModel>();
    public string AvailabilityText { get; set; } = string.Empty;

    public int ProfilePercent { get; set; }
    public string? ProfileNextStep { get; set; }
    public IReadOnlyList<ReviewItemViewModel> Reviews { get; set; } = Array.Empty<ReviewItemViewModel>();
}
