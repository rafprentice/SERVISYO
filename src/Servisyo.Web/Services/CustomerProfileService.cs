using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface ICustomerProfileService
{
    /// The My Profile page for the signed-in customer. Allowed = false when that user is not an Active customer.
    Task<CustomerProfilePageResult> GetAsync(int userId, CancellationToken ct = default);

    /// Saves the Personal Information form. Everything is checked first; if anything is wrong nothing at all is saved.
    Task<CustomerProfileSaveResult> SaveAsync(int userId, CustomerProfileForm form, CancellationToken ct = default);

    /// Saves (or, when the text is blank, removes) the customer's saved address.
    Task<CustomerProfileSaveResult> SaveAddressAsync(int userId, CustomerAddressForm form, CancellationToken ct = default);

    /// Changes the password after checking the current one.
    Task<CustomerProfileSaveResult> ChangePasswordAsync(int userId, CustomerPasswordForm form, CancellationToken ct = default);
}

public class CustomerProfileService : ICustomerProfileService
{
    private const int ManilaOffsetHours = 8;          // the Philippines has no daylight saving
    private const string EmailTaken = "That email address is already used by another account.";
    private const string FixFields = "Please fix the highlighted fields.";

    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();

    private readonly ServisyoDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ILogger<CustomerProfileService> _logger;

