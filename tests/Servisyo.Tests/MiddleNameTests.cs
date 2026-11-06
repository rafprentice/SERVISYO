using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// A middle name, when given, must be at least 2 characters: "Castro" is fine, "C" or "M" is refused.
public class MiddleNameTests
{
    [Fact]
    public void TheAttribute_IsStringLength100_Minimum2_WithTheMessage()
    {
        var length = typeof(RegisterBaseViewModel).GetProperty("MiddleName")!.GetCustomAttribute<StringLengthAttribute>()!;
        Assert.Equal(100, length.MaximumLength);
        Assert.Equal(2, length.MinimumLength);
        Assert.Equal("Middle name must be at least 2 characters if provided.", length.ErrorMessage);
        Assert.Equal("Middle name must be at least 2 characters.", InputRules.MiddleNameTooShortMessage);
    }

    private static List<ValidationResult> MiddleNameErrors(string? middle)
    {
        var model = new RegisterProviderViewModel { MiddleName = middle };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results.Where(r => r.MemberNames.Contains("MiddleName")).ToList();
    }

    [Theory]
    [InlineData("C")]
    [InlineData("M")]
    public void ASingleLetter_IsRefused_ByTheForm(string middle)
        => Assert.Equal(InputRules.MiddleNameLengthMessage, MiddleNameErrors(middle).Single().ErrorMessage);

    [Theory]
    [InlineData("Castro")]
    [InlineData("Al")]
    [InlineData("Dela Cruz")]
    public void TwoOrMoreCharacters_AreAccepted_ByTheForm(string middle)
        => Assert.Empty(MiddleNameErrors(middle));

    // ------------------------------------------------------------------ the service checks again (a request can skip the page)

    private static AuthService NewAuth(out ServisyoDbContext db)
    {
        db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new AuthService(db, new PasswordHasher<User>());
    }

    private static RegisterProviderViewModel Provider(string? middle) => new()
    {
        FirstName = "Juan", MiddleName = middle, LastName = "Dela Cruz", Email = "juan@email.com", PhoneNumber = "09171234567",
        Password = "Passw0rd1", ConfirmPassword = "Passw0rd1", BusinessName = "Juan Plumbing Services",
        Description = "Pipe repair and installation.", Address = "Salinas Drive, Brgy. Lahug, Cebu City", BaseFee = 500m
    };

    [Theory]
    [InlineData("C")]
    [InlineData("M")]
    [InlineData("  C  ")]              // spaces at the ends do not count
    public async Task RegisterProvider_RefusesAOneCharacterMiddleName_AndSavesNothing(string middle)
    {
        var auth = NewAuth(out var db);

        var result = await auth.RegisterProviderAsync(Provider(middle));

        Assert.False(result.Succeeded);
        Assert.Equal(InputRules.MiddleNameTooShortMessage, result.Error);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task RegisterProvider_AcceptsCastro()
    {
        var auth = NewAuth(out var db);

        var result = await auth.RegisterProviderAsync(Provider("Castro"));

        Assert.True(result.Succeeded);
        Assert.Equal("Castro", (await db.Users.SingleAsync()).MiddleName);
    }

    [Theory]
    [InlineData(null, true)]           // a customer's middle name stays optional
    [InlineData("C", false)]
    [InlineData("Castro", true)]
    public async Task RegisterCustomer_OptionalMiddleName_ButNotASingleLetter(string? middle, bool ok)
    {
        var auth = NewAuth(out var db);

        var result = await auth.RegisterCustomerAsync(new RegisterCustomerViewModel
        {
            FirstName = "Maria", MiddleName = middle, LastName = "Santos", Email = "maria@email.com", PhoneNumber = "09171234567",
            Address = "123 Rizal St., Brgy. Lahug", Password = "Passw0rd1", ConfirmPassword = "Passw0rd1"
        });

        Assert.Equal(ok, result.Succeeded);
        if (!ok) Assert.Equal(InputRules.MiddleNameTooShortMessage, result.Error);
        Assert.Equal(ok ? 1 : 0, await db.Users.CountAsync());
    }
}
