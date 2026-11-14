using System.Security.Claims;
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

/// The customer profile lock: while a customer has a booking that is not finished (any status except Completed,
/// Cancelled and Rejected), their personal information and saved address cannot be changed; the password can.
public class CustomerProfileLockTests
{
    private const string Lahug = "Salinas Drive, Brgy. Lahug, Cebu City";
    private const string Banilad = "Gov. M. Cuenco Ave, Brgy. Banilad, Cebu City";
    private const string Password = "OldPass123";

    private sealed record World(ServisyoDbContext Db, CustomerProfileService Svc, Customer Ana, Customer Ben, Provider Vince);

    private static async Task<World> CreateAsync()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hasher = new PasswordHasher<User>();
        User U(string first, string last, string role)
        {
            var u = new User { FirstName = first, LastName = last, Email = $"{first.ToLowerInvariant()}@test.com", PhoneNumber = "09171112222",
                               Role = role, AccountStatus = AccountStatuses.Active, CreatedAt = new DateTime(2024, 1, 12) };
            u.PasswordHash = hasher.HashPassword(u, Password);
            return u;
        }
        var ana = new Customer { User = U("Ana", "Cruz", Roles.Customer), Address = Lahug };
        var ben = new Customer { User = U("Ben", "Reyes", Roles.Customer), Address = Lahug };
        var vince = new Provider { BusinessName = "Vincent Valencia", ApprovalStatus = ApprovalStatuses.Approved, User = U("Vincent", "Valencia", Roles.ServiceProvider) };
        db.AddRange(ana, ben, vince);
        await db.SaveChangesAsync();
        return new World(db, new CustomerProfileService(db, hasher), ana, ben, vince);
    }

    private static async Task BookAsync(World w, Customer c, string status)
    {
        w.Db.Bookings.Add(new Booking
        {
            CustomerId = c.CustomerId, ProviderId = w.Vince.ProviderId, BookingDate = new DateOnly(2026, 11, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 500m, Status = status, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        });
        await w.Db.SaveChangesAsync();
    }

    private static CustomerProfileForm Changed(Customer c) =>
        new() { FullName = "Ana Santos", Email = "ana.new@test.com", PhoneNumber = "09998887777" };

    // ------------------------------------------------------------------ what counts as active

    [Theory]
    [InlineData(BookingStatuses.Pending, true)]        // a request, or a quotation waiting for approval and payment
    [InlineData(BookingStatuses.Accepted, true)]       // paid / scheduled
    [InlineData(BookingStatuses.InProgress, true)]
    [InlineData(BookingStatuses.Completed, false)]
    [InlineData(BookingStatuses.Cancelled, false)]
    [InlineData(BookingStatuses.Rejected, false)]
    public async Task OnlyAFinishedBooking_LeavesTheProfileOpen(string status, bool locked)
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ana, status);

        var page = (await w.Svc.GetAsync(w.Ana.UserId)).Page!;

        Assert.Equal(locked, page.HasActiveBooking);
        Assert.Equal(locked ? 1 : 0, page.ActiveBookingsCount);
        Assert.Equal(locked, await w.Db.CustomerHasActiveBookingAsync(w.Ana.CustomerId));
    }

    [Fact]
    public async Task TheCount_IsOnlyThisCustomersUnfinishedBookings()
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ana, BookingStatuses.Pending);
        await BookAsync(w, w.Ana, BookingStatuses.Accepted);
        await BookAsync(w, w.Ana, BookingStatuses.Completed);
        await BookAsync(w, w.Ben, BookingStatuses.InProgress);

        Assert.Equal(2, (await w.Svc.GetAsync(w.Ana.UserId)).Page!.ActiveBookingsCount);
        Assert.Equal(1, (await w.Svc.GetAsync(w.Ben.UserId)).Page!.ActiveBookingsCount);
        Assert.Equal(new[] { BookingStatuses.Completed, BookingStatuses.Cancelled, BookingStatuses.Rejected }, CustomerProfileLock.TerminalStatuses);
    }

    // ------------------------------------------------------------------ the saves are refused

    [Fact]
    public async Task PersonalInformation_IsNotSaved_DuringAnActiveBooking()
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ana, BookingStatuses.Accepted);

        var result = await w.Svc.SaveAsync(w.Ana.UserId, Changed(w.Ana));

        Assert.True(result.Allowed);
        Assert.False(result.Saved);
        Assert.True(result.Locked);
        Assert.Equal("Profile information and saved address cannot be modified while you have an active booking.", result.Message);
        var user = await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == w.Ana.UserId);
        Assert.Equal(("Ana", "Cruz", "ana@test.com", "09171112222"), (user.FirstName, user.LastName, user.Email, user.PhoneNumber));
    }

    [Fact]
    public async Task TheAddress_IsNotSaved_DuringAnActiveBooking()
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ana, BookingStatuses.Pending);

        var result = await w.Svc.SaveAddressAsync(w.Ana.UserId, new CustomerAddressForm { Address = Banilad });

        Assert.True(result.Locked);
        Assert.False(result.Saved);
        Assert.Equal(Lahug, (await w.Db.Customers.AsNoTracking().SingleAsync(c => c.CustomerId == w.Ana.CustomerId)).Address);
    }

    [Fact]
    public async Task ThePassword_CanStillBeChanged_DuringAnActiveBooking()
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ana, BookingStatuses.InProgress);

        var result = await w.Svc.ChangePasswordAsync(w.Ana.UserId,
            new CustomerPasswordForm { CurrentPassword = Password, NewPassword = "NewPass456", ConfirmPassword = "NewPass456" });

        Assert.True(result.Saved);
    }

    [Fact]
    public async Task OnceTheBookingIsFinished_EverythingCanBeSavedAgain()
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ana, BookingStatuses.InProgress);
        Assert.True((await w.Svc.SaveAsync(w.Ana.UserId, Changed(w.Ana))).Locked);

        var booking = await w.Db.Bookings.SingleAsync();
        booking.Status = BookingStatuses.Completed;
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();

        Assert.False((await w.Svc.GetAsync(w.Ana.UserId)).Page!.HasActiveBooking);
        Assert.True((await w.Svc.SaveAsync(w.Ana.UserId, Changed(w.Ana))).Saved);
        Assert.True((await w.Svc.SaveAddressAsync(w.Ana.UserId, new CustomerAddressForm { Address = Banilad })).Saved);
        var user = await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == w.Ana.UserId);
        Assert.Equal(("Ana", "Santos", "09998887777"), (user.FirstName, user.LastName, user.PhoneNumber));
    }

    [Fact]
    public async Task AnotherCustomersBooking_DoesNotLockMe()
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ben, BookingStatuses.Accepted);

        Assert.True((await w.Svc.SaveAddressAsync(w.Ana.UserId, new CustomerAddressForm { Address = Banilad })).Saved);
        Assert.True((await w.Svc.SaveAddressAsync(w.Ben.UserId, new CustomerAddressForm { Address = Banilad })).Locked);
    }

    [Fact]
    public async Task TheRulesStillApply_WhenUnlocked()
    {
        // the +63 / 09 phone check and the 5 Cebu City addresses are unchanged
        var w = await CreateAsync();
        var badPhone = new CustomerProfileForm { FullName = "Ana Cruz", Email = "ana@test.com", PhoneNumber = "12345" };
        Assert.Equal(InputRules.PhoneMessage, (await w.Svc.SaveAsync(w.Ana.UserId, badPhone)).FieldErrors["PhoneNumber"]);
        Assert.Equal(CebuLocations.NotInListMessage,
            (await w.Svc.SaveAddressAsync(w.Ana.UserId, new CustomerAddressForm { Address = "Ayala Center, Makati" })).Message);
    }

    // ------------------------------------------------------------------ straight HTTP requests: 400

    private static ClaimsPrincipal SignedIn(int userId) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, Roles.Customer)
    }, "test"));

    [Fact]
    public async Task ThePutAddressApi_Answers400_DuringAnActiveBooking()
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ana, BookingStatuses.Accepted);
        var api = new CustomerAddressApiController(w.Svc)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = SignedIn(w.Ana.UserId) } }
        };

        var result = await api.Put(new AddressUpdateRequest(Banilad), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(CustomerProfileLock.SaveRefusedMessage, Assert.IsType<AddressApiMessage>(bad.Value).Message);
        Assert.Equal(Lahug, (await w.Db.Customers.AsNoTracking().SingleAsync(c => c.CustomerId == w.Ana.CustomerId)).Address);
    }

    [Fact]
    public async Task TheProfileForms_Answer400_AndShowTheMessage_DuringAnActiveBooking()
    {
        var w = await CreateAsync();
        await BookAsync(w, w.Ana, BookingStatuses.Pending);
        var http = new DefaultHttpContext { User = SignedIn(w.Ana.UserId) };
        var controller = new CustomerProfileController(w.Svc)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, new NoTempData())
        };

        var save = Assert.IsType<ViewResult>(await controller.Save(Changed(w.Ana), CancellationToken.None));
        Assert.Equal(400, http.Response.StatusCode);
        var page = Assert.IsType<CustomerProfilePageViewModel>(save.Model);
        Assert.Equal(CustomerProfileLock.SaveRefusedMessage, page.FormError);
        Assert.True(page.HasActiveBooking);
        Assert.Equal("Ana Cruz", page.Form.FullName);                     // the saved details, not what was sent

        http.Response.StatusCode = 200;
        var address = Assert.IsType<ViewResult>(await controller.SaveAddress(new CustomerAddressForm { Address = Banilad }, CancellationToken.None));
        Assert.Equal(400, http.Response.StatusCode);
        Assert.Equal(CustomerProfileLock.SaveRefusedMessage, Assert.IsType<CustomerProfilePageViewModel>(address.Model).AddressError);
    }

    private sealed class NoTempData : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // ------------------------------------------------------------------ the page

    [Fact]
    public void ThePage_LocksTheEditButtons_AndShowsTheNotice()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools"))) dir = dir.Parent;
        var view = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Servisyo.Web", "Views", "CustomerProfile", "Index.cshtml"));

        Assert.Contains("@CustomerProfileLock.NoticeText", view);
        Assert.Contains("is-disabled\" disabled aria-describedby=\"cp-lock\" data-cp-open hidden>Edit</button>", view);
        Assert.Contains("is-disabled\" disabled aria-describedby=\"cp-lock\" data-cp-open data-cp-address-open hidden>", view);
        Assert.Contains("asp-action=\"ChangePassword\"", view);                 // the password form is not inside any lock
        Assert.Equal("Profile editing is currently locked because you have an active service booking. Please wait until your booking is completed before updating your personal details or saved address.",
            CustomerProfileLock.NoticeText);
    }
}
