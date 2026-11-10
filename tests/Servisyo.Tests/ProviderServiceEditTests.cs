using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// "Edit Service" on My Services: name, category, description and price can change, but not while the service is
/// part of an active booking (Pending, Accepted or In Progress), so its price cannot be changed under a customer.
public class ProviderServiceEditTests
{
    private const string Locked = "Editing is locked while there are active bookings for this service to prevent price tampering.";

    private static (ProviderOfferingsService svc, ServisyoDbContext db) Create()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new ProviderOfferingsService(db), db);
    }

    private static User NewUser(string first, string role, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status
    };

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string approval = ApprovalStatuses.Approved,
        string account = AccountStatuses.Active, string first = "Vincent")
    {
        var p = new Provider { BusinessName = first + " Works", ApprovalStatus = approval, User = NewUser(first, Roles.ServiceProvider, account) };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<ServiceCategory> AddCategoryAsync(ServisyoDbContext db, string name, bool active = true)
    {
        var c = new ServiceCategory { CategoryName = name, IsActive = active };
        db.ServiceCategories.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static async Task<Service> AddServiceAsync(ServisyoDbContext db, Provider p, ServiceCategory c, string name = "Repairing Electronics",
        decimal price = 700m, int minutes = 180)
    {
        var s = new Service
        {
            ProviderId = p.ProviderId, CategoryId = c.CategoryId, ServiceName = name, Description = "Fast diagnostics.",
            Price = price, DurationMinutes = minutes, IsActive = true
        };
        db.Services.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    /// A booking of this service with the given status (the price it was booked at is kept in PRICE_SNAPSHOT).
    private static async Task<Booking> BookAsync(ServisyoDbContext db, Provider p, Service s, string status)
    {
        var customer = new Customer { User = NewUser("Ana", Roles.Customer) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var b = new Booking
        {
            CustomerId = customer.CustomerId, ProviderId = p.ProviderId, BookingDate = new DateOnly(2026, 11, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = s.DurationMinutes, TotalFee = s.Price, Status = status, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        };
        b.Services.Add(new BookingService { ServiceId = s.ServiceId, PriceSnapshot = s.Price, DurationMinutesSnapshot = s.DurationMinutes });
        db.Bookings.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    private static EditServiceForm FormFor(Service s, Action<EditServiceForm>? change = null)
    {
        var f = new EditServiceForm { ServiceName = s.ServiceName, Description = s.Description, CategoryId = s.CategoryId, Price = s.Price };
        change?.Invoke(f);
        return f;
    }

    private static Task<Service> ReloadAsync(ServisyoDbContext db, int serviceId) =>
        db.Services.AsNoTracking().SingleAsync(s => s.ServiceId == serviceId);

    // ------------------------------------------------------------------ editing

    [Fact]
    public async Task Edit_ChangesTitleCategoryDescriptionAndPrice_KeepsTheDuration_AndLinksTheNewCategory()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var cleaning = await AddCategoryAsync(db, "Cleaning");
        var me = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, me, repair);

        var result = await svc.UpdateAsync(me.UserId, s.ServiceId, new EditServiceForm
        {
            ServiceName = "  Electronics Cleaning  ", Description = "  Dust removal.  ", CategoryId = cleaning.CategoryId, Price = 850.50m
        });

        Assert.Equal(EditServiceOutcome.Saved, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Equal("“Electronics Cleaning” was updated.", result.Message);
        var saved = await ReloadAsync(db, s.ServiceId);
        Assert.Equal("Electronics Cleaning", saved.ServiceName);
        Assert.Equal("Dust removal.", saved.Description);
        Assert.Equal(cleaning.CategoryId, saved.CategoryId);
        Assert.Equal(850.50m, saved.Price);
        Assert.Equal(180, saved.DurationMinutes);                                      // not edited here
        Assert.Contains(db.ProviderCategories, pc => pc.ProviderId == me.ProviderId && pc.CategoryId == cleaning.CategoryId);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task Edit_IsLocked_WhileTheServiceHasAnActiveBooking(string status)
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, me, repair);
        await BookAsync(db, me, s, status);

        var result = await svc.UpdateAsync(me.UserId, s.ServiceId, FormFor(s, f => f.Price = 9999m));

        Assert.Equal(EditServiceOutcome.Locked, result.Outcome);
        Assert.Equal(Locked, result.Message);
        Assert.Equal(700m, (await ReloadAsync(db, s.ServiceId)).Price);               // nothing changed

        var card = Assert.Single((await svc.GetAsync(me.UserId)).Page!.Cards);
        Assert.True(card.EditLocked);
    }

    [Theory]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.Rejected)]
    public async Task Edit_IsAllowed_WhenEveryBookingIsFinished_AndThoseBookingsKeepTheirPrice(string status)
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, me, repair);
        var booking = await BookAsync(db, me, s, status);

        Assert.False(Assert.Single((await svc.GetAsync(me.UserId)).Page!.Cards).EditLocked);
        var result = await svc.UpdateAsync(me.UserId, s.ServiceId, FormFor(s, f => f.Price = 900m));

        Assert.Equal(EditServiceOutcome.Saved, result.Outcome);
        Assert.Equal(900m, (await ReloadAsync(db, s.ServiceId)).Price);
        Assert.Equal(700m, (await db.BookingServices.AsNoTracking().SingleAsync(bs => bs.BookingId == booking.BookingId)).PriceSnapshot);
        Assert.Equal(700m, (await db.Bookings.AsNoTracking().SingleAsync()).TotalFee);
    }

    [Fact]
    public async Task AnActiveBookingOfAnotherService_DoesNotLockThisOne()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        var busy = await AddServiceAsync(db, me, repair, "Plumbing Works", 550m);
        var free = await AddServiceAsync(db, me, repair, "Roofing", 1200m);
        await BookAsync(db, me, busy, BookingStatuses.Accepted);

        var cards = (await svc.GetAsync(me.UserId)).Page!.Cards;
        Assert.True(cards.Single(c => c.ServiceId == busy.ServiceId).EditLocked);
        Assert.False(cards.Single(c => c.ServiceId == free.ServiceId).EditLocked);
        Assert.Equal(EditServiceOutcome.Saved, (await svc.UpdateAsync(me.UserId, free.ServiceId, FormFor(free, f => f.Price = 1300m))).Outcome);
        Assert.Equal(EditServiceOutcome.Locked, (await svc.UpdateAsync(me.UserId, busy.ServiceId, FormFor(busy, f => f.Price = 600m))).Outcome);
    }

    [Fact]
    public async Task Edit_BadInput_IsRefusedFieldByField_AndNothingIsSaved()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var off = await AddCategoryAsync(db, "Old Category", active: false);
        var me = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, me, repair);

        var cases = new (string Field, Action<EditServiceForm> Break)[]
        {
            ("ServiceName", f => f.ServiceName = "   "),
            ("ServiceName", f => f.ServiceName = null),
            ("ServiceName", f => f.ServiceName = new string('x', ProviderOfferingsService.MaxNameLength + 1)),
            ("Description", f => f.Description = new string('x', ProviderOfferingsService.MaxDescriptionLength + 1)),
            ("CategoryId", f => f.CategoryId = null),
            ("CategoryId", f => f.CategoryId = 0),
            ("CategoryId", f => f.CategoryId = 99999),
            ("CategoryId", f => f.CategoryId = off.CategoryId),          // switched off by an administrator
            ("Price", f => f.Price = null),
            ("Price", f => f.Price = -1m),
            ("Price", f => f.Price = ProviderOfferingsService.MaxPrice + 1m),
            ("Price", f => f.Price = 10.005m)
        };

        foreach (var (field, makeBad) in cases)
        {
            var result = await svc.UpdateAsync(me.UserId, s.ServiceId, FormFor(s, makeBad));

            Assert.Equal(EditServiceOutcome.Invalid, result.Outcome);
            Assert.Equal(field, Assert.Single(result.FieldErrors.Keys));
        }

        var saved = await ReloadAsync(db, s.ServiceId);
        Assert.Equal("Repairing Electronics", saved.ServiceName);
        Assert.Equal(700m, saved.Price);
        Assert.Equal(repair.CategoryId, saved.CategoryId);
    }

    [Fact]
    public async Task Edit_MayKeepItsOwnName_ButNotTakeAnotherServicesName()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, me, repair, "Repairing Electronics");
        await AddServiceAsync(db, me, repair, "Plumbing Works");

        var own = await svc.UpdateAsync(me.UserId, s.ServiceId, FormFor(s, f => f.ServiceName = "REPAIRING electronics"));
        Assert.Equal(EditServiceOutcome.Saved, own.Outcome);

        var taken = await svc.UpdateAsync(me.UserId, s.ServiceId, FormFor(s, f => f.ServiceName = "plumbing works"));
        Assert.Equal(EditServiceOutcome.Invalid, taken.Outcome);
        Assert.Equal("You already have a service with this name.", taken.FieldErrors["ServiceName"]);
    }

    [Fact]
    public async Task Edit_MayKeepACategoryThatWasSwitchedOff_ButCannotPickASwitchedOffOne()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, me, repair);
        var old = await AddCategoryAsync(db, "Old", active: false);
        repair.IsActive = false;                                                  // switched off after the service was added
        await db.SaveChangesAsync();

        Assert.Equal(EditServiceOutcome.Saved, (await svc.UpdateAsync(me.UserId, s.ServiceId, FormFor(s, f => f.Price = 750m))).Outcome);
        var other = await svc.UpdateAsync(me.UserId, s.ServiceId, FormFor(s, f => f.CategoryId = old.CategoryId));
        Assert.Equal("That category is not available. Choose another one.", other.FieldErrors["CategoryId"]);
    }

    [Fact]
    public async Task Edit_WithNothingChanged_SavesNothing()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, me, repair);

        var result = await svc.UpdateAsync(me.UserId, s.ServiceId, FormFor(s));

        Assert.Equal(EditServiceOutcome.NoChange, result.Outcome);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AProvider_CannotEditSomeoneElsesService_OrOneThatDoesNotExist()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, first: "Marco");
        var theirs = await AddServiceAsync(db, other, repair);

        var result = await svc.UpdateAsync(me.UserId, theirs.ServiceId, FormFor(theirs, f => f.Price = 1m));
        Assert.Equal(EditServiceOutcome.NotFound, result.Outcome);
        Assert.Equal(700m, (await ReloadAsync(db, theirs.ServiceId)).Price);

        Assert.Equal(EditServiceOutcome.NotFound, (await svc.UpdateAsync(me.UserId, 99999, FormFor(theirs))).Outcome);
    }

    [Theory]
    [InlineData(ApprovalStatuses.Pending, AccountStatuses.Active, ProviderDashboardAccess.Pending)]
    [InlineData(ApprovalStatuses.Approved, AccountStatuses.Suspended, ProviderDashboardAccess.NotAllowed)]
    [InlineData(ApprovalStatuses.Approved, AccountStatuses.Deactivated, ProviderDashboardAccess.NotAllowed)]
    public async Task OnlyAnActiveApprovedProvider_CanEdit(string approval, string account, ProviderDashboardAccess expected)
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var p = await AddProviderAsync(db, approval, account);
        var s = await AddServiceAsync(db, p, repair);

        var result = await svc.UpdateAsync(p.UserId, s.ServiceId, FormFor(s, f => f.Price = 1m));

        Assert.Equal(expected, result.Access);
        Assert.Equal(700m, (await ReloadAsync(db, s.ServiceId)).Price);
    }

    [Fact]
    public async Task ARejectedEdit_ReopensTheEditDialog_WithWhatWasTyped_ButNotForALockedService()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, me, repair);
        var typed = FormFor(s, f => f.Price = -5m);
        var rejected = await svc.UpdateAsync(me.UserId, s.ServiceId, typed);

        var page = (await svc.GetWithRejectedEditAsync(me.UserId, s.ServiceId, typed, rejected)).Page!;
        Assert.True(page.ShowEditDialog);
        Assert.Equal(s.ServiceId, page.EditServiceId);
        Assert.Equal(-5m, page.EditForm.Price);
        Assert.Equal("The price cannot be negative.", page.EditErrorFor("Price"));
        Assert.False(page.ShowAddDialog);

        await BookAsync(db, me, s, BookingStatuses.Pending);                   // locked meanwhile: just the list
        Assert.False((await svc.GetWithRejectedEditAsync(me.UserId, s.ServiceId, typed, rejected)).Page!.ShowEditDialog);
    }

    [Fact]
    public async Task TheCards_CarryTheCategoryForTheEditDialog()
    {
        var (svc, db) = Create();
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);
        await AddServiceAsync(db, me, repair);

        var card = Assert.Single((await svc.GetAsync(me.UserId)).Page!.Cards);
        Assert.Equal(repair.CategoryId, card.CategoryId);
        Assert.Equal("Repair", card.CategoryName);
    }
}
