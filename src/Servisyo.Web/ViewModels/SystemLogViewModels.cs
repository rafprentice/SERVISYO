namespace Servisyo.Web.ViewModels;

/// The filters of the System Logs page (all optional, from the address bar).
public class SystemLogQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Action { get; set; }
    public string? Role { get; set; }
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
}

public class SystemLogRowViewModel
{
    public int Id { get; set; }
    public string TimeText { get; set; } = string.Empty;   // Cebu time
    public string UserText { get; set; } = string.Empty;   // "Ana Reyes (#12)" or "—"
    public string Role { get; set; } = string.Empty;
    public string EntityText { get; set; } = string.Empty; // "Booking #6"
    public string Action { get; set; } = string.Empty;
    public string ActionTone { get; set; } = "muted";
    public string Description { get; set; } = string.Empty;
}

public class SystemLogsPageViewModel
{
    public const int PageSize = 25;
    public static readonly string[] Roles = { "Customer", "Service Provider", "Administrator", "Guest", "System" };

    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };
    public SystemLogQuery Query { get; set; } = new();
    public IReadOnlyList<SystemLogRowViewModel> Rows { get; set; } = Array.Empty<SystemLogRowViewModel>();
    public int Page { get; set; } = 1;
    public int TotalMatching { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalMatching / (double)PageSize));
}
