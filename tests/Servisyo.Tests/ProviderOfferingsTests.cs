using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ServiceFormatTests
{
    [Theory]
    [InlineData(800.0, "₱800")]
    [InlineData(1500.0, "₱1,500")]
    [InlineData(0.0, "₱0")]
    [InlineData(850.5, "₱850.50")]
    [InlineData(1234567.89, "₱1,234,567.89")]
    public void Peso_HidesZeroCentsOnly(double price, string expected)
        => Assert.Equal(expected, ServiceFormat.Peso((decimal)price));

    [Theory]
    [InlineData(30, "30 min")]
    [InlineData(60, "1 hr")]
    [InlineData(90, "1 hr 30 min")]
    [InlineData(120, "2 hrs")]
    [InlineData(480, "8 hrs")]
    public void Duration_IsWrittenShort(int minutes, string expected)
        => Assert.Equal(expected, ServiceFormat.Duration(minutes));

    [Fact]
    public void TheDefaultDuration_IsOneOfTheChoices()
        => Assert.Contains(ServiceDurations.Default, ServiceDurations.Choices);
}

public class ServiceCardViewModelTests
{
    [Fact]
    public void ActiveCard_IsGreen_AndShowsPriceDurationAndCategory()
    {
        var card = new ServiceCardViewModel { IsActive = true, Price = 800m, DurationMinutes = 120, CategoryName = "Plumbing" };

        Assert.Equal("Active", card.StatusText);
        Assert.Equal("completed", card.StatusTone);
        Assert.Equal("₱800", card.PriceText);
        Assert.Equal("2 hrs · Plumbing", card.MetaText);
    }

    [Fact]
    public void InactiveCard_IsGrey()
    {
        var card = new ServiceCardViewModel { IsActive = false, Price = 1500m, DurationMinutes = 60 };

        Assert.Equal("Inactive", card.StatusText);
        Assert.Equal("muted", card.StatusTone);
        Assert.Equal("1 hr", card.MetaText);            // no category name: nothing dangling after the duration
    }
}

public class ProviderOfferingsServiceTests
{
    private static (ProviderOfferingsService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new ProviderOfferingsService(db), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string approval = ApprovalStatuses.Approved,
        string account = AccountStatuses.Active, string first = "Marco", string last = "Reyes", string business = "Marco's Plumbing Works")
    {
        var p = new Provider { BusinessName = business, ApprovalStatus = approval, User = NewUser(first, last, Roles.ServiceProvider, account) };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<ServiceCategory> AddCategoryAsync(ServisyoDbContext db, string name = "Plumbing", bool active = true)
    {
        var c = new ServiceCategory { CategoryName = name, IsActive = active };
        db.ServiceCategories.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static async Task<Service> AddServiceAsync(ServisyoDbContext db, Provider p, ServiceCategory c, string name,
        decimal price = 500m, bool active = true, string? description = null, int minutes = 60)
    {
        var s = new Service
        {
            ProviderId = p.ProviderId, CategoryId = c.CategoryId, ServiceName = name, Description = description,
            Price = price, DurationMinutes = minutes, IsActive = active
        };
        db.Services.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    private static AddServiceForm ValidForm(int categoryId) => new()
    {
        ServiceName = "Pipe Repair", Description = "Fix leaking or burst pipes.", CategoryId = categoryId, Price = 800m, DurationMinutes = 120
    };

    // ----------------------------------------------------------------- access

    [Fact]
    public async Task OnlyAnActiveApprovedProvider_CanUseTheFeature()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator);
        db.Customers.Add(customer);
        db.Users.Add(admin);
        var pending = await AddProviderAsync(db, ApprovalStatuses.Pending);
        var rejected = await AddProviderAsync(db, ApprovalStatuses.Rejected);
        var suspended = await AddProviderAsync(db, account: AccountStatuses.Suspended);
        var deactivated = await AddProviderAsync(db, account: AccountStatuses.Deactivated);
        var theirService = await AddServiceAsync(db, pending, cat, "Existing", active: true);
        await db.SaveChangesAsync();

        var expected = new (int UserId, ProviderDashboardAccess Access)[]
        {
            (customer.UserId, ProviderDashboardAccess.NotAllowed),
            (admin.UserId, ProviderDashboardAccess.NotAllowed),
            (99999, ProviderDashboardAccess.NotAllowed),
            (suspended.UserId, ProviderDashboardAccess.NotAllowed),
            (deactivated.UserId, ProviderDashboardAccess.NotAllowed),
            (pending.UserId, ProviderDashboardAccess.Pending),
            (rejected.UserId, ProviderDashboardAccess.Rejected)
        };

        foreach (var (userId, access) in expected)
        {
            Assert.Equal(access, (await svc.GetAsync(userId)).Access);

            var add = await svc.AddAsync(userId, ValidForm(cat.CategoryId));
            Assert.Equal(access, add.Access);
            Assert.False(add.Added);

            var toggle = await svc.SetActiveAsync(userId, theirService.ServiceId, false);
            Assert.Equal(access, toggle.Access);
            Assert.False(toggle.Succeeded);
        }

        Assert.Single(db.Services);                                           // only the one that was there before
        Assert.True((await db.Services.AsNoTracking().SingleAsync()).IsActive);
        Assert.Empty(db.ProviderCategories);
    }

    // ------------------------------------------------------------------- list

    [Fact]
    public async Task Page_ListsOnlyMyServices_ActiveFirst_InTheOrderTheyWereAdded()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, first: "Liza", last: "Santos", business: "Liza Clean");

