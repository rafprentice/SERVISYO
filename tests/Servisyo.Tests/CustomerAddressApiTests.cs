using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Controllers;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// PUT / DELETE /api/profile/address: the saved address can be changed to one of the 5 Cebu City options,
/// never removed.
public class CustomerAddressApiTests
{
    private const string Lahug = "Salinas Drive, Brgy. Lahug, Cebu City";
    private const string Banilad = "Gov. M. Cuenco Ave, Brgy. Banilad, Cebu City";
    private const string CannotRemove = "Saved addresses cannot be removed once set. Please edit your address instead.";

    private static ServisyoDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Customer> AddCustomerAsync(ServisyoDbContext db, string? address = Lahug,
        string status = AccountStatuses.Active, string first = "Ana")
    {
        var user = new User
        {
            FirstName = first, LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com",
            Role = Roles.Customer, AccountStatus = status, PasswordHash = "x", CreatedAt = new DateTime(2024, 1, 12, 4, 0, 0)
        };
        var customer = new Customer { User = user, Address = address };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    /// The API as the signed-in user with this id (the id only ever comes from the sign-in cookie).
    private static CustomerAddressApiController ApiFor(ServisyoDbContext db, int? userId)
    {
        var claims = userId is null
            ? Array.Empty<Claim>()
            : new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()), new Claim(ClaimTypes.Role, Roles.Customer) };
        return new CustomerAddressApiController(new CustomerProfileService(db, new PasswordHasher<User>()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            }
        };
    }

    private static async Task<string?> SavedAddressAsync(ServisyoDbContext db, int customerId) =>
        (await db.Customers.AsNoTracking().SingleAsync(c => c.CustomerId == customerId)).Address;

    // ------------------------------------------------------------------ DELETE

    [Fact]
    public async Task Delete_IsAlways400_WithTheMessage_AndRemovesNothing()
    {
        using var db = NewDb();
        var ana = await AddCustomerAsync(db);

        var result = ApiFor(db, ana.UserId).Delete();

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, bad.StatusCode);
        Assert.Equal(CannotRemove, Assert.IsType<AddressApiMessage>(bad.Value).Message);
        Assert.Equal(Lahug, await SavedAddressAsync(db, ana.CustomerId));
    }

    // ------------------------------------------------------------------ PUT

    [Fact]
    public async Task Put_OneOfTheOptions_UpdatesTheSavedAddress_AndReturnsItsParts()
    {
        using var db = NewDb();
        var ana = await AddCustomerAsync(db);

        var result = await ApiFor(db, ana.UserId).Put(new AddressUpdateRequest(Banilad), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<SavedAddressResponse>(ok.Value);
        Assert.Equal("Your address was updated.", body.Message);
        Assert.Equal(Banilad, body.Address);
        Assert.Equal("Gov. M. Cuenco Ave", body.StreetAddress);
        Assert.Equal("Banilad", body.Barangay);
        Assert.Equal("Cebu City", body.City);
        Assert.Equal(Banilad, await SavedAddressAsync(db, ana.CustomerId));
    }

    [Fact]
    public async Task Put_ByACustomerWithNoAddressYet_AddsIt()
    {
        using var db = NewDb();
        var ana = await AddCustomerAsync(db, address: null);

        var result = await ApiFor(db, ana.UserId).Put(new AddressUpdateRequest(Lahug), default);

        Assert.Equal("Your address was saved.", Assert.IsType<SavedAddressResponse>(Assert.IsType<OkObjectResult>(result).Value).Message);
        Assert.Equal(Lahug, await SavedAddressAsync(db, ana.CustomerId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Put_Blank_CannotRemoveTheSavedAddress(string? blank)
    {
        using var db = NewDb();
        var ana = await AddCustomerAsync(db);

        var result = await ApiFor(db, ana.UserId).Put(new AddressUpdateRequest(blank), default);

        Assert.Equal(CannotRemove, Assert.IsType<AddressApiMessage>(Assert.IsType<BadRequestObjectResult>(result).Value).Message);
        Assert.Equal(Lahug, await SavedAddressAsync(db, ana.CustomerId));
    }

    [Fact]
    public async Task Put_WithNoBodyAtAll_IsTreatedAsBlank_AndRemovesNothing()
    {
        using var db = NewDb();
        var ana = await AddCustomerAsync(db);

        var result = await ApiFor(db, ana.UserId).Put(null, default);

        Assert.Equal(CannotRemove, Assert.IsType<AddressApiMessage>(Assert.IsType<BadRequestObjectResult>(result).Value).Message);
        Assert.Equal(Lahug, await SavedAddressAsync(db, ana.CustomerId));
    }

    [Theory]
    [InlineData("100 Ortigas Ave, Pasig City")]
    [InlineData("Lower Pakigne, Minglanilla")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Put_SomethingNotInTheList_Is400_AndKeepsTheOldAddress(string typed)
    {
        using var db = NewDb();
        var ana = await AddCustomerAsync(db);

        var result = await ApiFor(db, ana.UserId).Put(new AddressUpdateRequest(typed), default);

        Assert.Equal(CebuLocations.NotInListMessage, Assert.IsType<AddressApiMessage>(Assert.IsType<BadRequestObjectResult>(result).Value).Message);
        Assert.Equal(Lahug, await SavedAddressAsync(db, ana.CustomerId));
    }

    [Fact]
    public async Task Put_OnlyChangesTheSignedInCustomer()
    {
        using var db = NewDb();
        var ana = await AddCustomerAsync(db);
        var ben = await AddCustomerAsync(db, address: Lahug, first: "Ben");

        await ApiFor(db, ana.UserId).Put(new AddressUpdateRequest(Banilad), default);

        Assert.Equal(Banilad, await SavedAddressAsync(db, ana.CustomerId));
        Assert.Equal(Lahug, await SavedAddressAsync(db, ben.CustomerId));
    }

    [Theory]
    [InlineData(AccountStatuses.Suspended)]
    [InlineData(AccountStatuses.Deactivated)]
    public async Task Put_ByASuspendedOrDeactivatedCustomer_Is403_AndChangesNothing(string status)
    {
        using var db = NewDb();
        var ana = await AddCustomerAsync(db, status: status);

        var result = await ApiFor(db, ana.UserId).Put(new AddressUpdateRequest(Banilad), default);

        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        Assert.Equal(Lahug, await SavedAddressAsync(db, ana.CustomerId));
    }

    [Fact]
    public async Task Put_WithoutAUserIdInTheCookie_Is403()
    {
        using var db = NewDb();
        await AddCustomerAsync(db);

        var result = await ApiFor(db, null).Put(new AddressUpdateRequest(Banilad), default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ------------------------------------------------------------------ how the endpoint is set up

    [Fact]
    public void TheController_IsForSignedInCustomersOnly_AtApiProfileAddress()
    {
        var type = typeof(CustomerAddressApiController);
        Assert.Equal("api/profile/address", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(Roles.Customer, type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>());
    }

    [Fact]
    public void ThereAreOnlyTwoEndpoints_PutChecksTheAntiforgeryToken_DeleteChangesNothing()
    {
        var type = typeof(CustomerAddressApiController);
        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName).Select(m => m.Name).OrderBy(n => n);
        Assert.Equal(new[] { "Delete", "Put" }, actions);

        var put = type.GetMethod(nameof(CustomerAddressApiController.Put))!;
        Assert.NotNull(put.GetCustomAttribute<HttpPutAttribute>());
        Assert.NotNull(put.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());

        var delete = type.GetMethod(nameof(CustomerAddressApiController.Delete))!;
        Assert.NotNull(delete.GetCustomAttribute<HttpDeleteAttribute>());
        Assert.NotNull(delete.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>());
    }

    [Theory]
    [InlineData("/api/profile/address", true)]
    [InlineData("/API/Profile/Address", true)]
    [InlineData("/customer/profile", false)]
    [InlineData("/apiary", false)]
    public void ApiPaths_AnswerWithAStatusCode_InsteadOfTheLoginPage(string path, bool isApi)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        Assert.Equal(isApi, ApiPaths.IsApi(context.Request));
    }
}

/// The Saved Address card on "My Profile": no remove button, an Edit Address button and the 5 Cebu City options.
public class CustomerProfileAddressViewTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray()))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    private static string View() => Read("src", "Servisyo.Web", "Views", "CustomerProfile", "Index.cshtml");

    [Fact]
    public void TheCard_HasNoRemoveButton()
    {
        var view = View();
        Assert.DoesNotContain("sv-cp__trash", view);
        Assert.DoesNotContain("Remove saved address", view);
        Assert.DoesNotContain("<input type=\"hidden\" name=\"Address\" value=\"\"", view);
    }

    [Fact]
    public void TheCard_HasEditAddress_AndADropdownOfTheCebuOptions_SavedThroughTheApi()
    {
        var view = View();
        Assert.Contains("\"Edit Address\"", view);
        Assert.Contains("<select id=\"cp-address\" name=\"Address\" required", view);
        Assert.Contains("@foreach (var loc in CebuLocations.All)", view);
        Assert.Contains("data-cp-api=", view);
        Assert.Equal(5, CebuLocations.All.Count);
        Assert.Equal(new[]
        {
            "Osmeña Blvd, Brgy. Capitol Site, Cebu City",
            "Gov. M. Cuenco Ave, Brgy. Banilad, Cebu City",
            "N. Bacalso Ave, Brgy. Sambag I, Cebu City",
            "Salinas Drive, Brgy. Lahug, Cebu City",
            "C. Padilla St, Brgy. Duljo Fatima, Cebu City",
        }, CebuLocations.All.Select(l => l.Full));
    }

    [Fact]
    public void ThePhoneBox_IsRequired_AndAtMost13Characters()
    {
        var view = View();
        Assert.Contains("name=\"PhoneNumber\" type=\"tel\" value=\"@form.PhoneNumber\" required maxlength=\"13\"", view);
        Assert.Contains("pattern=\"(09[0-9]{9}|\\+639[0-9]{9})\"", view);
    }

    [Fact]
    public void TheScript_SavesWithPut_AndSendsTheAntiforgeryToken()
    {
        var script = Read("src", "Servisyo.Web", "wwwroot", "js", "customer-profile.js");
        Assert.Contains("method: \"PUT\"", script);
        Assert.Contains("\"RequestVerificationToken\": tokenField.value", script);
        Assert.DoesNotContain("DELETE", script);
    }
}
