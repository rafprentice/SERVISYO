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
    [InlineData("Lapu-Lapu City", true)]
    [InlineData("123213", false)]
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
    [InlineData("0917-555-1234", true)]
    [InlineData("+63 917 555 1234", true)]
    [InlineData("qweqweeqw", false)]
    [InlineData("aweqweqewqe", false)]
    [InlineData("12345", false)]
    [InlineData("0917abc1234", false)]
    public void Phone(string value, bool ok) => Assert.Equal(ok, InputRules.IsPhone(value));

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
        FullName = "Ana Reyes", Email = "ana@test.com", PhoneNumber = "0917-555-1234", City = "Cebu City",
        StreetAddress = "123 Rizal St.", Barangay = "Lahug",
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
        f.City = "qweqwe1";
        Assert.True(Fails(f, nameof(f.FullName)));
        Assert.True(Fails(f, nameof(f.PhoneNumber)));
        Assert.True(Fails(f, nameof(f.City)));
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
            BusinessName = "123123213", City = "213123", MinNoticeDays = 99, MaxJobsPerDay = 0, BaseFee = -1
        };
        foreach (var field in new[] { "FirstName", "MiddleName", "LastName", "PhoneNumber", "BusinessName", "City", "MinNoticeDays", "MaxJobsPerDay", "BaseFee" })
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
    public async Task Service_StoresThePhoneWithoutSpacesOrHyphens()
    {
        var svc = NewAuth(out var db);
        var result = await svc.RegisterCustomerAsync(new RegisterCustomerViewModel
        {
            FirstName = "Maria", MiddleName = "Lopez", LastName = "Santos", Email = "maria@email.com", PhoneNumber = "+63 917 555 1234",
            Address = "123 Rizal St., Brgy. Lahug", Password = "Passw0rd1", ConfirmPassword = "Passw0rd1"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("+639175551234", (await db.Users.SingleAsync()).PhoneNumber);
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
    public void EveryFieldExceptCity_IsRequired_OnBothForms()
    {
        var c = new CustomerRegistrationViewModel();
        foreach (var field in new[] { "FullName", "Email", "PhoneNumber", "StreetAddress", "Barangay", "Password", "ConfirmPassword", "AcceptTerms" })
            Assert.True(Fails(c, field), "customer " + field + " should be required");
        Assert.False(Fails(c, "City"));

        var p = new RegisterProviderViewModel { MinNoticeDays = 1, MaxJobsPerDay = 5 };
        foreach (var field in new[] { "FirstName", "MiddleName", "LastName", "Email", "PhoneNumber", "Password", "ConfirmPassword", "BusinessName", "Description", "Address", "BaseFee" })
            Assert.True(Fails(p, field), "provider " + field + " should be required");
        Assert.False(Fails(p, "City"));
    }
}
