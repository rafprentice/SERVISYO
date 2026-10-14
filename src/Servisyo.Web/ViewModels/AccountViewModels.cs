using System.ComponentModel.DataAnnotations;

namespace Servisyo.Web.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress, StringLength(155)]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
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
