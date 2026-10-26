using System.Globalization;

namespace Servisyo.Web.ViewModels;

/// How prices and durations are written on the My Services page.
public static class ServiceFormat
{
    /// 800 -> "₱800",  1500 -> "₱1,500",  850.5 -> "₱850.50"
    public static string Peso(decimal price) =>
        "₱" + (price == decimal.Truncate(price)
            ? price.ToString("N0", CultureInfo.InvariantCulture)
            : price.ToString("N2", CultureInfo.InvariantCulture));

    /// 30 -> "30 min",  60 -> "1 hr",  90 -> "1 hr 30 min",  120 -> "2 hrs"
    public static string Duration(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";
        var hours = minutes / 60;
        var rest = minutes % 60;
        var h = hours == 1 ? "1 hr" : $"{hours} hrs";
        return rest == 0 ? h : $"{h} {rest} min";
    }
}

/// The choices offered in the "How long does it take?" list.
/// The booking rules (Stage 9) use this length to stop two jobs from overlapping.
public static class ServiceDurations
{
    public const int Default = 60;
    public static readonly int[] Choices = { 30, 60, 90, 120, 180, 240, 300, 360, 480 };
}

/// The values posted by the "Add New Service" dialog.
public class AddServiceForm
{
    public string? ServiceName { get; set; }
    public string? Description { get; set; }
    public int? CategoryId { get; set; }
    public decimal? Price { get; set; }
    public int? DurationMinutes { get; set; } = ServiceDurations.Default;
}

/// The value posted by the Activate / Deactivate button. "Activate" is the state the provider WANTS,
/// so pressing a button twice (or in two tabs) cannot flip the service back and forth.
public class ServiceStatusForm
{
    public bool Activate { get; set; }
}

public class ServiceCardViewModel
{
    public int ServiceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string StatusText => IsActive ? "Active" : "Inactive";
    public string StatusTone => IsActive ? "completed" : "muted";        // green / grey
    public string PriceText => ServiceFormat.Peso(Price);
    public string MetaText => string.IsNullOrWhiteSpace(CategoryName)
        ? ServiceFormat.Duration(DurationMinutes)
        : $"{ServiceFormat.Duration(DurationMinutes)} · {CategoryName}";
}

public sealed record CategoryChoice(int CategoryId, string Name);

public class ServicesPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Provider" };
    public IReadOnlyList<ServiceCardViewModel> Cards { get; set; } = Array.Empty<ServiceCardViewModel>();

    /// Active categories the provider can pick from in the Add dialog.
    public IReadOnlyList<CategoryChoice> Categories { get; set; } = Array.Empty<CategoryChoice>();
    public IReadOnlyList<(int Minutes, string Label)> DurationOptions { get; set; } = Array.Empty<(int, string)>();

    // the Add dialog: closed normally; open again (with what was typed and the problems) when the server rejected it
    public bool ShowAddDialog { get; set; }
    public AddServiceForm Form { get; set; } = new();
    public IReadOnlyDictionary<string, string> FieldErrors { get; set; } = new Dictionary<string, string>();
    public string? FormError { get; set; }

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }

    public string? ErrorFor(string field) => FieldErrors.TryGetValue(field, out var e) ? e : null;
}

public sealed record ServicesPageResult(ProviderDashboardAccess Access, ServicesPageViewModel? Page);

/// Added = true when the service was saved. Otherwise FieldErrors says which boxes need fixing and Message is the general problem.
public sealed record AddServiceResult(ProviderDashboardAccess Access, bool Added, IReadOnlyDictionary<string, string> FieldErrors, string Message);

public enum ServiceStatusOutcome { Success, NotFound, NoChange }

public sealed record ServiceStatusResult(ProviderDashboardAccess Access, ServiceStatusOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == ServiceStatusOutcome.Success;
}
