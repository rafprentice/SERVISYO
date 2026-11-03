using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class RegistrationValidationTests
{
    // ------------------------------------------------------------------ the patterns themselves

    [Theory]
    [InlineData("Juan", true)]
    [InlineData("Ma. Lourdes", true)]
    [InlineData("O'Brien", true)]
    [InlineData("Peñaranda", true)]
    [InlineData("Niño", true)]
    [InlineData("Lapu-Lapu", true)]
    [InlineData("123213", false)]
    [InlineData("Ana@", false)]
    [InlineData("Ana_Marie", false)]
    [InlineData("Juan2", false)]
    [InlineData("...", false)]
    [InlineData("<script>", false)]
    public void Name(string value, bool ok) => Assert.Equal(ok, InputRules.IsName(value));

    [Theory]
    [InlineData("Juan dela Cruz", true)]
    [InlineData("José Rizal Jr.", true)]
    [InlineData("213123123123", false)]
    [InlineData("Juan", false)]
    [InlineData("Juan 123", false)]
    public void FullName(string value, bool ok) => Assert.Equal(ok, InputRules.IsFullName(value));

    [Theory]
    [InlineData("09171234567", true)]
    [InlineData("+639171234567", true)]
    [InlineData("0917-555-1234", false)]      // digits only: no dashes
    [InlineData("0917 555 1234", false)]      // no spaces
    [InlineData("+63 917 555 1234", false)]
    [InlineData("9171234567", false)]         // must start with 09 (or +639)
    [InlineData("0917123456", false)]         // 10 digits
    [InlineData("091712345678", false)]       // 12 digits
    [InlineData("08171234567", false)]        // not a mobile number
    [InlineData("qweqweeqw", false)]
    [InlineData("12345", false)]
    [InlineData("0917abc1234", false)]
    [InlineData("０９１７１２３４５６７", false)]  // full-width digits are not 0-9
    public void Phone(string value, bool ok) => Assert.Equal(ok, InputRules.IsPhone(value));

    [Theory]
    [InlineData("juan@gmail.com", true)]
    [InlineData("juan.dela-cruz+1@yahoo.com", true)]
    [InlineData("juan123@gmail.com", true)]          // numbers are fine BEFORE the @
    [InlineData("ana@usc.edu.ph", true)]
    [InlineData("ana@my-mail.com", true)]
    [InlineData("juan@gmail2.com", false)]           // no numbers in the domain
    [InlineData("juan@gmail.c0m", false)]            // no numbers in the TLD
    [InlineData("juan@a.c", false)]                  // domain and TLD need at least 2 letters
    [InlineData("juan@ab.c", false)]
    [InlineData("juan@b.co", false)]
    [InlineData("juan@-ab.com", false)]
    [InlineData("juan@gmail", false)]                // no TLD
    [InlineData("juangmail.com", false)]
    [InlineData("ju an@gmail.com", false)]
    public void Email(string value, bool ok) => Assert.Equal(ok, InputRules.IsEmail(value));

    [Theory]
    [InlineData("09171234567", "09171234567")]
    [InlineData("+639171234567", "09171234567")]
    [InlineData("639171234567", "09171234567")]
    [InlineData("0917-123-4567", "09171234567")]     // old saved numbers may still have dashes
    [InlineData("  ", null)]
    public void NormalizePhone_SavesThe09Form(string typed, string? saved) => Assert.Equal(saved, ProfileRules.NormalizePhone(typed));

    [Theory]
    [InlineData("123 Gorordo Ave", true)]
    [InlineData("Lahug", true)]
    [InlineData("12345", false)]
    [InlineData("42-7", false)]
    public void Address(string value, bool ok) => Assert.Equal(ok, InputRules.HasALetter(value));

    // ------------------------------------------------------------------ model validation (what ModelState.IsValid uses)

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static bool Fails(object model, string member) => Validate(model).Any(r => r.MemberNames.Contains(member));

    private static CustomerRegistrationViewModel GoodCustomer() => new()
    {
        FullName = "Ana Reyes", Email = "ana@test.com", PhoneNumber = "09175551234",
        Location = "Salinas Drive, Brgy. Lahug, Cebu City",
        Password = "Passw0rd1", ConfirmPassword = "Passw0rd1", AcceptTerms = true
    };

    [Fact]
    public void CustomerForm_ValidValues_Pass() => Assert.Empty(Validate(GoodCustomer()));

    [Fact]
    public void CustomerForm_RejectsTheValuesFromTheBugReport()
    {
        var f = GoodCustomer();
        f.FullName = "213123123123";
        f.PhoneNumber = "qweqweeqw";
        f.Location = "12345";
        f.Email = "ana@gmail2.com";
        Assert.True(Fails(f, nameof(f.FullName)));
        Assert.True(Fails(f, nameof(f.PhoneNumber)));
        Assert.True(Fails(f, nameof(f.Location)));                     // not one of the five addresses
        Assert.True(Fails(f, nameof(f.Email)));
    }

    [Fact]
    public void CustomerForm_TooLongNameOrEmail_Fails()
    {
        var f = GoodCustomer();
        f.FullName = new string('a', 50) + " " + new string('b', 50);        // 101: the name columns hold 100
        f.Email = new string('e', 250) + "@test.com";                        // 259: EMAIL holds 256
        Assert.True(Fails(f, nameof(f.FullName)));
        Assert.True(Fails(f, nameof(f.Email)));
    }

    [Fact]
    public void CustomerForm_EmptyRequiredFields_AndBadPasswords_Fail()
    {
        var f = new CustomerRegistrationViewModel();
        Assert.True(Fails(f, nameof(f.FullName)));
        Assert.True(Fails(f, nameof(f.Email)));
        Assert.True(Fails(f, nameof(f.Password)));
        Assert.True(Fails(f, nameof(f.ConfirmPassword)));
        Assert.True(Fails(f, nameof(f.AcceptTerms)));

        var g = GoodCustomer();
        g.Password = "short1"; g.ConfirmPassword = "short1";
        Assert.True(Fails(g, nameof(g.Password)));
        g.Password = "Passw0rd1"; g.ConfirmPassword = "Passw0rd2";
        Assert.True(Fails(g, nameof(g.ConfirmPassword)));
    }

    [Fact]
    public void ProviderForm_RejectsNumberNames_LetterPhones_AndOutOfRangeNumbers()
    {
        var p = new RegisterProviderViewModel
        {
            FirstName = "123213", MiddleName = "1231233", LastName = "213123", Email = "x@test.com",
            PhoneNumber = "aweqweqewqe", Password = "Passw0rd1", ConfirmPassword = "Passw0rd1",
            BusinessName = "123123213", Address = "12345", MinNoticeDays = 99, MaxJobsPerDay = 0, BaseFee = -1
        };
        foreach (var field in new[] { "FirstName", "MiddleName", "LastName", "PhoneNumber", "BusinessName", "Address", "MinNoticeDays", "MaxJobsPerDay", "BaseFee" })
            Assert.True(Fails(p, field), field + " should be refused");
    }

    // ------------------------------------------------------------------ the service refuses too (a request can skip the page)

    private static AuthService NewAuth(out ServisyoDbContext db)
    {
        db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new AuthService(db, new PasswordHasher<User>());
    }

    [Theory]
    [InlineData("123", "Santos", null, "Passw0rd1", "Passw0rd1")]
    [InlineData("Maria", "Santos", "qweqweeqw", "Passw0rd1", "Passw0rd1")]
    [InlineData("Maria", "Santos", "0917 123 4567", "Passw0rd1", "Passw0rd1")]
    [InlineData("Maria", "Santos", null, "short", "short")]
    [InlineData("Maria", "Santos", null, "Passw0rd1", "Different1")]
    public async Task Service_RefusesInvalidCustomerSignUps_AndSavesNothing(string first, string last, string? phone, string pw, string confirm)
    {
        var svc = NewAuth(out var db);
        var result = await svc.RegisterCustomerAsync(new RegisterCustomerViewModel
        {
            FirstName = first, MiddleName = "Lopez", LastName = last, Email = "maria@email.com", PhoneNumber = phone ?? "09171234567",
            Address = "123 Rizal St., Brgy. Lahug", Password = pw, ConfirmPassword = confirm
        });

        Assert.False(result.Succeeded);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Service_SavesA63PhoneAs09()
    {
        var svc = NewAuth(out var db);
        var result = await svc.RegisterCustomerAsync(new RegisterCustomerViewModel
        {
            FirstName = "Maria", MiddleName = "Lopez", LastName = "Santos", Email = "maria@email.com", PhoneNumber = "+639175551234",
            Address = "123 Rizal St., Brgy. Lahug", Password = "Passw0rd1", ConfirmPassword = "Passw0rd1"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("09175551234", (await db.Users.SingleAsync()).PhoneNumber);
        Assert.Equal(AccountStatuses.Active, (await db.Customers.SingleAsync()).AccountStatus);   // the status copy
    }

    [Theory]
    [InlineData("maria@gmail2.com")]
    [InlineData("maria@a.c")]
    public async Task Service_RefusesABadEmailDomain(string email)
    {
        var svc = NewAuth(out var db);
        var result = await svc.RegisterCustomerAsync(new RegisterCustomerViewModel
        {
            FirstName = "Maria", MiddleName = "Lopez", LastName = "Santos", Email = email, PhoneNumber = "09175551234",
            Address = "123 Rizal St., Brgy. Lahug", Password = "Passw0rd1", ConfirmPassword = "Passw0rd1"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(InputRules.EmailMessage, result.Error);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Service_RefusesANumbersOnlyAddress()
    {
        var svc = NewAuth(out var db);
        var result = await svc.RegisterCustomerAsync(new RegisterCustomerViewModel
        {
            FirstName = "Maria", MiddleName = "Lopez", LastName = "Santos", Email = "maria@email.com", PhoneNumber = "09175551234",
            Address = "12345", Password = "Passw0rd1", ConfirmPassword = "Passw0rd1"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(InputRules.AddressMessage, result.Error);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Service_RefusesAProviderWithANumbersOnlyBusinessName()
    {
        var svc = NewAuth(out var db);
        var result = await svc.RegisterProviderAsync(new RegisterProviderViewModel
        {
            FirstName = "Juan", MiddleName = "Reyes", LastName = "Dela Cruz", Email = "juan@email.com", PhoneNumber = "09171234567",
            Password = "Passw0rd1", ConfirmPassword = "Passw0rd1",
            BusinessName = "123123213", Description = "Pipe repair.", Address = "Lahug", BaseFee = 500m
        });

        Assert.False(result.Succeeded);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public void EveryField_IsRequired_OnBothForms()
    {
        var c = new CustomerRegistrationViewModel();
        foreach (var field in new[] { "FullName", "Email", "PhoneNumber", "Location", "Password", "ConfirmPassword", "AcceptTerms" })
            Assert.True(Fails(c, field), "customer " + field + " should be required");

        var p = new RegisterProviderViewModel { MinNoticeDays = 1, MaxJobsPerDay = 5 };
        foreach (var field in new[] { "FirstName", "MiddleName", "LastName", "Email", "PhoneNumber", "Password", "ConfirmPassword", "BusinessName", "Description", "Address", "BaseFee" })
            Assert.True(Fails(p, field), "provider " + field + " should be required");
    }

    [Theory]
    [InlineData("Osmeña Blvd, Brgy. Capitol Site, Cebu City", true)]
    [InlineData("C. Padilla St, Brgy. Duljo Fatima, Cebu City", true)]
    [InlineData("Lahug", false)]                                      // typed, not chosen
    [InlineData("<script>alert(1)</script>", false)]
    public void ProviderForm_AcceptsOnlyAnAddressFromTheList(string address, bool ok)
    {
        var p = new RegisterProviderViewModel
        {
            FirstName = "Juan", MiddleName = "Reyes", LastName = "Dela Cruz", Email = "juan@email.com", PhoneNumber = "09171234567",
            Password = "Passw0rd1", ConfirmPassword = "Passw0rd1", BusinessName = "Juan Plumbing", Description = "Pipe repair.",
            Address = address, BaseFee = 500m
        };
        Assert.Equal(!ok, Fails(p, nameof(p.Address)));
        if (ok)
        {
            Assert.NotNull(p.StreetAddress);
            Assert.NotNull(p.Barangay);
            Assert.Equal("Cebu City", p.City);
        }
    }

    [Fact]
    public void TheFormsHaveNoCityField_SERVISYO_IsCebuCityOnly()
    {
        // the forms show "Cebu City" (read-only, never posted); the tables have no CITY column
        Assert.False(typeof(CustomerRegistrationViewModel).GetProperty("City")!.CanWrite);
        Assert.Null(typeof(RegisterCustomerViewModel).GetProperty("City"));
        Assert.False(typeof(RegisterProviderViewModel).GetProperty("City")!.CanWrite);
        Assert.Null(typeof(Customer).GetProperty("City"));
        Assert.Null(typeof(Provider).GetProperty("City"));
    }
}
