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

    public AuthService(ServisyoDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<AuthResult> RegisterCustomerAsync(RegisterCustomerViewModel vm)
    {
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
        return await SaveAsync(user);
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

    private User BuildUser(RegisterBaseViewModel vm, string email, string role)
    {
        var user = new User
        {
            FirstName = vm.FirstName.Trim(),
            MiddleName = Clean(vm.MiddleName),
            LastName = vm.LastName.Trim(),
            Email = email,
            PhoneNumber = Clean(vm.PhoneNumber),
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
