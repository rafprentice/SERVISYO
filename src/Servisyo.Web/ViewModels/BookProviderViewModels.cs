namespace Servisyo.Web.ViewModels;

public static class BookingFormRules
{
    public const int MaxServices = 10;
    public const int MaxLocationLength = 255;      // BOOKINGS.SERVICE_LOCATION
    public const int MaxDescriptionLength = 255;   // BOOKINGS.SERVICE_DESCRIPTION
}

/// What the browser posts with "Send Booking Request". Nothing here says WHO the customer is (that comes from the
/// sign-in cookie) and the provider is in the address; prices, durations and the total are never taken from the browser.
public class BookProviderForm
{
    public List<int>? ServiceIds { get; set; }
    public DateOnly? Date { get; set; }
    public TimeOnly? Time { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
}

public class BookServiceOptionViewModel
{
    public int ServiceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }

    public string DurationText => ServiceFormat.Duration(DurationMinutes);
    public string PriceText => ServiceFormat.Peso(Price);
}

public class BookProviderPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Customer" };

    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? City { get; set; }
    public decimal? AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public int MinNoticeDays { get; set; }

    public IReadOnlyList<BookServiceOptionViewModel> Services { get; set; } = Array.Empty<BookServiceOptionViewModel>();

    /// The first and last date the date box allows (the server checks again on send).
    public DateOnly EarliestDate { get; set; }
    public DateOnly LatestDate { get; set; }

    public BookProviderForm Form { get; set; } = new();
    public IReadOnlyDictionary<string, string> FieldErrors { get; set; } = new Dictionary<string, string>();
    public string? FormError { get; set; }

    public string? ErrorFor(string field) => FieldErrors.TryGetValue(field, out var message) ? message : null;
    public bool IsSelected(int serviceId) => Form.ServiceIds?.Contains(serviceId) == true;
}

public enum BookPageOutcome
{
    Ok,
    NotAllowed,            // not an Active customer: the controller ends the session
    ProviderUnavailable    // no such provider, not approved, suspended, or no active services
}

public record BookProviderPageResult(BookPageOutcome Outcome, BookProviderPageViewModel? Page, PortalShellViewModel? Shell);

public record SlotViewModel(string Value, string Text);

public record BookSlotsResult(bool Allowed, bool ProviderAvailable, string? Message, IReadOnlyList<SlotViewModel> Slots);

public enum BookOutcome
{
    Created,
    Invalid,               // something to fix on the form (the problems are in FieldErrors)
    ProviderUnavailable,
    NotAllowed
}

public record BookProviderResult(
    BookOutcome Outcome,
    IReadOnlyDictionary<string, string> FieldErrors,
    string Message,
    int? BookingId = null,
    string ProviderName = "")
{
    public bool Succeeded => Outcome == BookOutcome.Created;
}
