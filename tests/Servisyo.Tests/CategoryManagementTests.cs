using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class CategoryRowViewModelTests
{
    [Theory]
    [InlineData(true, "Active", "completed")]
    [InlineData(false, "Disabled", "muted")]
    public void Status_IsShownAsGreenActiveOrGreyDisabled(bool isActive, string text, string tone)
    {
        var row = new CategoryRowViewModel { IsActive = isActive };
        Assert.Equal(text, row.StatusText);
        Assert.Equal(tone, row.StatusTone);
    }
}

public class CategoryManagementServiceTests
{
    private static (CategoryManagementService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new CategoryManagementService(db), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator, status);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    private static Provider NewProvider(string approval = ApprovalStatuses.Approved)
        => new()
        {
            BusinessName = "Biz " + Guid.NewGuid().ToString("N")[..6], ApprovalStatus = approval,
            User = NewUser("Pro", "Vider", Roles.ServiceProvider)
        };

    private static ServiceCategory NewCategory(string name, string? description = null, bool active = true)
        => new() { CategoryName = name, Description = description, IsActive = active };

    private static Booking NewBooking(Customer c, Provider p, params Service[] services)
    {
        var b = new Booking
        {
            Customer = c, Provider = p, BookingDate = new DateOnly(2026, 9, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 500m, Status = BookingStatuses.Completed, RowVersion = new byte[] { 1 }
        };
        foreach (var s in services)
            b.Services.Add(new BookingService { Service = s, PriceSnapshot = s.Price, DurationMinutesSnapshot = 60 });
        return b;
    }

    // ---------------------------------------------------------------- list

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanSeeTheList()
    {
        var (svc, db) = Create();
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        var provider = NewProvider();
        db.Customers.Add(customer);
        db.Providers.Add(provider);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        Assert.Null(await svc.GetCategoriesAsync(customer.UserId));
        Assert.Null(await svc.GetCategoriesAsync(provider.UserId));
        Assert.Null(await svc.GetCategoriesAsync(suspendedAdmin.UserId));
        Assert.Null(await svc.GetCategoriesAsync(99999));
    }

    [Fact]
    public async Task NoCategories_ShowsAnEmptyList()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var vm = (await svc.GetCategoriesAsync(admin.UserId))!;

        Assert.Empty(vm.Rows);
        Assert.Equal(0, vm.ActiveCount);
        Assert.Equal(0, vm.DisabledCount);
        Assert.False(vm.ShowAddForm);
    }

    [Fact]
    public async Task List_CountsActiveAndDisabled_ActiveFirst_ThenInCreationOrder()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        // created in this order; the two disabled ones are created in between on purpose
        db.ServiceCategories.AddRange(
            NewCategory("Plumbing"), NewCategory("Electrical", active: false),
            NewCategory("Cleaning"), NewCategory("Landscaping", active: false), NewCategory("Repair"));
        await db.SaveChangesAsync();

        var vm = (await svc.GetCategoriesAsync(admin.UserId))!;

