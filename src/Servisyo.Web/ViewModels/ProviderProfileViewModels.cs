using System.Text.RegularExpressions;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// The rules for the text boxes on the Provider Profile page. They follow the limits of the database columns
/// and of the registration forms, so a profile can never hold something registration would have refused.
public static class ProfileRules
{
    public const int MaxFullNameLength = InputRules.MaxNameLength;   // the full name is split into first + last (100 each)
    public const int MaxBusinessNameLength = 155;  // SERVICE_PROVIDERS.BUSINESS_NAME
    public const int MaxEmailLength = InputRules.MaxEmailLength;     // USERS.EMAIL (256)
    public const int MaxPhoneLength = 13;          // what may be typed: 09171234567 or +639171234567 (saved as 11 digits)
    public const int MaxAddressLength = 255;       // SERVICE_PROVIDERS.ADDRESS
    public const int MaxDescriptionLength = 255;   // SERVICE_PROVIDERS.DESCRIPTION

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PhoneShape = new(@"^09[0-9]{9}$", RegexOptions.Compiled);

    /// "  Marco   Reyes " -> "Marco Reyes"
    public static string NormalizeName(string? name) => Spaces.Replace((name ?? string.Empty).Trim(), " ");

    /// Same rule as the customer registration page: first word = first name, everything after it = last name.
    /// "Juan Dela Cruz" -> ("Juan", "Dela Cruz"). The name must already be normalized.
    public static (string First, string Last) SplitName(string normalizedName)
    {
        var space = normalizedName.IndexOf(' ');
        return space < 0 ? (normalizedName, string.Empty) : (normalizedName[..space], normalizedName[(space + 1)..]);
    }

    /// The phone number as it is saved: "+639175551234" -> "09175551234" (USERS.PHONE_NUMBER is always 09XXXXXXXXX).
    /// Spaces, dashes, dots and brackets are removed first (old numbers may have them). Blank -> null.
    /// Checking what was typed is InputRules.Phone's job; this only puts a valid number into the saved form.
    public static string? NormalizePhone(string? phone)
    {
        var cleaned = Regex.Replace(phone ?? string.Empty, @"[\s\-\.\(\)]", string.Empty);
        if (cleaned.StartsWith("+63", StringComparison.Ordinal)) cleaned = "0" + cleaned[3..];
        else if (cleaned.StartsWith("63", StringComparison.Ordinal) && cleaned.Length == 12) cleaned = "0" + cleaned[2..];
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// A saved (normalized) phone number: 11 digits starting with 09.
    public static bool IsValidPhone(string normalizedPhone) => PhoneShape.IsMatch(normalizedPhone);

    /// Browsers send line breaks as CR+LF; the database and the character limit see one character.
    public static string? NormalizeText(string? text)
    {
        var cleaned = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}

/// The Business Information form. Nothing here says WHO the provider is: that always comes from the sign-in cookie.
/// An approved provider can only change Email and About / Bio; the other values are shown read-only and are never
/// saved from this form (ProviderLockRules). They are still listed here so a request that tries to change them is noticed.
public class ProviderProfileForm
{
    // locked once the provider is approved (shown read-only)
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? BusinessName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public decimal? BaseFee { get; set; }

    // still editable
    public string? Email { get; set; }
    public string? Description { get; set; }
}

/// Once an administrator approves a provider (APPROVAL_STATUS = 'Approved'), the core profile and business information
/// is locked: names, business name, phone number, address and base fee. Only an administrator can change it.
/// The provider can still change their email, About / Bio, photo, services and availability.
public static class ProviderLockRules
{
    public const string ApprovedNotice = "Your provider account is approved and verified. Profile updates require Admin assistance.";
    public const string FieldLockedMessage = "This information is locked. Profile updates require Admin assistance.";

    /// The form fields an approved provider can no longer change.
    public static readonly IReadOnlyList<string> LockedFields =
        new[] { "FirstName", "MiddleName", "LastName", "BusinessName", "PhoneNumber", "Address", "BaseFee" };

    public static bool IsLocked(string? approvalStatus) => approvalStatus == ApprovalStatuses.Approved;

    /// The locked fields that the posted form tries to CHANGE. A disabled box is never sent by the browser, so a blank or
    /// missing value means "not sent" and the saved value stays. Sending the saved value again is not a change.
    /// The provider's User must be loaded.
    public static IReadOnlyList<string> ChangedFields(ProviderProfileForm form, Provider saved)
    {
        var changed = new List<string>();

        void Compare(string field, string? posted, string? current, Func<string?, string?> normalize)
        {
            if (string.IsNullOrWhiteSpace(posted)) return;
            if (!string.Equals(normalize(posted), normalize(current), StringComparison.Ordinal)) changed.Add(field);
        }

        Compare("FirstName", form.FirstName, saved.User.FirstName, ProfileRules.NormalizeName);
        Compare("MiddleName", form.MiddleName, saved.User.MiddleName, ProfileRules.NormalizeName);
        Compare("LastName", form.LastName, saved.User.LastName, ProfileRules.NormalizeName);
        Compare("BusinessName", form.BusinessName, saved.BusinessName, ProfileRules.NormalizeName);
        Compare("PhoneNumber", form.PhoneNumber, saved.User.PhoneNumber, ProfileRules.NormalizePhone);
        Compare("Address", form.Address, saved.Address, ProfileRules.NormalizeText);
        if (form.BaseFee is decimal fee && (saved.BaseFee is null || BaseFeeRules.Round(fee) != saved.BaseFee.Value))
            changed.Add("BaseFee");

        return changed;
    }
}

public class ProviderProfilePageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Provider" };

    /// "/uploads/avatars/…" or null (then the initials are shown).
    public string? PhotoUrl { get; set; }

    /// True when the provider is Approved: names, business name, phone, address and base fee are read-only (ProviderLockRules).
    public bool InfoLocked { get; set; }

    // what is SAVED (shown in the header card; typing in the form does not change it until Save)
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;

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

    /// Database v2.8: the "Name on Your Account" card (approved providers ask an administrator to change their name).
    public NameChangeSectionViewModel? NameChange { get; set; }

    /// "Marco's Plumbing Works · Cebu City" (SERVISYO serves Cebu City only).
    public string HeaderLine => $"{BusinessName} · {ServiceArea.City}";

    public string? ErrorFor(string field) => FieldErrors.TryGetValue(field, out var message) ? message : null;
}

public record ProviderProfilePageResult(ProviderDashboardAccess Access, ProviderProfilePageViewModel? Page);

public record ProviderProfileSaveResult(
    ProviderDashboardAccess Access,
    bool Saved,
    IReadOnlyDictionary<string, string> FieldErrors,
    string Message,
    string FullName = "",
    string Email = "",
    bool LockViolation = false);     // an approved provider tried to change locked information: the controller answers 403
