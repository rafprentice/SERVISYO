using System.Text.RegularExpressions;

namespace Servisyo.Web.ViewModels;

/// The rules for the text boxes on the Provider Profile page. They follow the limits of the database columns
/// and of the registration forms, so a profile can never hold something registration would have refused.
public static class ProfileRules
{
    public const int MaxFullNameLength = 155;      // registration: the full name is split into first + last (155 each)
    public const int MaxBusinessNameLength = 155;  // SERVICE_PROVIDERS.BUSINESS_NAME
    public const int MaxEmailLength = 155;         // USERS.EMAIL
    public const int MaxPhoneLength = 15;          // USERS.PHONE_NUMBER
    public const int MaxCityLength = 50;           // SERVICE_PROVIDERS.CITY
    public const int MaxAddressLength = 255;       // SERVICE_PROVIDERS.ADDRESS
    public const int MaxDescriptionLength = 255;   // SERVICE_PROVIDERS.DESCRIPTION

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PhoneShape = new(@"^(\+\d{7,14}|\d{7,15})$", RegexOptions.Compiled);

    /// "  Marco   Reyes " -> "Marco Reyes"
    public static string NormalizeName(string? name) => Spaces.Replace((name ?? string.Empty).Trim(), " ");

    /// Same rule as the customer registration page: first word = first name, everything after it = last name.
    /// "Juan Dela Cruz" -> ("Juan", "Dela Cruz"). The name must already be normalized.
    public static (string First, string Last) SplitName(string normalizedName)
    {
        var space = normalizedName.IndexOf(' ');
        return space < 0 ? (normalizedName, string.Empty) : (normalizedName[..space], normalizedName[(space + 1)..]);
    }

    /// "+63 917 555 1234" -> "+639175551234" (the database column holds at most 15 characters, so spaces cannot be kept).
    /// Blank -> null.
    public static string? NormalizePhone(string? phone)
    {
        var cleaned = Regex.Replace(phone ?? string.Empty, @"[\s\-\.\(\)]", string.Empty);
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// A normalized phone number: an optional + and 7 to 14 digits (or 7 to 15 digits without the +).
    public static bool IsValidPhone(string normalizedPhone) => PhoneShape.IsMatch(normalizedPhone);

    /// Browsers send line breaks as CR+LF; the database and the character limit see one character.
    public static string? NormalizeText(string? text)
    {
        var cleaned = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}

/// The values posted by the Business Information form. Nothing here says WHO the provider is:
/// that always comes from the sign-in cookie.
public class ProviderProfileForm
{
    public string? FullName { get; set; }
    public string? BusinessName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
}

public class ProviderProfilePageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Provider" };

    /// "/uploads/avatars/…" or null (then the initials are shown).
    public string? PhotoUrl { get; set; }

    /// True while the user has an active booking: the name fields are read-only (see ActiveBookingRules).
    public bool NameLocked { get; set; }

    // what is SAVED (shown in the header card; typing in the form does not change it until Save)
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? City { get; set; }

    public int StrengthPercent { get; set; }
    public string StrengthHint { get; set; } = string.Empty;

    // the form: the saved values, or what the provider typed when the server sent the form back
    public ProviderProfileForm Form { get; set; } = new();
    public IReadOnlyDictionary<string, string> FieldErrors { get; set; } = new Dictionary<string, string>();
    public string? FormError { get; set; }

    public IReadOnlyList<string> ServiceNames { get; set; } = Array.Empty<string>();
    public IReadOnlyList<ReviewItemViewModel> Reviews { get; set; } = Array.Empty<ReviewItemViewModel>();

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }

    /// "Marco's Plumbing Works · Quezon City", or just the business name when there is no city.
    public string HeaderLine => string.IsNullOrWhiteSpace(City) ? BusinessName : $"{BusinessName} · {City}";

    public string? ErrorFor(string field) => FieldErrors.TryGetValue(field, out var message) ? message : null;
}

public record ProviderProfilePageResult(ProviderDashboardAccess Access, ProviderProfilePageViewModel? Page);

public record ProviderProfileSaveResult(
    ProviderDashboardAccess Access,
    bool Saved,
    IReadOnlyDictionary<string, string> FieldErrors,
    string Message,
    string FullName = "",
    string Email = "");
