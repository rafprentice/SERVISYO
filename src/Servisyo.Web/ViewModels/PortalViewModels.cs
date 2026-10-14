namespace Servisyo.Web.ViewModels;

/// What the shared portal layout (top bar + sidebar) needs. Both dashboards fill this in.
public class PortalShellViewModel
{
    public string Role { get; set; } = "Customer";          // "Customer" or "Provider"
    public string FullName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;    // e.g. "Customer Account" or the business name
    public int UnreadNotifications { get; set; }
    public int UnreadMessages { get; set; }
}
