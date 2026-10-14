using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Servisyo.Web.ViewModels;

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
    [Required, StringLength(155), Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [StringLength(155), Display(Name = "Middle name (optional)")]
    public string? MiddleName { get; set; }

    [Required, StringLength(155), Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(155)]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(15), Display(Name = "Phone number (optional)")]
    public string? PhoneNumber { get; set; }

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Password must contain at least one letter and one number.")]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")]
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
    [Required, StringLength(155), Display(Name = "Business name")]
    public string BusinessName { get; set; } = string.Empty;

    [StringLength(255), Display(Name = "Description (optional)")]
    public string? Description { get; set; }

    [StringLength(255), Display(Name = "Address (optional)")]
    public string? Address { get; set; }

    [StringLength(50), Display(Name = "City (optional)")]
    public string? City { get; set; }

    [Range(0, 1000000), Display(Name = "Base fee in PHP (optional)")]
    public decimal? BaseFee { get; set; }

    [Range(0, 30), Display(Name = "Minimum notice (days)")]
    public int MinNoticeDays { get; set; } = 1;

    [Range(1, 50), Display(Name = "Maximum jobs per day")]
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
    [RegularExpression(@"^\S+(\s+\S+)+$", ErrorMessage = "Please enter your first and last name.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your email address."), EmailAddress(ErrorMessage = "Please enter a valid email address."), StringLength(155)]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Please enter a valid phone number."), StringLength(15)]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [StringLength(150)]
    [Display(Name = "Street Address")]
    public string? StreetAddress { get; set; }

    [StringLength(100)]
    public string? Barangay { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [Required(ErrorMessage = "Please enter a password."), DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Password must contain at least one letter and one number.")]
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
