using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class PasswordRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc1")]                 // too short
    [InlineData("abcdefgh")]             // no number
    [InlineData("12345678")]             // no letter
    public void Check_RejectsWeakPasswords(string? password)
        => Assert.NotNull(PasswordRules.Check(password));

    [Theory]
    [InlineData("abcdefg1")]
    [InlineData("Passw0rd!")]
    public void Check_AcceptsLetterAndNumber(string password)
        => Assert.Null(PasswordRules.Check(password));

    [Fact]
    public void Check_Limits_Are8And100()
    {
        Assert.NotNull(PasswordRules.Check("abcdef1"));                          // 7
        Assert.Null(PasswordRules.Check("abcdefg1"));                            // 8
        Assert.Null(PasswordRules.Check(new string('a', 99) + "1"));             // 100
        Assert.NotNull(PasswordRules.Check(new string('a', 100) + "1"));         // 101
    }
}

public class CustomerProfileServiceTests
{
    private static (CustomerProfileService svc, ServisyoDbContext db, PasswordHasher<User> hasher) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        var hasher = new PasswordHasher<User>();
        return (new CustomerProfileService(db, hasher), db, hasher);
    }

    private static async Task<Customer> AddCustomerAsync(ServisyoDbContext db, PasswordHasher<User> hasher,
        string first = "Ana", string last = "Reyes", string? email = null, string status = AccountStatuses.Active,
        string? phone = "09171112222", string? city = "Quezon City", string? address = "45 Mabini St, Quezon City",
        string password = "OldPass1", DateTime? createdUtc = null)
    {
        var user = new User
        {
            FirstName = first, LastName = last, Email = email ?? $"{Guid.NewGuid():N}@test.com",
            Role = Roles.Customer, AccountStatus = status, PhoneNumber = phone,
            CreatedAt = createdUtc ?? new DateTime(2024, 1, 12, 4, 0, 0)
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        var c = new Customer { User = user, City = city, Address = address };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static async Task<User> AddProviderUserAsync(ServisyoDbContext db)
    {
        var p = new Provider
        {
            BusinessName = "Works", ApprovalStatus = ApprovalStatuses.Approved,
            User = new User { FirstName = "Marco", LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = Roles.ServiceProvider }
        };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p.User;
    }

    private static CustomerProfileForm FormFrom(Customer c, Action<CustomerProfileForm>? change = null)
    {
        var f = new CustomerProfileForm
        {
            FullName = $"{c.User.FirstName} {c.User.LastName}", Email = c.User.Email,
            PhoneNumber = c.User.PhoneNumber, City = c.City
        };
        change?.Invoke(f);
        return f;
    }

    // ------------------------------------------------------------------ who may use the page

    [Fact]
    public async Task Get_AdministratorsProvidersAndInactiveCustomers_AreNotAllowed()
    {
        var (svc, db, hasher) = Create();
        var provider = await AddProviderUserAsync(db);
        var suspended = await AddCustomerAsync(db, hasher, status: AccountStatuses.Suspended);
        var deactivated = await AddCustomerAsync(db, hasher, status: AccountStatuses.Deactivated);

        Assert.False((await svc.GetAsync(provider.UserId)).Allowed);
        Assert.False((await svc.GetAsync(suspended.UserId)).Allowed);
        Assert.False((await svc.GetAsync(deactivated.UserId)).Allowed);
        Assert.False((await svc.GetAsync(9999)).Allowed);
    }

    [Fact]
    public async Task EverySave_IsRefusedForAnAccountThatIsNotAnActiveCustomer()
    {
        var (svc, db, hasher) = Create();
        var provider = await AddProviderUserAsync(db);
        var suspended = await AddCustomerAsync(db, hasher, status: AccountStatuses.Suspended);

        foreach (var id in new[] { provider.UserId, suspended.UserId, 9999 })
        {
            Assert.False((await svc.SaveAsync(id, new CustomerProfileForm { FullName = "A B", Email = "a@b.com" })).Allowed);
            Assert.False((await svc.SaveAddressAsync(id, new CustomerAddressForm { Address = "x" })).Allowed);
            Assert.False((await svc.ChangePasswordAsync(id, new CustomerPasswordForm { CurrentPassword = "OldPass1", NewPassword = "NewPass1", ConfirmPassword = "NewPass1" })).Allowed);
        }
    }

    // ------------------------------------------------------------------ the page

    [Fact]
    public async Task Get_ShowsTheSavedDetails_AndOnlyThisCustomersBookingCount()
    {
        var (svc, db, hasher) = Create();
        var ana = await AddCustomerAsync(db, hasher, email: "ana@test.com");
        var ben = await AddCustomerAsync(db, hasher, "Ben", "Cruz");
        var provider = new Provider { BusinessName = "W", User = new User { FirstName = "M", LastName = "R", Email = "m@test.com", PasswordHash = "x", Role = Roles.ServiceProvider } };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        foreach (var owner in new[] { ana, ana, ana, ben })
            db.Bookings.Add(new Booking { CustomerId = owner.CustomerId, ProviderId = provider.ProviderId, BookingDate = new DateOnly(2026, 10, 20), BookingTime = new TimeOnly(9, 0), TotalDurationMinutes = 60, TotalFee = 1m, Status = BookingStatuses.Pending, RowVersion = new byte[] { 1 } });
        await db.SaveChangesAsync();

        var page = (await svc.GetAsync(ana.UserId)).Page!;

        Assert.Equal("Ana Reyes", page.FullName);
        Assert.Equal("AR", page.Initials);
        Assert.Equal("ana@test.com", page.Email);
        Assert.Equal("Jan 12, 2024", page.MemberSince);
        Assert.Equal(3, page.TotalBookings);
        Assert.Equal("45 Mabini St, Quezon City", page.SavedAddress);
        Assert.Equal("09171112222", page.Form.PhoneNumber);
        Assert.Equal("Quezon City", page.Form.City);
        Assert.Equal("Customer", page.Shell.Role);
        Assert.Equal("Customer Account", page.Shell.Subtitle);
    }

    [Fact]
    public async Task Get_MemberSinceUsesManilaTime()
    {
        var (svc, db, hasher) = Create();
        // 17:00 UTC on 11 Jan is already 12 Jan in Manila
        var c = await AddCustomerAsync(db, hasher, createdUtc: new DateTime(2024, 1, 11, 17, 0, 0));
        Assert.Equal("Jan 12, 2024", (await svc.GetAsync(c.UserId)).Page!.MemberSince);
    }

    [Fact]
    public async Task Get_WithNoAddress_HasNoSavedAddress()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, address: "   ");
        Assert.Null((await svc.GetAsync(c.UserId)).Page!.SavedAddress);
    }

    // ------------------------------------------------------------------ personal information

    [Fact]
    public async Task Save_ChangesNameEmailPhoneAndCity()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAsync(c.UserId, new CustomerProfileForm
        {
            FullName = "  Ana   Maria Reyes ", Email = "  ANA.New@Test.com ", PhoneNumber = "+63 917 555 1234", City = " Cebu City "
        });

        Assert.True(result.Saved);
        Assert.Equal("Ana Maria Reyes", result.FullName);
        Assert.Equal("ana.new@test.com", result.Email);

        var user = await db.Users.SingleAsync(u => u.UserId == c.UserId);
        Assert.Equal("Ana", user.FirstName);
        Assert.Equal("Maria Reyes", user.LastName);
        Assert.Equal("ana.new@test.com", user.Email);
        Assert.Equal("+639175551234", user.PhoneNumber);
        Assert.Equal("Cebu City", (await db.Customers.SingleAsync()).City);
    }

    [Fact]
    public async Task Save_WithoutChanges_KeepsAMultiWordLastNameAndAnOldPhoneStyle()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, "Juan", "Dela Cruz", phone: "0917-111-2222");

        var result = await svc.SaveAsync(c.UserId, FormFrom(c));

        Assert.True(result.Saved);
        var user = await db.Users.SingleAsync();
        Assert.Equal("Juan", user.FirstName);
        Assert.Equal("Dela Cruz", user.LastName);
        Assert.Equal("0917-111-2222", user.PhoneNumber);        // not rewritten when the customer did not touch it
    }

    [Fact]
    public async Task Save_BlankPhoneAndCity_ClearThem()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAsync(c.UserId, FormFrom(c, f => { f.PhoneNumber = "  "; f.City = ""; }));

        Assert.True(result.Saved);
        Assert.Null((await db.Users.SingleAsync()).PhoneNumber);
        Assert.Null((await db.Customers.SingleAsync()).City);
    }

    [Fact]
    public async Task Save_KeepsTheMiddleName()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);
        c.User.MiddleName = "Santos";
        await db.SaveChangesAsync();

        await svc.SaveAsync(c.UserId, FormFrom(c, f => f.FullName = "Anna Reyes"));

        var user = await db.Users.SingleAsync();
        Assert.Equal("Anna", user.FirstName);
        Assert.Equal("Santos", user.MiddleName);
    }

    [Theory]
    [InlineData("FullName", "", "Enter your full name.")]
    [InlineData("FullName", "Ana", "Enter your first and last name.")]
    [InlineData("Email", "", "Enter your email address.")]
    [InlineData("Email", "not-an-email", "Enter a valid email address.")]
    [InlineData("Email", "two words@test.com", "Enter a valid email address.")]
    [InlineData("PhoneNumber", "abc123", "Enter a valid phone number, for example +63 917 555 1234.")]
    public async Task Save_RefusesBadInput_AndSavesNothing(string field, string value, string message)
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, email: "ana@test.com");

        var form = FormFrom(c, f =>
        {
            switch (field)
            {
                case "FullName": f.FullName = value; break;
                case "Email": f.Email = value; break;
                case "PhoneNumber": f.PhoneNumber = value; break;
            }
            f.City = "Somewhere Else";                      // would be saved if anything were
        });

        var result = await svc.SaveAsync(c.UserId, form);

        Assert.True(result.Allowed);
        Assert.False(result.Saved);
        Assert.Equal(message, result.FieldErrors[field]);
        Assert.Equal("Please fix the highlighted fields.", result.Message);
        Assert.Equal("Quezon City", (await db.Customers.SingleAsync()).City);       // nothing at all was saved
        Assert.Equal("ana@test.com", (await db.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task Save_RefusesTooLongValues()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAsync(c.UserId, FormFrom(c, f =>
        {
            f.FullName = new string('a', 100) + " " + new string('b', 100);
            f.City = new string('c', ProfileRules.MaxCityLength + 1);
        }));

        Assert.False(result.Saved);
        Assert.True(result.FieldErrors.ContainsKey("FullName"));
        Assert.True(result.FieldErrors.ContainsKey("City"));
    }

    [Fact]
    public async Task Save_RefusesAnEmailThatAnotherAccountUses_ButAllowsYourOwn()
    {
        var (svc, db, hasher) = Create();
        var ana = await AddCustomerAsync(db, hasher, email: "ana@test.com");
        await AddCustomerAsync(db, hasher, "Ben", "Cruz", email: "ben@test.com");

        var taken = await svc.SaveAsync(ana.UserId, FormFrom(ana, f => f.Email = "BEN@test.com"));
        Assert.False(taken.Saved);
        Assert.Equal("That email address is already used by another account.", taken.FieldErrors["Email"]);

        var own = await svc.SaveAsync(ana.UserId, FormFrom(ana, f => f.Email = "Ana@Test.com"));
        Assert.True(own.Saved);
    }

    [Fact]
    public async Task Save_OnlyChangesTheSignedInCustomer()
    {
        var (svc, db, hasher) = Create();
        var ana = await AddCustomerAsync(db, hasher, email: "ana@test.com");
        var ben = await AddCustomerAsync(db, hasher, "Ben", "Cruz", email: "ben@test.com", city: "Pasig City");

        await svc.SaveAsync(ana.UserId, FormFrom(ana, f => { f.FullName = "Anna Reyes"; f.City = "Cebu City"; }));

        var benUser = await db.Users.SingleAsync(u => u.UserId == ben.UserId);
        Assert.Equal("Ben", benUser.FirstName);
        Assert.Equal("Pasig City", (await db.Customers.SingleAsync(x => x.CustomerId == ben.CustomerId)).City);
    }

    // ------------------------------------------------------------------ saved address

    [Fact]
    public async Task SaveAddress_TrimsAndSaves()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, address: null);

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = "  100 Ortigas Ave, Pasig City  " });

        Assert.True(result.Saved);
        Assert.Equal("Your address was saved.", result.Message);
        Assert.Equal("100 Ortigas Ave, Pasig City", (await db.Customers.SingleAsync()).Address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAddress_Blank_RemovesTheSavedAddress(string? blank)
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = blank });

        Assert.True(result.Saved);
        Assert.Equal("Your saved address was removed.", result.Message);
        Assert.Null((await db.Customers.SingleAsync()).Address);
    }

    [Fact]
    public async Task SaveAddress_TooLong_IsRefused_AndKeepsTheOldAddress()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = new string('x', ProfileRules.MaxAddressLength + 1) });

        Assert.False(result.Saved);
        Assert.True(result.FieldErrors.ContainsKey("Address"));
        Assert.Equal("45 Mabini St, Quezon City", (await db.Customers.SingleAsync()).Address);
    }

    [Fact]
    public async Task SaveAddress_OnlyChangesTheSignedInCustomer()
    {
        var (svc, db, hasher) = Create();
        var ana = await AddCustomerAsync(db, hasher);
        var ben = await AddCustomerAsync(db, hasher, "Ben", "Cruz", address: "Ben's house");

        await svc.SaveAddressAsync(ana.UserId, new CustomerAddressForm { Address = null });

        Assert.Equal("Ben's house", (await db.Customers.SingleAsync(x => x.CustomerId == ben.CustomerId)).Address);
    }

    // ------------------------------------------------------------------ password

    private static CustomerPasswordForm Pw(string current, string next, string? confirm = null)
        => new() { CurrentPassword = current, NewPassword = next, ConfirmPassword = confirm ?? next };

    [Fact]
    public async Task ChangePassword_Success_StoresAHashOfTheNewPasswordOnly()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, password: "OldPass1");

        var result = await svc.ChangePasswordAsync(c.UserId, Pw("OldPass1", "NewPass22"));

        Assert.True(result.Saved);
        Assert.Equal("Your password was changed.", result.Message);

        var user = await db.Users.SingleAsync();
        Assert.NotEqual("NewPass22", user.PasswordHash);                                  // never plain text
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, user.PasswordHash, "NewPass22"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, user.PasswordHash, "OldPass1"));
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_IsRefused_AndNothingChanges()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, password: "OldPass1");
        var before = c.User.PasswordHash;

        var result = await svc.ChangePasswordAsync(c.UserId, Pw("WrongPass1", "NewPass22"));

        Assert.False(result.Saved);
        Assert.Equal("Your current password is incorrect.", result.FieldErrors["CurrentPassword"]);
        Assert.Equal(before, (await db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task ChangePassword_MissingFields_AreAllReported()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.ChangePasswordAsync(c.UserId, new CustomerPasswordForm());

        Assert.False(result.Saved);
        Assert.Equal("Enter your current password.", result.FieldErrors["CurrentPassword"]);
        Assert.Equal("Enter a new password.", result.FieldErrors["NewPassword"]);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("nodigitshere")]
    [InlineData("12345678")]
    public async Task ChangePassword_WeakNewPassword_IsRefused(string weak)
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, password: "OldPass1");

        var result = await svc.ChangePasswordAsync(c.UserId, Pw("OldPass1", weak));

        Assert.False(result.Saved);
        Assert.True(result.FieldErrors.ContainsKey("NewPassword"));
    }

    [Fact]
    public async Task ChangePassword_SameAsCurrent_IsRefused()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, password: "OldPass1");

        var result = await svc.ChangePasswordAsync(c.UserId, Pw("OldPass1", "OldPass1"));

        Assert.False(result.Saved);
        Assert.Equal("Choose a new password that is different from your current one.", result.FieldErrors["NewPassword"]);
    }

    [Fact]
    public async Task ChangePassword_ConfirmationMustMatch()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, password: "OldPass1");

        var result = await svc.ChangePasswordAsync(c.UserId, Pw("OldPass1", "NewPass22", "NewPass23"));

        Assert.False(result.Saved);
        Assert.Equal("The passwords do not match.", result.FieldErrors["ConfirmPassword"]);
    }

    [Fact]
    public async Task ChangePassword_OnlyChangesTheSignedInCustomer()
    {
        var (svc, db, hasher) = Create();
        var ana = await AddCustomerAsync(db, hasher, password: "OldPass1");
        var ben = await AddCustomerAsync(db, hasher, "Ben", "Cruz", password: "BenPass1");
        var benHash = ben.User.PasswordHash;

        await svc.ChangePasswordAsync(ana.UserId, Pw("OldPass1", "NewPass22"));

        Assert.Equal(benHash, (await db.Users.SingleAsync(u => u.UserId == ben.UserId)).PasswordHash);
    }
}