        await AddServiceAsync(db, me, cat, "Pipe Repair", 800m, description: "Fix leaking or burst pipes.", minutes: 120);
        await AddServiceAsync(db, me, cat, "Water Heater Install", 1500m, active: false);
        await AddServiceAsync(db, me, cat, "Fixture Install", 600m);
        await AddServiceAsync(db, other, cat, "Someone Else's Service");

        var page = (await svc.GetAsync(me.UserId)).Page!;

        Assert.Equal(new[] { "Pipe Repair", "Fixture Install", "Water Heater Install" }, page.Cards.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { true, true, false }, page.Cards.Select(c => c.IsActive).ToArray());
        Assert.Equal("Fix leaking or burst pipes.", page.Cards[0].Description);
        Assert.Null(page.Cards[1].Description);
        Assert.Equal("₱800", page.Cards[0].PriceText);
        Assert.Equal("Plumbing", page.Cards[0].CategoryName);
        Assert.Equal("MR", page.Shell.Initials);
        Assert.Equal("Provider", page.Shell.Role);
        Assert.Equal("Marco's Plumbing Works", page.Shell.Subtitle);
        Assert.False(page.ShowAddDialog);
    }

    [Fact]
    public async Task Page_OfferOnlyActiveCategories_ByName()
    {
        var (svc, db) = Create();
        var me = await AddProviderAsync(db);
        await AddCategoryAsync(db, "Repair");
        await AddCategoryAsync(db, "Cleaning");
        await AddCategoryAsync(db, "Old Category", active: false);

        var page = (await svc.GetAsync(me.UserId)).Page!;

        Assert.Equal(new[] { "Cleaning", "Repair" }, page.Categories.Select(c => c.Name).ToArray());
        Assert.Equal(ServiceDurations.Choices, page.DurationOptions.Select(d => d.Minutes).ToArray());
        Assert.Empty(page.Cards);
    }

    // ------------------------------------------------------------------- add

    [Fact]
    public async Task Add_SavesTheService_AndLinksTheProviderToTheCategory()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);

        var result = await svc.AddAsync(me.UserId, new AddServiceForm
        {
            ServiceName = "  Pipe Repair  ", Description = "  Fix leaks.  ", CategoryId = cat.CategoryId, Price = 800m, DurationMinutes = 120
        });

        Assert.True(result.Added);
        Assert.Empty(result.FieldErrors);
        Assert.Equal("“Pipe Repair” was added to your services.", result.Message);

        var saved = await db.Services.AsNoTracking().SingleAsync();
        Assert.Equal(me.ProviderId, saved.ProviderId);
        Assert.Equal(cat.CategoryId, saved.CategoryId);
        Assert.Equal("Pipe Repair", saved.ServiceName);
        Assert.Equal("Fix leaks.", saved.Description);
        Assert.Equal(800m, saved.Price);
        Assert.Equal(120, saved.DurationMinutes);
        Assert.True(saved.IsActive);

        var link = await db.ProviderCategories.AsNoTracking().SingleAsync();
        Assert.Equal(me.ProviderId, link.ProviderId);
        Assert.Equal(cat.CategoryId, link.CategoryId);
    }

    [Fact]
    public async Task Add_NeverLinksTheSameCategoryTwice()
    {
        var (svc, db) = Create();
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var repair = await AddCategoryAsync(db, "Repair");
        var me = await AddProviderAsync(db);

        var first = ValidForm(plumbing.CategoryId);
        var second = ValidForm(plumbing.CategoryId); second.ServiceName = "Fixture Install";
        var third = ValidForm(repair.CategoryId); third.ServiceName = "Carpentry";
        foreach (var form in new[] { first, second, third })
            Assert.True((await svc.AddAsync(me.UserId, form)).Added);

        Assert.Equal(3, await db.Services.CountAsync());
        Assert.Equal(2, await db.ProviderCategories.CountAsync());     // Plumbing once, Repair once
    }

    [Fact]
    public async Task Add_AnotherProvidersCategoryLink_IsNotAffected()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, first: "Liza", last: "Santos", business: "Liza Clean");
        db.ProviderCategories.Add(new ProviderCategory { ProviderId = other.ProviderId, CategoryId = cat.CategoryId });
        await db.SaveChangesAsync();

        Assert.True((await svc.AddAsync(me.UserId, ValidForm(cat.CategoryId))).Added);

        Assert.Equal(2, await db.ProviderCategories.CountAsync());      // theirs and mine
    }

    [Fact]
    public async Task Add_BadInput_IsRefusedFieldByField_AndNothingIsSaved()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var off = await AddCategoryAsync(db, "Old Category", active: false);
        var me = await AddProviderAsync(db);

        var cases = new (string Field, Action<AddServiceForm> Break)[]
        {
            ("ServiceName", f => f.ServiceName = "   "),
            ("ServiceName", f => f.ServiceName = null),
            ("ServiceName", f => f.ServiceName = new string('x', ProviderOfferingsService.MaxNameLength + 1)),
            ("Description", f => f.Description = new string('x', ProviderOfferingsService.MaxDescriptionLength + 1)),
            ("CategoryId", f => f.CategoryId = null),
            ("CategoryId", f => f.CategoryId = 0),
            ("CategoryId", f => f.CategoryId = 99999),
            ("CategoryId", f => f.CategoryId = off.CategoryId),          // disabled by an administrator
            ("Price", f => f.Price = null),
            ("Price", f => f.Price = -1m),
            ("Price", f => f.Price = ProviderOfferingsService.MaxPrice + 1m),
            ("Price", f => f.Price = 10.005m),
            ("DurationMinutes", f => f.DurationMinutes = null),
            ("DurationMinutes", f => f.DurationMinutes = 0),
            ("DurationMinutes", f => f.DurationMinutes = 45),
            ("DurationMinutes", f => f.DurationMinutes = 10_000)
        };

        foreach (var (field, makeBad) in cases)
        {
            var form = ValidForm(cat.CategoryId);
            makeBad(form);

            var result = await svc.AddAsync(me.UserId, form);

            Assert.False(result.Added);
            Assert.Equal(field, Assert.Single(result.FieldErrors.Keys));   // exactly the one broken box is flagged
            Assert.Contains("fix the highlighted fields", result.Message);
        }

        Assert.Empty(db.Services);
        Assert.Empty(db.ProviderCategories);
    }

    [Fact]
    public async Task Add_AcceptsTheLimits()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);

        var form = ValidForm(cat.CategoryId);
        form.ServiceName = new string('n', ProviderOfferingsService.MaxNameLength);
        form.Description = new string('d', ProviderOfferingsService.MaxDescriptionLength);
        form.Price = ProviderOfferingsService.MaxPrice;
        Assert.True((await svc.AddAsync(me.UserId, form)).Added);

        var free = ValidForm(cat.CategoryId);
        free.ServiceName = "Free Estimate";
        free.Description = null;
        free.Price = 0m;
        Assert.True((await svc.AddAsync(me.UserId, free)).Added);

        Assert.Equal(2, await db.Services.CountAsync());
    }

    [Fact]
    public async Task Add_RefusesTheSameNameTwice_IgnoringCaseAndSpaces_ButOtherProvidersMayUseIt()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, first: "Liza", last: "Santos", business: "Liza Clean");

        Assert.True((await svc.AddAsync(me.UserId, ValidForm(cat.CategoryId))).Added);

        var again = ValidForm(cat.CategoryId);
        again.ServiceName = "  pipe REPAIR ";
        var result = await svc.AddAsync(me.UserId, again);
        Assert.False(result.Added);
        Assert.Contains("ServiceName", result.FieldErrors.Keys);

        Assert.True((await svc.AddAsync(other.UserId, ValidForm(cat.CategoryId))).Added);
        Assert.Equal(2, await db.Services.CountAsync());
    }

    [Fact]
    public async Task Add_AnInactiveServiceWithTheSameNameStillCountsAsADuplicate()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);
        await AddServiceAsync(db, me, cat, "Pipe Repair", active: false);

        var result = await svc.AddAsync(me.UserId, ValidForm(cat.CategoryId));

        Assert.False(result.Added);
        Assert.Contains("ServiceName", result.FieldErrors.Keys);
    }

    [Fact]
    public async Task RejectedForm_ReopensTheDialog_WithWhatWasTypedAndTheProblems()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);
        var form = ValidForm(cat.CategoryId);
        form.Price = -5m;

        var rejected = await svc.AddAsync(me.UserId, form);
        var page = (await svc.GetAsync(me.UserId, form, rejected)).Page!;

        Assert.True(page.ShowAddDialog);
        Assert.Equal("Pipe Repair", page.Form.ServiceName);
        Assert.Equal(-5m, page.Form.Price);
        Assert.NotNull(page.ErrorFor("Price"));
        Assert.Null(page.ErrorFor("ServiceName"));
        Assert.Null(page.FormError);                    // the problem is on a field, not general
    }

    // ------------------------------------------------------- activate / deactivate

    [Fact]
    public async Task Deactivate_ThenActivate_ChangesTheStatus()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);
        var service = await AddServiceAsync(db, me, cat, "Pipe Repair");

        var off = await svc.SetActiveAsync(me.UserId, service.ServiceId, false);
        Assert.True(off.Succeeded);
        Assert.Contains("deactivated", off.Message);
        Assert.False((await db.Services.AsNoTracking().SingleAsync()).IsActive);

        var on = await svc.SetActiveAsync(me.UserId, service.ServiceId, true);
        Assert.True(on.Succeeded);
        Assert.Contains("active again", on.Message);
        Assert.True((await db.Services.AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task AskingForTheStateItAlreadyHas_ChangesNothing()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);
        var active = await AddServiceAsync(db, me, cat, "Pipe Repair", active: true);
        var inactive = await AddServiceAsync(db, me, cat, "Water Heater Install", active: false);

        var a = await svc.SetActiveAsync(me.UserId, active.ServiceId, true);
        var b = await svc.SetActiveAsync(me.UserId, inactive.ServiceId, false);

        Assert.Equal(ServiceStatusOutcome.NoChange, a.Outcome);
        Assert.Contains("already active", a.Message);
        Assert.Equal(ServiceStatusOutcome.NoChange, b.Outcome);
        Assert.Contains("already inactive", b.Message);
    }

    [Fact]
    public async Task AProvider_CannotChangeSomeoneElsesService()
    {
        var (svc, db) = Create();
        var cat = await AddCategoryAsync(db);
        var me = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, first: "Liza", last: "Santos", business: "Liza Clean");
        var theirs = await AddServiceAsync(db, other, cat, "Deep Cleaning");

        var result = await svc.SetActiveAsync(me.UserId, theirs.ServiceId, false);

        Assert.Equal(ServiceStatusOutcome.NotFound, result.Outcome);       // looks exactly like a missing service
        Assert.True((await db.Services.AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task UnknownService_IsNotFound()
    {
        var (svc, db) = Create();
        var me = await AddProviderAsync(db);

        var result = await svc.SetActiveAsync(me.UserId, 99999, true);

        Assert.Equal(ServiceStatusOutcome.NotFound, result.Outcome);
        Assert.Contains("no longer exists", result.Message);
    }
}
