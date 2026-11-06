using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// "Base fee in PHP" on provider registration and My Profile: at least 1 PHP, at most 100,000 PHP; never 0 or below.
public class BaseFeeTests
{
    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ the rule

    [Theory]
    [InlineData(null, BaseFeeRules.RequiredMessage)]
    [InlineData("-500", BaseFeeRules.MinMessage)]
    [InlineData("-0.01", BaseFeeRules.MinMessage)]
    [InlineData("0", BaseFeeRules.MinMessage)]
    [InlineData("0.5", BaseFeeRules.MinMessage)]
    [InlineData("0.99", BaseFeeRules.MinMessage)]
    [InlineData("1", null)]
    [InlineData("1.01", null)]
    [InlineData("500", null)]
    [InlineData("100000", null)]
    [InlineData("100000.01", BaseFeeRules.MaxMessage)]
    public void Check_RefusesZeroNegativeAndTooHigh(string? fee, string? expected)
        => Assert.Equal(expected, BaseFeeRules.Check(fee is null ? null : D(fee)));

    [Fact]
    public void TheMessage_IsTheOneAskedFor()
        => Assert.Equal("Base fee must be at least 1 PHP.", BaseFeeRules.MinMessage);

    // ------------------------------------------------------------------ the [Range] attributes match the rule

    [Fact]
    public void TheRegistrationForm_HasRange1To100000_WithTheMessage()
    {
        var form = typeof(RegisterProviderViewModel);
        var range = form.GetProperty("BaseFee")!.GetCustomAttribute<RangeAttribute>()!;
        Assert.Equal(BaseFeeRules.Min, Convert.ToInt32(range.Minimum));
        Assert.Equal(BaseFeeRules.Max, Convert.ToInt32(range.Maximum));
        Assert.Equal(BaseFeeRules.MinMessage, range.ErrorMessage);
        Assert.NotNull(form.GetProperty("BaseFee")!.GetCustomAttribute<RequiredAttribute>());
    }

    private static List<ValidationResult> BaseFeeErrors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results.Where(r => r.MemberNames.Contains("BaseFee")).ToList();
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("0.99", false)]
    [InlineData("100001", false)]
    [InlineData("1", true)]
    [InlineData("750.25", true)]
    [InlineData("100000", true)]
    public void ModelValidation_RefusesZeroAndNegative_OnRegistration(string fee, bool ok)
    {
        var errors = BaseFeeErrors(new RegisterProviderViewModel { BaseFee = D(fee) });
        Assert.Equal(ok, errors.Count == 0);
        if (!ok) Assert.Equal(BaseFeeRules.MinMessage, errors[0].ErrorMessage);   // the one [Range] message
    }

    [Fact]
    public void ModelValidation_RequiresTheFee_OnRegistration()
        => Assert.Equal(BaseFeeRules.RequiredMessage, BaseFeeErrors(new RegisterProviderViewModel()).Single().ErrorMessage);

    [Fact]
    public void MyProfile_DoesNotTakeABaseFee_ItIsLockedOnceApproved()
        => Assert.Contains("BaseFee", ProviderLockRules.LockedFields);

    // ------------------------------------------------------------------ the message shown after ModelState fails

    private static ModelStateDictionary StateWith(string typed, string error)
    {
        var state = new ModelStateDictionary();
        state.SetModelValue("BaseFee", typed, typed);
        state.AddModelError("BaseFee", error);
        state.SetModelValue("Email", "x", "x");
        state.AddModelError("Email", "Email problem.");
        return state;
    }

    [Theory]
    [InlineData("0", "0", BaseFeeRules.MinMessage)]
    [InlineData("-5", "-5", BaseFeeRules.MinMessage)]
    [InlineData("200000", "200000", BaseFeeRules.MaxMessage)]     // [Range] alone would say "at least 1 PHP"
    [InlineData("abc", null, BaseFeeRules.NotANumberMessage)]     // the binder could not read it: BaseFee stays null
    [InlineData("", null, BaseFeeRules.RequiredMessage)]
    public void FixModelStateMessage_GivesTheRightMessage_AndLeavesOtherFieldsAlone(string typed, string? bound, string expected)
    {
        var state = StateWith(typed, "Base fee must be at least 1 PHP.");

        BaseFeeRules.FixModelStateMessage(state, bound is null ? null : D(bound));

        Assert.Equal(expected, state["BaseFee"]!.Errors.Single().ErrorMessage);
        Assert.Equal("Email problem.", state["Email"]!.Errors.Single().ErrorMessage);
        Assert.False(state.IsValid);                                   // still blocks the save
    }

    [Fact]
    public void ForInput_AlwaysUsesADot()
    {
        Assert.Equal("1500.5", BaseFeeRules.ForInput(1500.50m));
        Assert.Equal("500", BaseFeeRules.ForInput(500m));
        Assert.Null(BaseFeeRules.ForInput(null));
    }

    // ------------------------------------------------------------------ registration service (a request can skip the page)

    private static AuthService NewAuth(out ServisyoDbContext db)
    {
        db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new AuthService(db, new PasswordHasher<User>());
    }

    private static RegisterProviderViewModel Provider(decimal? fee) => new()
    {
        FirstName = "Juan", MiddleName = "Reyes", LastName = "Dela Cruz", Email = "juan@email.com", PhoneNumber = "09171234567",
        Password = "Passw0rd1", ConfirmPassword = "Passw0rd1",
        BusinessName = "Juan Plumbing Services", Description = "Pipe repair and installation.",
        Address = "Salinas Drive, Brgy. Lahug, Cebu City", BaseFee = fee
    };

    [Theory]
    [InlineData("0", BaseFeeRules.MinMessage)]
    [InlineData("-1", BaseFeeRules.MinMessage)]
    [InlineData("0.5", BaseFeeRules.MinMessage)]
    [InlineData("100000.5", BaseFeeRules.MaxMessage)]
    public async Task Register_RefusesABadBaseFee_AndSavesNothing(string fee, string expected)
    {
        var auth = NewAuth(out var db);

        var result = await auth.RegisterProviderAsync(Provider(D(fee)));

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error);
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Providers.CountAsync());
    }

    [Fact]
    public async Task Register_RefusesAMissingBaseFee()
    {
        var auth = NewAuth(out var db);
        var result = await auth.RegisterProviderAsync(Provider(null));

        Assert.False(result.Succeeded);
        Assert.Equal(BaseFeeRules.RequiredMessage, result.Error);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("1500.555", "1500.56")]
    [InlineData("100000", "100000")]
    public async Task Register_SavesAGoodBaseFee_WithTwoDecimals(string fee, string saved)
    {
        var auth = NewAuth(out var db);

        var result = await auth.RegisterProviderAsync(Provider(D(fee)));

        Assert.True(result.Succeeded);
        Assert.Equal(D(saved), (await db.Providers.SingleAsync()).BaseFee);
    }
}
