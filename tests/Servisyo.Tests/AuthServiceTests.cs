using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class AuthServiceTests
{
    private static (AuthService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new AuthService(db, new PasswordHasher<User>()), db);
    }

    private static RegisterCustomerViewModel Customer(string email = "maria@email.com") => new()
    {
        FirstName = "Maria", MiddleName = "Lopez", LastName = "Santos", Email = email, PhoneNumber = "09171234567",
        Address = "123 Rizal St., Brgy. Lahug", Password = "Passw0rd1", ConfirmPassword = "Passw0rd1"
    };

    [Fact]
    public async Task RegisterCustomer_CreatesCustomerWithHashedPassword()
    {
        var (svc, db) = Create();
        var result = await svc.RegisterCustomerAsync(Customer());

        Assert.True(result.Succeeded);
        var user = await db.Users.Include(u => u.Customer).SingleAsync();
        Assert.Equal(Roles.Customer, user.Role);
        Assert.NotNull(user.Customer);
        Assert.NotEqual("Passw0rd1", user.PasswordHash);
    }

    [Fact]
    public async Task RegisterCustomer_DuplicateEmail_IsRejected_CaseInsensitive()
    {
        var (svc, db) = Create();
        await svc.RegisterCustomerAsync(Customer("maria@email.com"));
        var second = await svc.RegisterCustomerAsync(Customer("  MARIA@email.com "));

        Assert.False(second.Succeeded);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task RegisterProvider_StartsPending()
    {
        var (svc, db) = Create();
        var result = await svc.RegisterProviderAsync(new RegisterProviderViewModel
        {
            FirstName = "Juan", MiddleName = "Reyes", LastName = "Dela Cruz", Email = "juan@email.com", PhoneNumber = "09171234567",
            Password = "Passw0rd1", ConfirmPassword = "Passw0rd1",
            BusinessName = "Juan Plumbing Services", Description = "Pipe repair and installation.", Address = "Lahug, Cebu City", BaseFee = 500m
        });

        Assert.True(result.Succeeded);
        var user = await db.Users.Include(u => u.Provider).SingleAsync();
        Assert.Equal(Roles.ServiceProvider, user.Role);
        Assert.Equal(ApprovalStatuses.Pending, user.Provider!.ApprovalStatus);
    }

    [Fact]
    public async Task Login_WrongPassword_Fails()
    {
        var (svc, _) = Create();
        await svc.RegisterCustomerAsync(Customer());
        var result = await svc.ValidateLoginAsync("maria@email.com", "WrongPass1");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Login_CorrectPassword_Succeeds()
    {
        var (svc, _) = Create();
        await svc.RegisterCustomerAsync(Customer());
        var result = await svc.ValidateLoginAsync("MARIA@email.com", "Passw0rd1");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Login_SuspendedAccount_IsBlocked()
    {
        var (svc, db) = Create();
        await svc.RegisterCustomerAsync(Customer());
        var user = await db.Users.SingleAsync();
        user.AccountStatus = AccountStatuses.Suspended;
        await db.SaveChangesAsync();

        var result = await svc.ValidateLoginAsync("maria@email.com", "Passw0rd1");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Login_UnknownEmail_Fails()
    {
        var (svc, _) = Create();
        var result = await svc.ValidateLoginAsync("nobody@email.com", "Passw0rd1");
        Assert.False(result.Succeeded);
    }
}