    public CustomerProfileService(ServisyoDbContext db, IPasswordHasher<User> hasher, ILogger<CustomerProfileService>? logger = null)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CustomerProfileService>.Instance;
    }

    /// The customer (with the user account that belongs to it) is found from the SIGNED-IN user id only,
    /// never from the browser. Only an Active customer account may use the page.
    private Task<Customer?> FindAsync(int userId, CancellationToken ct) =>
        _db.Customers.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.UserId == userId
                                   && c.User.Role == Roles.Customer
                                   && c.User.AccountStatus == AccountStatuses.Active, ct);

    private static CustomerProfileSaveResult NotAllowed() => new(false, false, NoErrors, string.Empty);

    // ------------------------------------------------------------------ read

    public async Task<CustomerProfilePageResult> GetAsync(int userId, CancellationToken ct = default)
    {
        var c = await _db.Customers.AsNoTracking()
            .Where(x => x.UserId == userId
                     && x.User.Role == Roles.Customer
                     && x.User.AccountStatus == AccountStatuses.Active)
            .Select(x => new
            {
                x.CustomerId, x.City, x.Address, x.ProfilePhoto,
                x.User.FirstName, x.User.LastName, x.User.Email, x.User.PhoneNumber, x.User.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
        if (c is null) return new(false, null);

        var fullName = $"{c.FirstName} {c.LastName}".Trim();
        var address = string.IsNullOrWhiteSpace(c.Address) ? null : c.Address.Trim();

        var vm = new CustomerProfilePageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Customer",
                Subtitle = "Customer Account",
                FirstName = c.FirstName,
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            FullName = fullName,
            Initials = NameFormat.Initials(fullName),
            PhotoUrl = string.IsNullOrWhiteSpace(c.ProfilePhoto) ? null : c.ProfilePhoto,
            NameLocked = await _db.Bookings.AnyAsync(b => b.CustomerId == c.CustomerId && ActiveBookingRules.Statuses.Contains(b.Status), ct),
            Email = c.Email,
            // stored in UTC, shown in Philippine time
            MemberSince = c.CreatedAt.AddHours(ManilaOffsetHours).ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
            TotalBookings = await _db.Bookings.CountAsync(b => b.CustomerId == c.CustomerId, ct),
            Form = new CustomerProfileForm { FullName = fullName, Email = c.Email, PhoneNumber = c.PhoneNumber, City = c.City },
            SavedAddress = address,
            AddressForm = new CustomerAddressForm { Address = address }
        };

        return new(true, vm);
    }

    // ----------------------------------------------------------------- personal information

    public async Task<CustomerProfileSaveResult> SaveAsync(int userId, CustomerProfileForm form, CancellationToken ct = default)
    {
        var customer = await FindAsync(userId, ct);
        if (customer is null) return NotAllowed();

        var user = customer.User;
        var errors = new Dictionary<string, string>();

        // ---- full name (stored as first + last name; only re-split when the customer actually changed it) ----
        var fullName = ProfileRules.NormalizeName(form.FullName);
        var currentName = ProfileRules.NormalizeName($"{user.FirstName} {user.LastName}");
        var nameChanged = !string.Equals(fullName, currentName, StringComparison.Ordinal);
        if (fullName.Length == 0)
            errors["FullName"] = "Enter your full name.";
        else if (nameChanged && fullName.Length > ProfileRules.MaxFullNameLength)
            errors["FullName"] = $"The name can be at most {ProfileRules.MaxFullNameLength} characters.";
        else if (nameChanged && !fullName.Contains(' '))
            errors["FullName"] = "Enter your first and last name.";
        else if (nameChanged && await _db.Bookings.AnyAsync(b => b.CustomerId == customer.CustomerId && ActiveBookingRules.Statuses.Contains(b.Status), ct))
            errors["FullName"] = ActiveBookingRules.NameLockedMessage;       // the provider must keep seeing the name they were booked by

        // ---- phone (optional; a number saved earlier in another style is left alone unless it is edited) ----
        var typedPhone = (form.PhoneNumber ?? string.Empty).Trim();
        var phoneChanged = typedPhone != (user.PhoneNumber ?? string.Empty).Trim();
        var phone = ProfileRules.NormalizePhone(typedPhone);
        if (phoneChanged && phone is not null && !ProfileRules.IsValidPhone(phone))
            errors["PhoneNumber"] = "Enter a valid phone number, for example +63 917 555 1234.";

        // ---- email (it is also the sign-in name, so it must stay unique) ----
        var email = (form.Email ?? string.Empty).Trim().ToLowerInvariant();
        var emailChanged = !string.Equals(email, user.Email.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        if (email.Length == 0)
            errors["Email"] = "Enter your email address.";
        else if (emailChanged && email.Length > ProfileRules.MaxEmailLength)
            errors["Email"] = $"The email can be at most {ProfileRules.MaxEmailLength} characters.";
        else if (emailChanged && (email.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(email)))
            errors["Email"] = "Enter a valid email address.";
        else if (emailChanged && await EmailUsedByAnotherAsync(email, user.UserId, ct))
            errors["Email"] = EmailTaken;

        // ---- city (optional) ----
        var city = ProfileRules.NormalizeText(form.City);
        if (city is { Length: > ProfileRules.MaxCityLength })
            errors["City"] = $"The city can be at most {ProfileRules.MaxCityLength} characters.";

        if (errors.Count > 0)
        {
            _logger.LogInformation("Customer {CustomerId}: profile not saved, {Count} field(s) need attention.", customer.CustomerId, errors.Count);
            return new(true, false, errors, FixFields);
        }

        // ---- everything is fine: change the account and the customer profile together ----
        if (nameChanged)
        {
            var (first, last) = ProfileRules.SplitName(fullName);
            user.FirstName = first;
            user.LastName = last;               // the middle name, if there is one, is left as it was
        }
        if (emailChanged) user.Email = email;
        if (phoneChanged) user.PhoneNumber = phone;
        customer.City = city;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (emailChanged)
        {
            // someone registered the same email between our check and the save: the unique index in the database caught it
            _db.ChangeTracker.Clear();
            if (await EmailUsedByAnotherAsync(email, user.UserId, ct))
            {
                _logger.LogWarning("Customer {CustomerId}: email became taken while saving the profile.", customer.CustomerId);
                return new(true, false, new Dictionary<string, string> { ["Email"] = EmailTaken }, FixFields);
            }
            throw;
        }

        _logger.LogInformation("Customer {CustomerId} updated their profile.", customer.CustomerId);
        return new(true, true, NoErrors, "Your profile was updated.", $"{user.FirstName} {user.LastName}".Trim(), user.Email);
    }

    // ----------------------------------------------------------------- saved address

    public async Task<CustomerProfileSaveResult> SaveAddressAsync(int userId, CustomerAddressForm form, CancellationToken ct = default)
    {
        var customer = await FindAsync(userId, ct);
        if (customer is null) return NotAllowed();

        var address = ProfileRules.NormalizeText(form.Address);
        if (address is { Length: > ProfileRules.MaxAddressLength })
            return new(true, false,
                new Dictionary<string, string> { ["Address"] = $"The address can be at most {ProfileRules.MaxAddressLength} characters." },
                FixFields);

        var removing = address is null;
        customer.Address = address;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Customer {CustomerId} {Action} their saved address.", customer.CustomerId, removing ? "removed" : "saved");
        return new(true, true, NoErrors, removing ? "Your saved address was removed." : "Your address was saved.");
    }

    // ----------------------------------------------------------------- password

    public async Task<CustomerProfileSaveResult> ChangePasswordAsync(int userId, CustomerPasswordForm form, CancellationToken ct = default)
    {
        var customer = await FindAsync(userId, ct);
        if (customer is null) return NotAllowed();

        var user = customer.User;
        var errors = new Dictionary<string, string>();

        // the current password must be right (so a stolen, still-signed-in browser cannot lock the owner out)
        var current = form.CurrentPassword ?? string.Empty;
        var currentOk = false;
        if (current.Length == 0)
            errors["CurrentPassword"] = "Enter your current password.";
        else
        {
            var check = _hasher.VerifyHashedPassword(user, user.PasswordHash, current);
            currentOk = check != PasswordVerificationResult.Failed;
            if (!currentOk)
            {
                errors["CurrentPassword"] = "Your current password is incorrect.";
                _logger.LogWarning("Customer {CustomerId}: wrong current password while changing the password.", customer.CustomerId);
            }
        }

        var newPassword = form.NewPassword ?? string.Empty;
        var newProblem = PasswordRules.Check(newPassword);
        if (newProblem is not null)
            errors["NewPassword"] = newProblem;
        else if (currentOk && string.Equals(newPassword, current, StringComparison.Ordinal))
            errors["NewPassword"] = "Choose a new password that is different from your current one.";

        if (!errors.ContainsKey("NewPassword") && !string.Equals(newPassword, form.ConfirmPassword ?? string.Empty, StringComparison.Ordinal))
            errors["ConfirmPassword"] = "The passwords do not match.";

        if (errors.Count > 0) return new(true, false, errors, FixFields);

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Customer {CustomerId} changed their password.", customer.CustomerId);
        return new(true, true, NoErrors, "Your password was changed.");
    }

    private Task<bool> EmailUsedByAnotherAsync(string normalizedEmail, int userId, CancellationToken ct) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.UserId != userId && u.Email == normalizedEmail, ct);
}