        Assert.Equal(3, vm.ActiveCount);
        Assert.Equal(2, vm.DisabledCount);
        Assert.Equal(new[] { "Plumbing", "Cleaning", "Repair", "Electrical", "Landscaping" }, vm.Rows.Select(r => r.Name));
        Assert.Equal(new[] { true, true, true, false, false }, vm.Rows.Select(r => r.IsActive));
    }

    [Fact]
    public async Task List_CountsApprovedProviders_AndDistinctBookings()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var plumbing = NewCategory("Plumbing");
        var cleaning = NewCategory("Cleaning");
        var repair = NewCategory("Repair");
        var approvedA = NewProvider();
        var approvedB = NewProvider();
        var pending = NewProvider(ApprovalStatuses.Pending);
        db.ProviderCategories.AddRange(
            new ProviderCategory { Provider = approvedA, Category = plumbing },
            new ProviderCategory { Provider = approvedB, Category = plumbing },
            new ProviderCategory { Provider = pending, Category = plumbing },      // not approved: not counted
            new ProviderCategory { Provider = approvedA, Category = cleaning });
        db.ServiceCategories.Add(repair);

        var pipe = new Service { Provider = approvedA, Category = plumbing, ServiceName = "Pipe Repair", Price = 850m };
        var drain = new Service { Provider = approvedA, Category = plumbing, ServiceName = "Drain Unclogging", Price = 650m };
        var deep = new Service { Provider = approvedA, Category = cleaning, ServiceName = "Deep Cleaning", Price = 1200m };
        var ana = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Bookings.AddRange(
            NewBooking(ana, approvedA, pipe),               // plumbing
            NewBooking(ana, approvedA, pipe, drain),         // two plumbing services: still ONE booking for plumbing
            NewBooking(ana, approvedA, pipe, deep));         // counts for plumbing AND cleaning
        await db.SaveChangesAsync();

        var vm = (await svc.GetCategoriesAsync(admin.UserId))!;

        var p = vm.Rows.Single(r => r.Name == "Plumbing");
        var c = vm.Rows.Single(r => r.Name == "Cleaning");
        var r2 = vm.Rows.Single(r => r.Name == "Repair");
        Assert.Equal(2, p.Providers);
        Assert.Equal(3, p.Bookings);
        Assert.Equal(1, c.Providers);
        Assert.Equal(1, c.Bookings);
        Assert.Equal(0, r2.Providers);
        Assert.Equal(0, r2.Bookings);
    }

    // -------------------------------------------------------------- create

    [Fact]
    public async Task Create_SavesTrimmedCategory_AsActive_AndLogsIt()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var result = await svc.CreateAsync(admin.UserId, "  Landscaping  ", "  Garden maintenance and lawn care.  ");

        Assert.True(result.Succeeded);
        var saved = await db.ServiceCategories.SingleAsync();
        Assert.Equal("Landscaping", saved.CategoryName);
        Assert.Equal("Garden maintenance and lawn care.", saved.Description);
        Assert.True(saved.IsActive);

        var log = await db.AdminActions.SingleAsync();
        Assert.Equal(admin.UserId, log.AdminUserId);
        Assert.Equal("Category created", log.ActionType);
        Assert.Equal("CATEGORY", log.TargetType);
        Assert.Equal(saved.CategoryId, log.TargetId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutDescription_StoresNull(string? description)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        Assert.True((await svc.CreateAsync(admin.UserId, "Plumbing", description)).Succeeded);

        Assert.Null((await db.ServiceCategories.SingleAsync()).Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Create_RequiresAName(string? name)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var result = await svc.CreateAsync(admin.UserId, name, "x");

        Assert.Equal(CategoryOutcome.Invalid, result.Outcome);
        Assert.Empty(db.ServiceCategories);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task Create_RejectsTooLongNameOrDescription_ButAcceptsTheExactLimit()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var tooLongName = await svc.CreateAsync(admin.UserId, new string('a', 101), null);
        var tooLongDescription = await svc.CreateAsync(admin.UserId, "Plumbing", new string('d', 256));
        var atTheLimit = await svc.CreateAsync(admin.UserId, new string('a', 100), new string('d', 255));

        Assert.Equal(CategoryOutcome.Invalid, tooLongName.Outcome);
        Assert.Equal(CategoryOutcome.Invalid, tooLongDescription.Outcome);
        Assert.True(atTheLimit.Succeeded);
        Assert.Single(db.ServiceCategories);
    }

    [Theory]
    [InlineData("Plumbing")]
    [InlineData("plumbing")]
    [InlineData("  PLUMBING  ")]
    public async Task Create_RejectsADuplicateName_IgnoringCaseAndSpaces(string name)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.ServiceCategories.Add(NewCategory("Plumbing"));
        await db.SaveChangesAsync();

        var result = await svc.CreateAsync(admin.UserId, name, null);

        Assert.Equal(CategoryOutcome.Duplicate, result.Outcome);
        Assert.Single(db.ServiceCategories);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task Create_RejectsADuplicateOfADisabledCategoryToo()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.ServiceCategories.Add(NewCategory("Electrical", active: false));
        await db.SaveChangesAsync();

        Assert.Equal(CategoryOutcome.Duplicate, (await svc.CreateAsync(admin.UserId, "electrical", null)).Outcome);
    }

    [Fact]
    public async Task Create_IsRefusedForEveryoneButAnActiveAdministrator()
    {
        var (svc, db) = Create();
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        var provider = NewProvider();
        db.Customers.Add(customer);
        db.Providers.Add(provider);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        foreach (var userId in new[] { customer.UserId, provider.UserId, suspendedAdmin.UserId, 99999 })
            Assert.Equal(CategoryOutcome.NotAllowed, (await svc.CreateAsync(userId, "Landscaping", null)).Outcome);

        Assert.Empty(db.ServiceCategories);
        Assert.Empty(db.AdminActions);
    }

    // -------------------------------------------------------------- update

    [Fact]
    public async Task Update_ChangesNameAndDescription_AndLogsIt()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var cat = NewCategory("Plumbin", "old");
        db.ServiceCategories.Add(cat);
        await db.SaveChangesAsync();

        var result = await svc.UpdateAsync(admin.UserId, cat.CategoryId, " Plumbing ", " Pipes and fixtures. ");

        Assert.True(result.Succeeded);
        var saved = await db.ServiceCategories.SingleAsync();
        Assert.Equal("Plumbing", saved.CategoryName);
        Assert.Equal("Pipes and fixtures.", saved.Description);

        var log = await db.AdminActions.SingleAsync();
        Assert.Equal("Category updated", log.ActionType);
        Assert.Equal("CATEGORY", log.TargetType);
        Assert.Equal(cat.CategoryId, log.TargetId);
        Assert.Equal(admin.UserId, log.AdminUserId);
    }

    [Fact]
    public async Task Update_KeepingTheSameNameIsFine_AndOnlyTheCaseMayChange()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var cat = NewCategory("plumbing", "old");
        db.ServiceCategories.Add(cat);
        await db.SaveChangesAsync();

        var sameNameNewDescription = await svc.UpdateAsync(admin.UserId, cat.CategoryId, "plumbing", "new");
        var onlyCase = await svc.UpdateAsync(admin.UserId, cat.CategoryId, "Plumbing", "new");

        Assert.True(sameNameNewDescription.Succeeded);   // the category does not clash with itself
        Assert.True(onlyCase.Succeeded);
        Assert.Equal("Plumbing", (await db.ServiceCategories.SingleAsync()).CategoryName);
    }

    [Fact]
    public async Task Update_WithNoRealChange_SaysSo_AndWritesNoLog()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var cat = NewCategory("Plumbing", "Pipes.");
        db.ServiceCategories.Add(cat);
        await db.SaveChangesAsync();

        var result = await svc.UpdateAsync(admin.UserId, cat.CategoryId, "Plumbing", "Pipes.");

        Assert.Equal(CategoryOutcome.NoChange, result.Outcome);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task Update_CannotTakeTheNameOfAnotherCategory()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var plumbing = NewCategory("Plumbing");
        var cleaning = NewCategory("Cleaning");
        db.ServiceCategories.AddRange(plumbing, cleaning);
        await db.SaveChangesAsync();

        var result = await svc.UpdateAsync(admin.UserId, cleaning.CategoryId, "plumbing", null);

        Assert.Equal(CategoryOutcome.Duplicate, result.Outcome);
        Assert.Equal("Cleaning", (await db.ServiceCategories.SingleAsync(c => c.CategoryId == cleaning.CategoryId)).CategoryName);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task Update_ChecksTheTextAndTheCategoryExists()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var cat = NewCategory("Plumbing");
        db.ServiceCategories.Add(cat);
        await db.SaveChangesAsync();

        Assert.Equal(CategoryOutcome.Invalid, (await svc.UpdateAsync(admin.UserId, cat.CategoryId, " ", null)).Outcome);
        Assert.Equal(CategoryOutcome.Invalid, (await svc.UpdateAsync(admin.UserId, cat.CategoryId, new string('a', 101), null)).Outcome);
        Assert.Equal(CategoryOutcome.NotFound, (await svc.UpdateAsync(admin.UserId, 424242, "Whatever", null)).Outcome);
        Assert.Equal("Plumbing", (await db.ServiceCategories.SingleAsync()).CategoryName);
    }

    [Fact]
    public async Task Update_IsRefusedForANonAdministrator()
    {
        var (svc, db) = Create();
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        var cat = NewCategory("Plumbing");
        db.Customers.Add(customer);
        db.ServiceCategories.Add(cat);
        await db.SaveChangesAsync();

        var result = await svc.UpdateAsync(customer.UserId, cat.CategoryId, "Hacked", null);

        Assert.Equal(CategoryOutcome.NotAllowed, result.Outcome);
        Assert.Equal("Plumbing", (await db.ServiceCategories.SingleAsync()).CategoryName);
    }

    // ------------------------------------------------------ disable / enable

    [Fact]
    public async Task Disable_ThenEnable_FlipsTheCategory_AndLogsBoth()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var cat = NewCategory("Plumbing");
        db.ServiceCategories.Add(cat);
        await db.SaveChangesAsync();

        var disabled = await svc.SetActiveAsync(admin.UserId, cat.CategoryId, enable: false);
        Assert.True(disabled.Succeeded);
        Assert.False((await db.ServiceCategories.SingleAsync()).IsActive);

        var enabled = await svc.SetActiveAsync(admin.UserId, cat.CategoryId, enable: true);
        Assert.True(enabled.Succeeded);
        Assert.True((await db.ServiceCategories.SingleAsync()).IsActive);

        var logs = await db.AdminActions.OrderBy(a => a.ActionId).ToListAsync();
        Assert.Equal(new[] { "Category disabled", "Category enabled" }, logs.Select(l => l.ActionType));
        Assert.All(logs, l =>
        {
            Assert.Equal("CATEGORY", l.TargetType);
            Assert.Equal(cat.CategoryId, l.TargetId);
            Assert.Equal(admin.UserId, l.AdminUserId);
        });
    }

    [Fact]
    public async Task Disable_LeavesServicesAndBookingsUntouched()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var cat = NewCategory("Plumbing");
        var provider = NewProvider();
        var service = new Service { Provider = provider, Category = cat, ServiceName = "Pipe Repair", Price = 850m, IsActive = true };
        var booking = NewBooking(new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) }, provider, service);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        await svc.SetActiveAsync(admin.UserId, cat.CategoryId, enable: false);

        Assert.True((await db.Services.SingleAsync()).IsActive);
        Assert.Equal(BookingStatuses.Completed, (await db.Bookings.SingleAsync()).Status);
    }

    [Fact]
    public async Task Disable_WhenAlreadyDisabled_SaysSo_AndWritesNoLog()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var off = NewCategory("Electrical", active: false);
        var on = NewCategory("Plumbing");
        db.ServiceCategories.AddRange(off, on);
        await db.SaveChangesAsync();

        Assert.Equal(CategoryOutcome.NoChange, (await svc.SetActiveAsync(admin.UserId, off.CategoryId, enable: false)).Outcome);
        Assert.Equal(CategoryOutcome.NoChange, (await svc.SetActiveAsync(admin.UserId, on.CategoryId, enable: true)).Outcome);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task SetActive_UnknownCategory_IsNotFound()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        Assert.Equal(CategoryOutcome.NotFound, (await svc.SetActiveAsync(admin.UserId, 424242, enable: false)).Outcome);
    }

    [Fact]
    public async Task SetActive_IsRefusedForEveryoneButAnActiveAdministrator()
    {
        var (svc, db) = Create();
        var provider = NewProvider();
        var cat = NewCategory("Plumbing");
        db.Providers.Add(provider);
        db.ServiceCategories.Add(cat);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        foreach (var userId in new[] { provider.UserId, suspendedAdmin.UserId, 99999 })
            Assert.Equal(CategoryOutcome.NotAllowed, (await svc.SetActiveAsync(userId, cat.CategoryId, enable: false)).Outcome);

        Assert.True((await db.ServiceCategories.SingleAsync()).IsActive);
        Assert.Empty(db.AdminActions);
    }
}
