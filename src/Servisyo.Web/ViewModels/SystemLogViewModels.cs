using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// The filters of the System Logs page (all optional, from the address bar): ?actionType=UPDATE&search=phone&page=2
public class SystemLogQuery
{
    public string? ActionType { get; set; }     // INSERT, UPDATE, DELETE, LOGIN or nothing (= All)
    public string? Search { get; set; }
    public string? Role { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
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

/// One filter pill: "All", "INSERT", "UPDATE", "DELETE", "LOGIN" with how many lines it has (with the other filters applied).
public sealed record SystemLogTab(string? ActionType, string Label, int Count);

public class SystemLogsPageViewModel
{
    public const int PageSize = 20;
    public static readonly string[] Roles = { "Customer", "Service Provider", "Administrator", "Guest", "System" };

    public PortalShellViewModel Shell { get; set; } = new() { Role = "Admin" };
    public SystemLogQuery Query { get; set; } = new();
    public IReadOnlyList<SystemLogTab> Tabs { get; set; } = Array.Empty<SystemLogTab>();
    public PagedResult<SystemLogRowViewModel> Logs { get; set; } = new(Array.Empty<SystemLogRowViewModel>(), 1, PageSize, 0);

    public bool AnyFilter => Query.Search is not null || Query.Role is not null || Query.From is not null || Query.To is not null;

    /// The address-bar values of a link that keeps every filter (pills and pages use it).
    public object Link(string? actionType, int page) => new
    {
        actionType,
        search = Query.Search,
        role = Query.Role,
        from = Query.From?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        to = Query.To?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        page = page > 1 ? page : (int?)null
    };

    public static string ToneOf(string actionType) => actionType switch
    {
        AuditActionType.Insert => "completed",
        AuditActionType.Update => "accepted",
        AuditActionType.Delete => "danger",
        _ => "muted"                                  // LOGIN
    };
}
