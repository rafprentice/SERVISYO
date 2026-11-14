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

    /// Saves the customer's address: one of the 5 Cebu City options (CebuLocations). A saved address can be changed
    /// to another option but never removed: a blank value is refused. Used by the My Profile page AND by
    /// PUT /api/profile/address, so both follow exactly the same rules.
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

    /// The profile lock (CustomerProfileLock): nothing is written while the customer has a booking that is not finished.
    private static CustomerProfileSaveResult Locked() => new(true, false, NoErrors, CustomerProfileLock.SaveRefusedMessage, Locked: true);

    // ------------------------------------------------------------------ read

    public async Task<CustomerProfilePageResult> GetAsync(int userId, CancellationToken ct = default)
    {
        var c = await _db.Customers.AsNoTracking()
            .Where(x => x.UserId == userId
                     && x.User.Role == Roles.Customer
                     && x.User.AccountStatus == AccountStatuses.Active)
            .Select(x => new
            {
                x.CustomerId, x.Address, x.ProfilePhoto,
                x.User.FirstName, x.User.LastName, x.User.Email, x.User.PhoneNumber, x.User.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
        if (c is null) return new(false, null);

        var fullName = $"{c.FirstName} {c.LastName}".Trim();
        var address = string.IsNullOrWhiteSpace(c.Address) ? null : c.Address.Trim();
        var hasActive = await _db.CustomerHasActiveBookingAsync(c.CustomerId, ct);

        var vm = new CustomerProfilePageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Customer",
                Subtitle = "Customer Account",
                FirstName = c.FirstName,
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                UnreadNotifications = await _db.CountUnreadNotificationsAsync(userId, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            FullName = fullName,
            Initials = NameFormat.Initials(fullName),
            PhotoUrl = string.IsNullOrWhiteSpace(c.ProfilePhoto) ? null : c.ProfilePhoto,
            HasActiveBooking = hasActive,
            ActiveBookingsCount = hasActive ? await _db.CountCustomerActiveBookingsAsync(c.CustomerId, ct) : 0,
            Email = c.Email,
            // stored in UTC, shown in Philippine time
            MemberSince = c.CreatedAt.AddHours(ManilaOffsetHours).ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
            TotalBookings = await _db.Bookings.CountAsync(b => b.CustomerId == c.CustomerId, ct),
            Form = new CustomerProfileForm { FullName = fullName, Email = c.Email, PhoneNumber = c.PhoneNumber },
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

        // the profile lock comes first: with a booking that is not finished nothing is changed (not even the phone)
        if (await _db.CustomerHasActiveBookingAsync(customer.CustomerId, ct))
        {
            _logger.LogWarning("Customer {CustomerId} tried to change their personal information during an active booking: refused.", customer.CustomerId);
            return Locked();
        }

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
        else if (nameChanged && !InputRules.IsFullName(fullName))
            errors["FullName"] = InputRules.FullNameMessage;                    // letters only, no numbers or symbols

        // ---- phone (REQUIRED: the service provider must be able to contact the customer) ----
        // Only 09XXXXXXXXX (11 characters) or +639XXXXXXXXX (13 characters), digits only; it is always saved as
        // 09XXXXXXXXX. A blank box is refused, so a saved number can be changed but never removed.
        var typedPhone = (form.PhoneNumber ?? string.Empty).Trim();
        if (typedPhone.Length == 0)
            errors["PhoneNumber"] = InputRules.PhoneRequiredMessage;
        else if (typedPhone.Length > ProfileRules.MaxPhoneLength || !InputRules.IsPhone(typedPhone))
            errors["PhoneNumber"] = InputRules.PhoneMessage;                     // e.g. 09998384283842833248 is refused
        var phone = ProfileRules.NormalizePhone(typedPhone);                     // +639171234567 is saved as 09171234567
        var phoneChanged = !string.Equals(phone, user.PhoneNumber, StringComparison.Ordinal);

        // ---- email (it is also the sign-in name, so it must stay unique) ----
        var email = (form.Email ?? string.Empty).Trim().ToLowerInvariant();
        var emailChanged = !string.Equals(email, user.Email.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        if (email.Length == 0)
            errors["Email"] = "Enter your email address.";
        else if (emailChanged && email.Length > ProfileRules.MaxEmailLength)
            errors["Email"] = $"The email can be at most {ProfileRules.MaxEmailLength} characters.";
        else if (emailChanged && !InputRules.IsEmail(email))
            errors["Email"] = InputRules.EmailMessage;                           // no numbers after the @, parts of 2+ letters
        else if (emailChanged && await EmailUsedByAnotherAsync(email, user.UserId, ct))
            errors["Email"] = EmailTaken;

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

        // the profile lock: the provider is on the way to (or working at) the saved address
        if (await _db.CustomerHasActiveBookingAsync(customer.CustomerId, ct))
        {
            _logger.LogWarning("Customer {CustomerId} tried to change their saved address during an active booking: refused.", customer.CustomerId);
            return Locked();
        }

        var hadAddress = !string.IsNullOrWhiteSpace(customer.Address);

        // a blank value would remove the address: refused once one is saved (older accounts with none are asked to choose)
        if (string.IsNullOrWhiteSpace(form.Address))
        {
            if (hadAddress)
                _logger.LogWarning("Customer {CustomerId} tried to remove their saved address: refused.", customer.CustomerId);
            return Problem(hadAddress ? SavedAddressRules.CannotRemoveMessage : CebuLocations.NotInListMessage);
        }

        // only the exact text of one of the 5 options (anything typed or changed in the browser is refused)
        var chosen = CebuLocations.Find(form.Address);
        if (chosen is null) return Problem(CebuLocations.NotInListMessage);

        customer.Address = chosen.Full;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Customer {CustomerId} {Action} their saved address.", customer.CustomerId, hadAddress ? "changed" : "added");
        return new(true, true, NoErrors, hadAddress ? "Your address was updated." : "Your address was saved.", Address: chosen.Full);

        static CustomerProfileSaveResult Problem(string message) =>
            new(true, false, new Dictionary<string, string> { ["Address"] = message }, message);
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
