using System.Text.RegularExpressions;

namespace Servisyo.Web.ViewModels;

/// The password rules of the Change Password form. They are the same as the registration page
/// (8 to 100 characters, at least one letter and one number), so a password can never be changed to
/// something registration would have refused.
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    private static readonly Regex LetterAndNumber = new(@"^(?=.*[A-Za-z])(?=.*\d).+$", RegexOptions.Compiled);

    /// The problem with a new password, or null when it is acceptable.
    public static string? Check(string? password)
    {
        if (string.IsNullOrEmpty(password)) return "Enter a new password.";
        if (password.Length < MinLength) return $"The password must be at least {MinLength} characters.";
        if (password.Length > MaxLength) return $"The password can be at most {MaxLength} characters.";
        if (!LetterAndNumber.IsMatch(password)) return "Password must contain at least one letter and one number.";
        return null;
    }
}

/// The values posted by the Personal Information form. Nothing here says WHO the customer is:
/// that always comes from the sign-in cookie.
public class CustomerProfileForm
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? City { get; set; }
}

/// The Saved Address form. A blank address removes the saved one.
public class CustomerAddressForm
{
    public string? Address { get; set; }
}

public class CustomerPasswordForm
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}

public class CustomerProfilePageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new() { Role = "Customer" };

    // what is SAVED (shown in the summary card; typing in a form does not change it until Save)
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MemberSince { get; set; } = string.Empty;
    public int TotalBookings { get; set; }

    // Personal Information: the saved values, or what the customer typed when the server sent the form back
    public CustomerProfileForm Form { get; set; } = new();
    public IReadOnlyDictionary<string, string> FieldErrors { get; set; } = new Dictionary<string, string>();
    public string? FormError { get; set; }

    // Saved Address
    public string? SavedAddress { get; set; }
    public CustomerAddressForm AddressForm { get; set; } = new();
    public string? AddressError { get; set; }

    // Change Password (the typed passwords are never sent back to the page)
    public IReadOnlyDictionary<string, string> PasswordErrors { get; set; } = new Dictionary<string, string>();
    public string? PasswordFormError { get; set; }

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }

    public bool PersonalHasProblems => FieldErrors.Count > 0 || !string.IsNullOrEmpty(FormError);
    public bool AddressHasProblem => !string.IsNullOrEmpty(AddressError);
    public bool PasswordHasProblems => PasswordErrors.Count > 0 || !string.IsNullOrEmpty(PasswordFormError);

    public string? ErrorFor(string field) => FieldErrors.TryGetValue(field, out var message) ? message : null;
    public string? PasswordErrorFor(string field) => PasswordErrors.TryGetValue(field, out var message) ? message : null;
}

/// Allowed = false when the signed-in user is not an Active customer (the controller then ends the session).
public record CustomerProfilePageResult(bool Allowed, CustomerProfilePageViewModel? Page);

/// The answer of every save on the page (personal information, address, password).
public record CustomerProfileSaveResult(
    bool Allowed,
    bool Saved,
    IReadOnlyDictionary<string, string> FieldErrors,
    string Message,
    string FullName = "",
    string Email = "");
