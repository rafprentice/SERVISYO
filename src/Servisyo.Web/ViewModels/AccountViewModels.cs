using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Servisyo.Web.ViewModels;

/// One set of rules for every sign-up form. The same patterns run in the browser (jQuery validation) and on the
/// server (model validation + AuthService), so a form that skips the browser checks is still refused.
/// Letters include accented ones (Peñaranda, José); written without \p{L} so the browser's JavaScript understands them too.
public static class InputRules
{
    private const string L = "A-Za-zÀ-ÖØ-öø-ÿ";

    /// letters, spaces, dots, apostrophes and hyphens, with at least one letter ("Ma. Lourdes", "O'Brien", "Lapu-Lapu")
    public const string Name = "^(?=.*[" + L + "])[" + L + "\\s.'-]+$";
    public const string NameMessage = "Please enter a valid name containing letters only.";

    /// at least two words, each made of the same characters ("Juan dela Cruz")
    public const string FullName = "^\\s*[" + L + ".'-]*[" + L + "][" + L + ".'-]*(\\s+[" + L + ".'-]*[" + L + "][" + L + ".'-]*)+\\s*$";
    public const string FullNameMessage = "Please enter your first and last name using letters only.";

    /// digits, spaces and hyphens, optional leading +, 7 to 15 characters ("0917 555 1234", "+63-917-555-1234")
    public const string Phone = "^\\+?[0-9\\s-]{7,15}$";
    public const string PhoneMessage = "Phone number can only contain digits and valid phone symbols.";

    /// a business name may have numbers ("A1 Plumbing") but must have at least one letter
    public const string HasLetter = "^(?=.*[" + L + "]).+$";
    public const string BusinessNameMessage = "The business name must contain letters.";

    public const string Password = "^(?=.*[A-Za-z])(?=.*\\d).+$";
    public const string PasswordMessage = "Password must contain at least one letter and one number.";

    public static bool IsName(string? v) => v is not null && System.Text.RegularExpressions.Regex.IsMatch(v, Name);
    public static bool IsFullName(string? v) => v is not null && System.Text.RegularExpressions.Regex.IsMatch(v, FullName);
    public static bool IsPhone(string? v) => v is not null && System.Text.RegularExpressions.Regex.IsMatch(v, Phone);
    public static bool HasALetter(string? v) => v is not null && System.Text.RegularExpressions.Regex.IsMatch(v, HasLetter);
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Please enter your email address."), EmailAddress(ErrorMessage = "Please enter a valid email address."), StringLength(155)]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your password."), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me for 30 days")]
    public bool RememberMe { get; set; }
}

public abstract class RegisterBaseViewModel
{
    [Required(ErrorMessage = "Please enter your first name."), StringLength(155), Display(Name = "First name")]
    [RegularExpression(InputRules.Name, ErrorMessage = InputRules.NameMessage)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your middle name."), StringLength(155), Display(Name = "Middle name")]
    [RegularExpression(InputRules.Name, ErrorMessage = InputRules.NameMessage)]
    public string? MiddleName { get; set; }

    [Required(ErrorMessage = "Please enter your last name."), StringLength(155), Display(Name = "Last name")]
    [RegularExpression(InputRules.Name, ErrorMessage = InputRules.NameMessage)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your email address."), EmailAddress(ErrorMessage = "Please enter a valid email address."), StringLength(155)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your phone number."), StringLength(16), Display(Name = "Phone number")]
    [RegularExpression(InputRules.Phone, ErrorMessage = InputRules.PhoneMessage)]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Please enter a password."), DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [RegularExpression(InputRules.Password, ErrorMessage = InputRules.PasswordMessage)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please re-enter your password."), DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match."), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// Used by the service layer (and its tests). The customer registration PAGE uses
/// CustomerRegistrationViewModel below, which maps onto this one.
public class RegisterCustomerViewModel : RegisterBaseViewModel
{
    [StringLength(255), Display(Name = "Address (optional)")]
    public string? Address { get; set; }

    [StringLength(255), Display(Name = "City (optional)")]
    public string? City { get; set; }
}

public class RegisterProviderViewModel : RegisterBaseViewModel
{
    [Required(ErrorMessage = "Please enter your business name."), StringLength(155), Display(Name = "Business name")]
    [RegularExpression(InputRules.HasLetter, ErrorMessage = InputRules.BusinessNameMessage)]
    public string BusinessName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please describe your services."), StringLength(255), Display(Name = "Description")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Please enter your business address."), StringLength(255), Display(Name = "Address")]
    public string? Address { get; set; }

    [StringLength(50), Display(Name = "City (optional)")]
    [RegularExpression(InputRules.Name, ErrorMessage = "Please enter a valid city name containing letters only.")]
    public string? City { get; set; }

    [Required(ErrorMessage = "Please enter your base fee.")]
    [Range(0, 1000000, ErrorMessage = "Enter a base fee from 0 to 1,000,000 (numbers only).")]
    [Display(Name = "Base fee in PHP")]
    public decimal? BaseFee { get; set; }

    [Range(0, 30, ErrorMessage = "Enter a whole number of days from 0 to 30.")]
    [Display(Name = "Minimum notice (days)")]
    public int MinNoticeDays { get; set; } = 1;

    [Range(1, 50, ErrorMessage = "Enter a whole number of jobs from 1 to 50.")]
    [Display(Name = "Maximum jobs per day")]
    public int MaxJobsPerDay { get; set; } = 5;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is true;
}

/// The form on the customer registration page (matches the design).
public class CustomerRegistrationViewModel
{
    [Required(ErrorMessage = "Please enter your full name."), StringLength(155)]
    [RegularExpression(InputRules.FullName, ErrorMessage = InputRules.FullNameMessage)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your email address."), EmailAddress(ErrorMessage = "Please enter a valid email address."), StringLength(155)]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your phone number.")]
    [StringLength(16, ErrorMessage = "Phone number is too long.")]
    [RegularExpression(InputRules.Phone, ErrorMessage = InputRules.PhoneMessage)]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Please enter your street address."), StringLength(150)]
    [Display(Name = "Street Address")]
    public string? StreetAddress { get; set; }

    [Required(ErrorMessage = "Please enter your barangay."), StringLength(100)]
    public string? Barangay { get; set; }

    [StringLength(100)]
    [RegularExpression(InputRules.Name, ErrorMessage = "Please enter a valid city name containing letters only.")]
    public string? City { get; set; }

    [Required(ErrorMessage = "Please enter a password."), DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [RegularExpression(InputRules.Password, ErrorMessage = InputRules.PasswordMessage)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please re-enter your password."), DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [MustBeTrue(ErrorMessage = "You must agree to the Terms of Service and Privacy Policy.")]
    public bool AcceptTerms { get; set; }

    /// Converts the page form into the model the service layer expects.
    /// The database stores first / last name separately and has no Barangay column,
    /// so: first word = first name, the rest = last name; barangay is appended to the address.
    public RegisterCustomerViewModel ToServiceModel()
    {
        var words = Regex.Split(FullName.Trim(), @"\s+");
        var address = string.Join(", ",
            new[] { StreetAddress, Barangay }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

        return new RegisterCustomerViewModel
        {
            FirstName = words[0],
            LastName = string.Join(' ', words.Skip(1)),
            Email = Email,
            PhoneNumber = string.IsNullOrWhiteSpace(PhoneNumber) ? null : PhoneNumber.Trim(),
            Address = address.Length == 0 ? null : address,
            City = string.IsNullOrWhiteSpace(City) ? null : City.Trim(),
            Password = Password,
            ConfirmPassword = ConfirmPassword
        };
    }
}
