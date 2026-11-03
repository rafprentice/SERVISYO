namespace Servisyo.Web.ViewModels;

public static class BookingFormRules
{
    public const int MaxServices = 10;
    public const int MaxLocationLength = 255;      // BOOKINGS.SERVICE_LOCATION
    public const int MaxDescriptionLength = 255;   // BOOKINGS.SERVICE_DESCRIPTION

    public const string AddressRequiredMessage = "Please select a service address.";
    public const string AddressNotInListMessage = "Please choose one of the addresses in the list.";
}

/// The addresses a customer can choose in step 3 ("Where and what?") of the booking form.
/// The chosen text is saved as it is in BOOKINGS.SERVICE_LOCATION, so every bookings page shows it unchanged.
public static class BookingAddresses
{
    public const string Minglanilla = "Lower Pakigne, Minglanilla";

    /// The 5 Cebu City addresses of the registration forms (CebuLocations), then Minglanilla.
    public static readonly IReadOnlyList<string> Preset = CebuLocations.All.Select(l => l.Full).Append(Minglanilla).ToList();

    /// The options for ONE customer: their saved profile address first when it is not already one of the preset ones
    /// (older accounts typed their address), then the preset ones. The saved address always comes from the database.
    public static IReadOnlyList<string> For(string? savedAddress)
    {
        var saved = savedAddress?.Trim();
        if (string.IsNullOrEmpty(saved) || saved.Length > BookingFormRules.MaxLocationLength || Preset.Contains(saved, StringComparer.Ordinal))
            return Preset;
        return new[] { saved }.Concat(Preset).ToList();
    }

    /// True when the posted value is exactly one of this customer's options (a value typed or changed in the browser is refused).
    public static bool IsAllowed(string? value, string? savedAddress)
        => value is not null && For(savedAddress).Contains(value.Trim(), StringComparer.Ordinal);
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
    public decimal? AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public int MinNoticeDays { get; set; }

    public IReadOnlyList<BookServiceOptionViewModel> Services { get; set; } = Array.Empty<BookServiceOptionViewModel>();

    /// The first and last date the date box allows (the server checks again on send).
    public DateOnly EarliestDate { get; set; }
    public DateOnly LatestDate { get; set; }

    public BookProviderForm Form { get; set; } = new();

    /// The service-address dropdown (BookingAddresses.For this customer) and the customer's saved profile address.
    public IReadOnlyList<string> AddressOptions { get; set; } = BookingAddresses.Preset;
    public string? SavedAddress { get; set; }

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
