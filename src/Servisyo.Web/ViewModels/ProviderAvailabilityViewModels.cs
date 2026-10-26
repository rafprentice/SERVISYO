using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// Day numbers follow the database: 0 = Sunday ... 6 = Saturday. The page lists Monday first.
public static class AvailabilityDays
{
    public static readonly string[] Names = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
    public static readonly int[] DisplayOrder = { 1, 2, 3, 4, 5, 6, 0 };
}

public class BreakForm
{
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}

public class DayScheduleForm
{
    public int DayOfWeek { get; set; }
    public bool IsAvailable { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    public List<BreakForm> Breaks { get; set; } = new();
}

public class UnavailableDateForm
{
    public DateOnly? Date { get; set; }
    public string? Reason { get; set; }
}

/// Everything the page posts back when "Save Schedule" is pressed.
/// There is no provider id here on purpose: the provider is always the signed-in user.
public class AvailabilityForm
{
    public List<DayScheduleForm> Days { get; set; } = new();
    public List<UnavailableDateForm> Dates { get; set; } = new();
    public int MinNoticeDays { get; set; } = 1;
    public int MaxJobsPerDay { get; set; } = 5;
}

public class AvailabilityPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Provider" };
    public AvailabilityForm Form { get; set; } = new();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    /// True when the provider has never saved a schedule: the page shows a suggested Mon-Fri 8-5 week.
    public bool IsSuggested { get; set; }

    public DateOnly Today { get; set; }
    public IReadOnlyList<(int Value, string Label)> NoticeOptions { get; set; } = Array.Empty<(int, string)>();

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }
}

public sealed record AvailabilityPageResult(ProviderDashboardAccess Access, AvailabilityPageViewModel? Page);

public sealed record AvailabilitySaveResult(ProviderDashboardAccess Access, bool Saved, IReadOnlyList<string> Errors, string Message);
