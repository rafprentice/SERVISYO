using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public class AuthResult
{
    public bool Succeeded { get; private init; }
    public string? Error { get; private init; }
    public User? User { get; private init; }

    public static AuthResult Ok(User user) => new() { Succeeded = true, User = user };
    public static AuthResult Fail(string error) => new() { Succeeded = false, Error = error };
}

public interface IAuthService
{
    Task<AuthResult> RegisterCustomerAsync(RegisterCustomerViewModel vm);
    Task<AuthResult> RegisterProviderAsync(RegisterProviderViewModel vm);
    Task<AuthResult> ValidateLoginAsync(string email, string password);
}

public class AuthService : IAuthService
{
    private const string EmailTaken = "That email address is already registered.";
    private const string InvalidLogin = "Invalid email or password.";

    private readonly ServisyoDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(ServisyoDbContext db, IPasswordHasher<User> hasher, ILogger<AuthService>? logger = null)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance;
    }

    public async Task<AuthResult> RegisterCustomerAsync(RegisterCustomerViewModel vm)
    {
        var problem = CheckCommon(vm)
            ?? (string.IsNullOrWhiteSpace(vm.Address) ? "Please enter your street address and barangay." : null);
        if (problem is not null) return AuthResult.Fail(problem);

        var email = NormalizeEmail(vm.Email);
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return AuthResult.Fail(EmailTaken);

        var user = BuildUser(vm, email, Roles.Customer);
        user.Customer = new Customer { Address = Clean(vm.Address), City = Clean(vm.City) };

        _db.Users.Add(user);
        return await SaveAsync(user);   // one SaveChanges = one transaction (user + customer)
    }

    public async Task<AuthResult> RegisterProviderAsync(RegisterProviderViewModel vm)
    {
        var problem = CheckCommon(vm)
            ?? (string.IsNullOrWhiteSpace(vm.MiddleName) ? "Please enter your middle name." : null)
            ?? (InputRules.HasALetter(vm.BusinessName?.Trim()) ? null : InputRules.BusinessNameMessage)
            ?? (string.IsNullOrWhiteSpace(vm.Description) ? "Please describe your services." : null)
            ?? (string.IsNullOrWhiteSpace(vm.Address) ? "Please enter your business address." : null)
            ?? (vm.BaseFee is null ? "Please enter your base fee." : null)
            ?? (vm.MinNoticeDays is < 0 or > 30 ? "Enter a whole number of days from 0 to 30." : null)
            ?? (vm.MaxJobsPerDay is < 1 or > 50 ? "Enter a whole number of jobs from 1 to 50." : null)
            ?? (vm.BaseFee is < 0 or > 1000000 ? "Enter a base fee from 0 to 1,000,000 (numbers only)." : null);
        if (problem is not null) return AuthResult.Fail(problem);

        var email = NormalizeEmail(vm.Email);
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return AuthResult.Fail(EmailTaken);

        var user = BuildUser(vm, email, Roles.ServiceProvider);
        user.Provider = new Provider
        {
            BusinessName = vm.BusinessName.Trim(),
            Description = Clean(vm.Description),
            Address = Clean(vm.Address),
            City = Clean(vm.City),
            BaseFee = vm.BaseFee,
            MinNoticeDays = vm.MinNoticeDays,
            MaxJobsPerDay = vm.MaxJobsPerDay,
            ApprovalStatus = ApprovalStatuses.Pending,   // always Pending until an admin approves
            DateApplied = DateTime.UtcNow
        };

        _db.Users.Add(user);
        var result = await SaveAsync(user);
        if (result.Succeeded) await NotifyAdminsAsync(user, user.Provider!.BusinessName);
        return result;
    }

    /// Tells every ACTIVE administrator (their bell) that a provider applied. The registration itself is already saved,
    /// so a problem here is only logged: it must never make a successful sign-up look like a failure.
    private async Task NotifyAdminsAsync(User applicant, string businessName)
    {
        try
        {
            var adminIds = await _db.Users.AsNoTracking()
                .Where(u => u.Role == Roles.Administrator && u.AccountStatus == AccountStatuses.Active)
                .Select(u => u.UserId)
                .ToListAsync();
            if (adminIds.Count == 0) return;

            var text = $"New provider application from {businessName} ({applicant.FirstName} {applicant.LastName}).";
            if (text.Length > 255) text = text[..252] + "...";

            foreach (var id in adminIds)
                _db.Notifications.Add(new Notification { UserId = id, Message = text });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not notify the administrators about the new provider application of user {UserId}.", applicant.UserId);
        }
    }

    public async Task<AuthResult> ValidateLoginAsync(string email, string password)
    {
        var normalized = NormalizeEmail(email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalized);

        if (user is null)
        {
            // Do similar work so response time does not reveal whether the email exists.
            _hasher.HashPassword(new User(), password ?? string.Empty);
            return AuthResult.Fail(InvalidLogin);
        }

        var check = _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (check == PasswordVerificationResult.Failed)
            return AuthResult.Fail(InvalidLogin);

        if (user.AccountStatus != AccountStatuses.Active)
            return AuthResult.Fail($"This account is {user.AccountStatus.ToLowerInvariant()}. Please contact support.");

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password!);
            await _db.SaveChangesAsync();
        }

        return AuthResult.Ok(user);
    }

    /// The sign-up rules again, on the server: the page checks them in the browser too, but a request can skip the page.
    private static string? CheckCommon(RegisterBaseViewModel vm)
    {
        if (!InputRules.IsName(vm.FirstName?.Trim()) || !InputRules.IsName(vm.LastName?.Trim()))
            return InputRules.NameMessage;
        // the customer page asks for one Full Name (no middle name box); the provider form requires it (checked below)
        if (!string.IsNullOrWhiteSpace(vm.MiddleName) && !InputRules.IsName(vm.MiddleName.Trim())) return InputRules.NameMessage;
        if (string.IsNullOrWhiteSpace(vm.PhoneNumber)) return "Please enter your phone number.";
        if (!InputRules.IsPhone(vm.PhoneNumber.Trim())) return InputRules.PhoneMessage;
        if ((ProfileRules.NormalizePhone(vm.PhoneNumber) ?? string.Empty).Length > 15) return "Phone number is too long.";
        if (string.IsNullOrEmpty(vm.Password) || vm.Password.Length < 8 || !System.Text.RegularExpressions.Regex.IsMatch(vm.Password, InputRules.Password))
            return "Password must be at least 8 characters, with a letter and a number.";
        if (vm.Password != vm.ConfirmPassword)
            return "The passwords do not match.";
        if (string.IsNullOrWhiteSpace(vm.Email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(vm.Email.Trim()))
            return "Please enter a valid email address.";
        return null;
    }

    private User BuildUser(RegisterBaseViewModel vm, string email, string role)
    {
        var user = new User
        {
            FirstName = vm.FirstName.Trim(),
            MiddleName = Clean(vm.MiddleName),
            LastName = vm.LastName.Trim(),
            Email = email,
            PhoneNumber = ProfileRules.NormalizePhone(vm.PhoneNumber),      // stored without spaces/hyphens: "09171234567"
            Role = role,                          // set by the server, never by the browser
            AccountStatus = AccountStatuses.Active
        };
        user.PasswordHash = _hasher.HashPassword(user, vm.Password);
        return user;
    }

    private async Task<AuthResult> SaveAsync(User user)
    {
        try
        {
            await _db.SaveChangesAsync();
            return AuthResult.Ok(user);
        }
        catch (DbUpdateException)
        {
            // Most likely two people registered the same email at the same moment
            // (the unique index on USERS.EMAIL rejected the second one).
            return AuthResult.Fail(EmailTaken);
        }
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
