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
        string? phone = "09171112222", string? address = "45 Mabini St, Quezon City",
        string password = "OldPass1", DateTime? createdUtc = null)
    {
        var user = new User
        {
            FirstName = first, LastName = last, Email = email ?? $"{Guid.NewGuid():N}@test.com",
            Role = Roles.Customer, AccountStatus = status, PhoneNumber = phone,
            CreatedAt = createdUtc ?? new DateTime(2024, 1, 12, 4, 0, 0)
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        var c = new Customer { User = user, Address = address };
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
            PhoneNumber = c.User.PhoneNumber
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
    public async Task Save_ChangesNameEmailAndPhone_AndSavesThePhoneAs09()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAsync(c.UserId, new CustomerProfileForm
        {
            FullName = "  Ana   Maria Reyes ", Email = "  ANA.New@Test.com ", PhoneNumber = " +639175551234 "
        });

        Assert.True(result.Saved);
        Assert.Equal("Ana Maria Reyes", result.FullName);
        Assert.Equal("ana.new@test.com", result.Email);

        var user = await db.Users.SingleAsync(u => u.UserId == c.UserId);
        Assert.Equal("Ana", user.FirstName);
        Assert.Equal("Maria Reyes", user.LastName);
        Assert.Equal("ana.new@test.com", user.Email);
        Assert.Equal("09175551234", user.PhoneNumber);          // +639... is always saved as 09...
    }

    [Fact]
    public async Task Save_WithoutChanges_KeepsAMultiWordLastNameAndThePhone()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, "Juan", "Dela Cruz");

        var result = await svc.SaveAsync(c.UserId, FormFrom(c));

        Assert.True(result.Saved);
        var user = await db.Users.SingleAsync();
        Assert.Equal("Juan", user.FirstName);
        Assert.Equal("Dela Cruz", user.LastName);
        Assert.Equal("09171112222", user.PhoneNumber);
    }

    // ---- the phone number is required (the service provider must be able to contact the customer)

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_BlankPhone_IsRefused_AndKeepsTheSavedNumber(string? blank)
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAsync(c.UserId, FormFrom(c, f => f.PhoneNumber = blank));

        Assert.False(result.Saved);
        Assert.Equal(InputRules.PhoneRequiredMessage, result.FieldErrors["PhoneNumber"]);
        Assert.Equal("09171112222", (await db.Users.SingleAsync()).PhoneNumber);
    }

    [Fact]
    public async Task Save_AnAccountWithNoPhoneYet_MustAddOne_BeforeAnythingIsSaved()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, phone: null);

        var refused = await svc.SaveAsync(c.UserId, FormFrom(c, f => f.Email = "ana.new@test.com"));
        Assert.False(refused.Saved);
        Assert.Equal(InputRules.PhoneRequiredMessage, refused.FieldErrors["PhoneNumber"]);
        Assert.NotEqual("ana.new@test.com", (await db.Users.AsNoTracking().SingleAsync()).Email);   // nothing at all was saved

        var saved = await svc.SaveAsync(c.UserId, FormFrom(c, f => { f.Email = "ana.new@test.com"; f.PhoneNumber = "09983854130"; }));
        Assert.True(saved.Saved);
        var user = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal("09983854130", user.PhoneNumber);
        Assert.Equal("ana.new@test.com", user.Email);
    }

    [Theory]
    [InlineData("09983854130", "09983854130")]                  // 11 characters
    [InlineData("+639983854130", "09983854130")]                // 13 characters, saved as 09...
    [InlineData("  09983854130  ", "09983854130")]
    public async Task Save_AcceptsBothPhoneStyles(string typed, string saved)
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAsync(c.UserId, FormFrom(c, f => f.PhoneNumber = typed));

        Assert.True(result.Saved);
        Assert.Equal(saved, (await db.Users.SingleAsync()).PhoneNumber);
    }

    [Fact]
    public async Task Save_AnOldStyleSavedNumber_MustBeCorrected()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, phone: "0917-111-2222");

        var result = await svc.SaveAsync(c.UserId, FormFrom(c));          // the old number sent back as it is

        Assert.False(result.Saved);
        Assert.Equal(InputRules.PhoneMessage, result.FieldErrors["PhoneNumber"]);
        Assert.Equal("0917-111-2222", (await db.Users.SingleAsync()).PhoneNumber);
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
    [InlineData("FullName", "Ana R3yes", InputRules.FullNameMessage)]
    [InlineData("FullName", "Ana Reyes!", InputRules.FullNameMessage)]
    [InlineData("Email", "not-an-email", InputRules.EmailMessage)]
    [InlineData("Email", "two words@test.com", InputRules.EmailMessage)]
    [InlineData("Email", "ana@gmail2.com", InputRules.EmailMessage)]      // no numbers in the domain
    [InlineData("Email", "ana@a.c", InputRules.EmailMessage)]             // domain and TLD need 2+ letters
    [InlineData("PhoneNumber", "abc123", InputRules.PhoneMessage)]
    [InlineData("PhoneNumber", "0917 555 1234", InputRules.PhoneMessage)] // no spaces
    [InlineData("PhoneNumber", "0917-555-1234", InputRules.PhoneMessage)] // no dashes
    [InlineData("PhoneNumber", "0917555123", InputRules.PhoneMessage)]    // 10 digits
    [InlineData("PhoneNumber", "08175551234", InputRules.PhoneMessage)]   // not a mobile number
    [InlineData("PhoneNumber", "09998384283842833248", InputRules.PhoneMessage)]   // 20 digits: longer than 13
    [InlineData("PhoneNumber", "+6399838541301", InputRules.PhoneMessage)] // 14 characters
    [InlineData("PhoneNumber", "099838541301", InputRules.PhoneMessage)]  // 12 digits
    [InlineData("PhoneNumber", "", InputRules.PhoneRequiredMessage)]
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
        });

        var result = await svc.SaveAsync(c.UserId, form);

        Assert.True(result.Allowed);
        Assert.False(result.Saved);
        Assert.Equal(message, result.FieldErrors[field]);
        Assert.Equal("Please fix the highlighted fields.", result.Message);
        var user = await db.Users.SingleAsync();                                   // nothing at all was saved
        Assert.Equal("ana@test.com", user.Email);
        Assert.Equal("09171112222", user.PhoneNumber);
        Assert.Equal("Ana", user.FirstName);
    }

    [Fact]
    public async Task Save_RefusesTooLongValues()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAsync(c.UserId, FormFrom(c, f =>
        {
            f.FullName = new string('a', 50) + " " + new string('b', 50);   // 101 characters: the columns hold 100
            f.Email = new string('e', 250) + "@test.com";
        }));

        Assert.False(result.Saved);
        Assert.True(result.FieldErrors.ContainsKey("FullName"));
        Assert.True(result.FieldErrors.ContainsKey("Email"));
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
        var ben = await AddCustomerAsync(db, hasher, "Ben", "Cruz", email: "ben@test.com", phone: "09179998888");

        await svc.SaveAsync(ana.UserId, FormFrom(ana, f => { f.FullName = "Anna Reyes"; f.PhoneNumber = "09170001111"; }));

        var benUser = await db.Users.SingleAsync(u => u.UserId == ben.UserId);
        Assert.Equal("Ben", benUser.FirstName);
        Assert.Equal("09179998888", benUser.PhoneNumber);
    }

    // ------------------------------------------------------------------ saved address
    // A saved address can be CHANGED to one of the 5 Cebu City options, never removed (SavedAddressRules).

    public static TheoryData<string> CebuOptions()
    {
        var data = new TheoryData<string>();
        foreach (var location in CebuLocations.All) data.Add(location.Full);
        return data;
    }

    [Theory]
    [MemberData(nameof(CebuOptions))]
    public async Task SaveAddress_AnyOfTheFiveCebuOptions_ReplacesTheSavedAddress(string option)
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);                    // saved: "45 Mabini St, Quezon City" (an older typed one)

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = option });

        Assert.True(result.Saved);
        Assert.Equal("Your address was updated.", result.Message);
        Assert.Equal(option, result.Address);
        Assert.Equal(option, (await db.Customers.SingleAsync()).Address);
    }

    [Fact]
    public async Task SaveAddress_TrimsTheChoice_AndCanAddAFirstAddress()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, address: null);

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = "  Salinas Drive, Brgy. Lahug, Cebu City  " });

        Assert.True(result.Saved);
        Assert.Equal("Your address was saved.", result.Message);
        Assert.Equal("Salinas Drive, Brgy. Lahug, Cebu City", (await db.Customers.SingleAsync()).Address);
    }

    [Fact]
    public async Task SaveAddress_CanSwitchBetweenOptions()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, address: "Osmeña Blvd, Brgy. Capitol Site, Cebu City");

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = "C. Padilla St, Brgy. Duljo Fatima, Cebu City" });

        Assert.True(result.Saved);
        Assert.Equal("C. Padilla St, Brgy. Duljo Fatima, Cebu City", (await db.Customers.SingleAsync()).Address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAddress_Blank_CannotRemoveTheSavedAddress(string? blank)
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, address: "N. Bacalso Ave, Brgy. Sambag I, Cebu City");

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = blank });

        Assert.True(result.Allowed);
        Assert.False(result.Saved);
        Assert.Equal(SavedAddressRules.CannotRemoveMessage, result.Message);
        Assert.Equal("Saved addresses cannot be removed once set. Please edit your address instead.", result.FieldErrors["Address"]);
        Assert.Equal("N. Bacalso Ave, Brgy. Sambag I, Cebu City", (await db.Customers.SingleAsync()).Address);
    }

    [Fact]
    public async Task SaveAddress_Blank_WithNoAddressYet_AsksToChooseOne()
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher, address: null);

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = "" });

        Assert.False(result.Saved);
        Assert.Equal(CebuLocations.NotInListMessage, result.FieldErrors["Address"]);
        Assert.Null((await db.Customers.SingleAsync()).Address);
    }

    [Theory]
    [InlineData("100 Ortigas Ave, Pasig City")]                          // typed, not one of the options
    [InlineData("osmeña blvd, brgy. capitol site, cebu city")]           // changed in the browser
    [InlineData("Osmeña Blvd,  Brgy. Capitol Site, Cebu City")]          // (two spaces)
    [InlineData("Lower Pakigne, Minglanilla")]                           // a booking-only place, not a profile address
    [InlineData("12345")]
    public async Task SaveAddress_NotOneOfTheOptions_IsRefused_AndKeepsTheOldAddress(string typed)
    {
        var (svc, db, hasher) = Create();
        var c = await AddCustomerAsync(db, hasher);

        var result = await svc.SaveAddressAsync(c.UserId, new CustomerAddressForm { Address = typed });

        Assert.False(result.Saved);
        Assert.Equal(CebuLocations.NotInListMessage, result.FieldErrors["Address"]);
        Assert.Equal("45 Mabini St, Quezon City", (await db.Customers.SingleAsync()).Address);
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

        await svc.SaveAddressAsync(ana.UserId, new CustomerAddressForm { Address = "Salinas Drive, Brgy. Lahug, Cebu City" });
        await svc.SaveAddressAsync(ana.UserId, new CustomerAddressForm { Address = null });

        Assert.Equal("Salinas Drive, Brgy. Lahug, Cebu City", (await db.Customers.SingleAsync(x => x.CustomerId == ana.CustomerId)).Address);
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
