using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ProfileRulesTests
{
    [Theory]
    [InlineData("  Marco   Reyes ", "Marco Reyes")]
    [InlineData("Marco\tReyes", "Marco Reyes")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeName_CollapsesSpaces(string? typed, string expected)
        => Assert.Equal(expected, ProfileRules.NormalizeName(typed));

    [Theory]
    [InlineData("Marco Reyes", "Marco", "Reyes")]
    [InlineData("Juan Dela Cruz", "Juan", "Dela Cruz")]       // same rule as the customer registration page
    [InlineData("Madonna", "Madonna", "")]
    public void SplitName_FirstWordIsTheFirstName(string name, string first, string last)
        => Assert.Equal((first, last), ProfileRules.SplitName(name));

    [Theory]
    [InlineData("+63 917 555 1234", "+639175551234")]
    [InlineData("(032) 123-4567", "0321234567")]
    [InlineData("0917.555.1234", "09175551234")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizePhone_RemovesSeparators(string? typed, string? expected)
        => Assert.Equal(expected, ProfileRules.NormalizePhone(typed));

    [Theory]
    [InlineData("+639175551234", true)]
    [InlineData("09171234567", true)]
    [InlineData("1234567", true)]
    [InlineData("+12345678901234", true)]            // + and 14 digits = 15 characters, the most the column holds
    [InlineData("+123456789012345", false)]          // 16 characters
    [InlineData("1234567890123456", false)]          // 16 digits
    [InlineData("12345", false)]
    [InlineData("abc1234567", false)]
    [InlineData("63+9175551234", false)]
    public void IsValidPhone_ChecksTheShape(string phone, bool valid)
        => Assert.Equal(valid, ProfileRules.IsValidPhone(phone));

    [Fact]
    public void NormalizeText_TurnsBrowserLineBreaksIntoOneCharacter()
    {
        Assert.Equal("a\nb", ProfileRules.NormalizeText(" a\r\nb "));
        Assert.Null(ProfileRules.NormalizeText("   "));
        Assert.Null(ProfileRules.NormalizeText(null));
    }
}

public class ProviderProfilePageViewModelTests
{
    [Fact]
    public void HeaderLine_ShowsBusinessAndCity_OrJustTheBusiness()
    {
        Assert.Equal("Marco's Plumbing Works · Quezon City", new ProviderProfilePageViewModel { BusinessName = "Marco's Plumbing Works", City = "Quezon City" }.HeaderLine);
        Assert.Equal("Marco's Plumbing Works", new ProviderProfilePageViewModel { BusinessName = "Marco's Plumbing Works", City = "  " }.HeaderLine);
        Assert.Equal("Marco's Plumbing Works", new ProviderProfilePageViewModel { BusinessName = "Marco's Plumbing Works" }.HeaderLine);
    }

    [Fact]
    public void ErrorFor_ReturnsTheMessage_OrNull()
    {
        var vm = new ProviderProfilePageViewModel { FieldErrors = new Dictionary<string, string> { ["Email"] = "Bad email." } };

        Assert.Equal("Bad email.", vm.ErrorFor("Email"));
        Assert.Null(vm.ErrorFor("City"));
    }
}

public class ProviderProfileServiceTests
{
    private static (ProviderProfileService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new ProviderProfileService(db), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active,
        string? email = null, string? phone = null, string? middle = null)
        => new()
        {
            FirstName = first, MiddleName = middle, LastName = last, Email = email ?? $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            PhoneNumber = phone, Role = role, AccountStatus = status
        };

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string approval = ApprovalStatuses.Approved,
        string account = AccountStatuses.Active, string first = "Marco", string last = "Reyes", string? middle = null,
        string business = "Marco's Plumbing Works", string? email = null, string? phone = "+639175551234",
        string? city = "Quezon City", string? address = "12 Mango St", string? description = "Licensed master plumber.",
        string? photo = null)
    {
        var p = new Provider
        {
            BusinessName = business, City = city, Address = address, Description = description, ProfilePhoto = photo,
            ApprovalStatus = approval,
            User = NewUser(first, last, Roles.ServiceProvider, account, email, phone, middle)
        };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<Service> AddServiceAsync(ServisyoDbContext db, Provider p, string name, bool active = true)
    {
        var category = await db.ServiceCategories.FirstOrDefaultAsync(c => c.CategoryName == "Plumbing")
                       ?? new ServiceCategory { CategoryName = "Plumbing" };
        var s = new Service { ProviderId = p.ProviderId, Category = category, ServiceName = name, Price = 500m, DurationMinutes = 60, IsActive = active };
        db.Services.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    private static async Task<Review> AddReviewAsync(ServisyoDbContext db, Provider p, byte rating, string? comment,
        DateTime createdUtc, string status = ReviewStatuses.Visible, string first = "Elena", string last = "Cruz")
    {
        var customer = new Customer { User = NewUser(first, last, Roles.Customer) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var booking = new Booking
        {
            CustomerId = customer.CustomerId, ProviderId = p.ProviderId, BookingDate = new DateOnly(2026, 9, 10),
            BookingTime = new TimeOnly(9, 0), TotalDurationMinutes = 60, TotalFee = 500m,
            Status = BookingStatuses.Completed, RowVersion = new byte[] { 1 }
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var review = new Review { BookingId = booking.BookingId, Rating = rating, Comment = comment, CreatedAt = createdUtc, Status = status };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return review;
    }

    private static ProviderProfileForm FormFrom(Provider p) => new()
    {
        FullName = $"{p.User.FirstName} {p.User.LastName}",
        BusinessName = p.BusinessName,
        PhoneNumber = p.User.PhoneNumber,
        Email = p.User.Email,
        City = p.City,
        Address = p.Address,
        Description = p.Description
    };

    private static Task<Provider> ReloadAsync(ServisyoDbContext db, int providerId)
        => db.Providers.AsNoTracking().Include(p => p.User).FirstAsync(p => p.ProviderId == providerId);

    // ------------------------------------------------------------------ access

    [Fact]
    public async Task OnlyAnActiveApprovedProvider_CanOpenTheProfile_OrSaveIt()
    {
        var (svc, db) = Create();
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var approved = await AddProviderAsync(db);
        var pending = await AddProviderAsync(db, ApprovalStatuses.Pending);
        var rejected = await AddProviderAsync(db, ApprovalStatuses.Rejected);
        var suspended = await AddProviderAsync(db, account: AccountStatuses.Suspended);
        var deactivated = await AddProviderAsync(db, account: AccountStatuses.Deactivated);

        var expected = new (int UserId, ProviderDashboardAccess Access)[]
        {
            (approved.UserId, ProviderDashboardAccess.Allowed),
            (pending.UserId, ProviderDashboardAccess.Pending),
            (rejected.UserId, ProviderDashboardAccess.Rejected),
            (suspended.UserId, ProviderDashboardAccess.NotAllowed),
            (deactivated.UserId, ProviderDashboardAccess.NotAllowed),
            (customer.UserId, ProviderDashboardAccess.NotAllowed),
            (999_999, ProviderDashboardAccess.NotAllowed)
        };

        foreach (var (userId, access) in expected)
        {
            var page = await svc.GetAsync(userId);
            Assert.Equal(access, page.Access);
            Assert.Equal(access == ProviderDashboardAccess.Allowed, page.Page is not null);

            var save = await svc.SaveAsync(userId, new ProviderProfileForm
            {
                FullName = "Hacked Name", BusinessName = "Hacked Business", Email = "hacked@test.com"
            });
            Assert.Equal(access, save.Access);
            Assert.Equal(access == ProviderDashboardAccess.Allowed, save.Saved);
        }

        // only the approved provider's own data changed
        Assert.Equal("Hacked Business", (await ReloadAsync(db, approved.ProviderId)).BusinessName);
        foreach (var other in new[] { pending, rejected, suspended, deactivated })
            Assert.Equal("Marco's Plumbing Works", (await ReloadAsync(db, other.ProviderId)).BusinessName);
        Assert.Equal("Ana", (await db.Users.AsNoTracking().FirstAsync(u => u.UserId == customer.UserId)).FirstName);
    }

    // -------------------------------------------------------------------- page

    [Fact]
    public async Task ThePage_ShowsTheSavedValues_InTheFormAndTheHeader()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, email: "marco@marcoplumbing.ph", phone: "+639175551234");

        var page = (await svc.GetAsync(p.UserId)).Page!;

        Assert.Equal("Marco Reyes", page.FullName);
        Assert.Equal("MR", page.Initials);
        Assert.Equal("Marco's Plumbing Works · Quezon City", page.HeaderLine);
        Assert.Equal("Marco Reyes", page.Form.FullName);
        Assert.Equal("Marco's Plumbing Works", page.Form.BusinessName);
        Assert.Equal("+639175551234", page.Form.PhoneNumber);
        Assert.Equal("marco@marcoplumbing.ph", page.Form.Email);
        Assert.Equal("Quezon City", page.Form.City);
        Assert.Equal("12 Mango St", page.Form.Address);
        Assert.Equal("Licensed master plumber.", page.Form.Description);
        Assert.Empty(page.FieldErrors);
        Assert.Null(page.FormError);
    }

    [Fact]
    public async Task Strength_IsTheSameSixChecksAsTheDashboard()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);                                  // description, address + city, phone: 3 of 6
        db.ProviderAvailabilities.Add(new ProviderAvailability { ProviderId = p.ProviderId, DayOfWeek = 1, IsAvailable = true, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0) });
        await AddServiceAsync(db, p, "Pipe Repair");
        await db.SaveChangesAsync();

        var missingPhoto = (await svc.GetAsync(p.UserId)).Page!;

        Assert.Equal(83, missingPhoto.StrengthPercent);                      // 5 of 6
        Assert.Equal("Add a profile photo to reach 100%", missingPhoto.StrengthHint);

        var tracked = await db.Providers.FirstAsync(x => x.ProviderId == p.ProviderId);
        tracked.ProfilePhoto = "/photos/marco.jpg";
        await db.SaveChangesAsync();

        var complete = (await svc.GetAsync(p.UserId)).Page!;

        Assert.Equal(100, complete.StrengthPercent);
        Assert.Equal("Your profile is complete.", complete.StrengthHint);
    }

    [Fact]
    public async Task ABareProfile_StartsAtZero_AndSaysWhatToDoFirst()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, phone: null, city: null, address: null, description: null);

        var page = (await svc.GetAsync(p.UserId)).Page!;

        Assert.Equal(0, page.StrengthPercent);
        Assert.Equal("Add your first service to reach 100%", page.StrengthHint);
    }

    [Fact]
    public async Task ServicesOffered_AreTheProvidersOwnActiveServices_InTheOrderTheyWereAdded()
    {
        var (svc, db) = Create();
        var mine = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, business: "Other");
        await AddServiceAsync(db, mine, "Pipe Repair");
        await AddServiceAsync(db, mine, "Drain Unclogging");
        await AddServiceAsync(db, mine, "Water Heater Install", active: false);
        await AddServiceAsync(db, other, "Not Mine");

        var page = (await svc.GetAsync(mine.UserId)).Page!;

        Assert.Equal(new[] { "Pipe Repair", "Drain Unclogging" }, page.ServiceNames);
    }

    [Fact]
    public async Task Reviews_AreTheLatestVisibleOnesOfThisProvider_NewestFirst_AtMostThree()
    {
        var (svc, db) = Create();
        var mine = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, business: "Other");

        await AddReviewAsync(db, mine, 5, "Oldest", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddReviewAsync(db, mine, 3, "Second newest", new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc));
        await AddReviewAsync(db, mine, 4, "Newest", new DateTime(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc), first: "Elena", last: "Cruz");
        await AddReviewAsync(db, mine, 2, "Third newest", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddReviewAsync(db, mine, 1, "Removed by an admin", new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), ReviewStatuses.Removed);
        await AddReviewAsync(db, mine, 1, "Flagged", new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc), ReviewStatuses.Flagged);
        await AddReviewAsync(db, other, 5, "Someone else's review", new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc));

        var reviews = (await svc.GetAsync(mine.UserId)).Page!.Reviews;

        Assert.Equal(new[] { "Newest", "Second newest", "Third newest" }, reviews.Select(r => r.Comment));
        Assert.Equal("Elena Cruz", reviews[0].Author);
        Assert.Equal((byte)4, reviews[0].Rating);
        Assert.Equal(new DateOnly(2026, 9, 13), reviews[0].Date);          // 6 pm UTC on the 12th is already the 13th in Cebu
    }

    [Fact]
    public async Task AReviewWithoutAComment_StillShows()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        await AddReviewAsync(db, p, 5, null, new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc));

        var review = Assert.Single((await svc.GetAsync(p.UserId)).Page!.Reviews);

        Assert.Equal(string.Empty, review.Comment);
    }

    [Fact]
    public async Task ARejectedForm_IsShownAgainWithTheTypedText_AndTheSavedHeader()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var typed = FormFrom(p);
        typed.BusinessName = "Typed But Not Saved";
        typed.Email = "not-an-email";
        var rejected = await svc.SaveAsync(p.UserId, typed);

        var page = (await svc.GetAsync(p.UserId, typed, rejected)).Page!;

        Assert.False(rejected.Saved);
        Assert.Equal("Typed But Not Saved", page.Form.BusinessName);          // what was typed stays in the boxes
        Assert.Equal("Marco's Plumbing Works · Quezon City", page.HeaderLine); // the header still shows what is saved
        Assert.NotNull(page.ErrorFor("Email"));
    }

    // -------------------------------------------------------------------- save

    [Fact]
    public async Task Save_UpdatesTheAccountAndTheProfile()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, middle: "Santos");

        var result = await svc.SaveAsync(p.UserId, new ProviderProfileForm
        {
            FullName = "  Marco   Antonio Reyes ",
            BusinessName = "  Reyes Plumbing & Co.  ",
            PhoneNumber = "+63 917 555 9999",
            Email = "  Marco.New@MarcoPlumbing.PH ",
            City = " Cebu City ",
            Address = " 5 Colon St ",
            Description = "  Fast and reliable.  "
        });

        Assert.True(result.Saved);
        Assert.Empty(result.FieldErrors);
        Assert.Equal("Marco Antonio Reyes", result.FullName);
        Assert.Equal("marco.new@marcoplumbing.ph", result.Email);

        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("Marco", saved.User.FirstName);
        Assert.Equal("Antonio Reyes", saved.User.LastName);           // first word = first name, the rest = last name
        Assert.Equal("Santos", saved.User.MiddleName);                // the middle name is not touched
        Assert.Equal("marco.new@marcoplumbing.ph", saved.User.Email); // emails are stored in lower case, like registration
        Assert.Equal("+639175559999", saved.User.PhoneNumber);        // the column holds 15 characters, so no spaces
        Assert.Equal("Reyes Plumbing & Co.", saved.BusinessName);
        Assert.Equal("Cebu City", saved.City);
        Assert.Equal("5 Colon St", saved.Address);
        Assert.Equal("Fast and reliable.", saved.Description);
    }

    [Fact]
    public async Task Save_WithTheNameUnchanged_NeverRewritesFirstMiddleOrLastName()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, first: "Maria", middle: "Luisa", last: "Dela Cruz");
        var form = FormFrom(p);
        form.BusinessName = "New Business Name";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("Maria", saved.User.FirstName);
        Assert.Equal("Luisa", saved.User.MiddleName);
        Assert.Equal("Dela Cruz", saved.User.LastName);
        Assert.Equal("New Business Name", saved.BusinessName);
    }

    [Fact]
    public async Task Save_WithTheNameUnchanged_ToleratesExtraSpaces()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, first: "Maria", last: "Dela Cruz");
        var form = FormFrom(p);
        form.FullName = "  Maria    Dela   Cruz ";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        Assert.Equal("Dela Cruz", (await ReloadAsync(db, p.ProviderId)).User.LastName);
    }

    [Fact]
    public async Task Save_AllowsAnOldOneWordName_AsLongAsItIsNotEdited()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, first: "Marco", last: "");
        var form = FormFrom(p);
        form.FullName = "Marco";
        form.City = "Mandaue City";

        Assert.True((await svc.SaveAsync(p.UserId, form)).Saved);

        form.FullName = "Marco R";                    // once the name is edited, the first-and-last-name rule applies; "Marco R" has both
        Assert.True((await svc.SaveAsync(p.UserId, form)).Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("Marco", saved.User.FirstName);
        Assert.Equal("R", saved.User.LastName);
    }

    [Fact]
    public async Task Save_RefusesANewNameWithoutAFirstAndLastName()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.FullName = "Marco";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Equal("Enter your first and last name.", result.FieldErrors["FullName"]);
        Assert.Equal("Reyes", (await ReloadAsync(db, p.ProviderId)).User.LastName);
    }

    [Fact]
    public async Task Save_RequiresNameBusinessAndEmail()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);

        var result = await svc.SaveAsync(p.UserId, new ProviderProfileForm { FullName = "  ", BusinessName = "", Email = null });

        Assert.False(result.Saved);
        Assert.Equal(new[] { "BusinessName", "Email", "FullName" }, result.FieldErrors.Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task Save_IsAllOrNothing_OneBadBoxMeansNothingIsSaved()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.BusinessName = "A Perfectly Good New Name";
        form.City = "Cebu City";
        form.Email = "not-an-email";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("Marco's Plumbing Works", saved.BusinessName);
        Assert.Equal("Quezon City", saved.City);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two@@test.com")]
    [InlineData("spaces in@test.com")]
    public async Task Save_RefusesAnInvalidEmail(string email)
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.Email = email;

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Equal("Enter a valid email address.", result.FieldErrors["Email"]);
    }

    [Fact]
    public async Task Save_RefusesAnEmailAnotherAccountUses_EvenInADifferentCase()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, email: "marco@test.com");
        db.Users.Add(NewUser("Ana", "Reyes", Roles.Customer, email: "taken@test.com"));
        await db.SaveChangesAsync();
        var form = FormFrom(p);
        form.Email = "TAKEN@Test.com";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Equal("That email address is already used by another account.", result.FieldErrors["Email"]);
        Assert.Equal("marco@test.com", (await ReloadAsync(db, p.ProviderId)).User.Email);
    }

    [Fact]
    public async Task Save_KeepsTheOwnEmail_EvenWhenItsCaseIsDifferent()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, email: "marco@test.com");
        var form = FormFrom(p);
        form.Email = "Marco@Test.com";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        Assert.Equal("marco@test.com", (await ReloadAsync(db, p.ProviderId)).User.Email);
    }

    [Fact]
    public async Task Save_TurnsABlankPhoneCityAddressAndBio_IntoNothing()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.PhoneNumber = "   ";
        form.City = "";
        form.Address = null;
        form.Description = "  \r\n ";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Null(saved.User.PhoneNumber);
        Assert.Null(saved.City);
        Assert.Null(saved.Address);
        Assert.Null(saved.Description);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("call me")]
    [InlineData("+63 917 555 1234 5678 9")]
    public async Task Save_RefusesAnInvalidPhone(string phone)
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.PhoneNumber = phone;

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Equal("Enter a valid phone number, for example +63 917 555 1234.", result.FieldErrors["PhoneNumber"]);
    }

    [Fact]
    public async Task Save_LeavesAnOldPhoneInAnotherStyleAlone_UntilItIsEdited()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, phone: "n/a");
        var form = FormFrom(p);
        form.City = "Cebu City";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        Assert.Equal("n/a", (await ReloadAsync(db, p.ProviderId)).User.PhoneNumber);
    }

    [Fact]
    public async Task Save_EnforcesTheDatabaseLengths()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.FullName = "A " + new string('b', ProfileRules.MaxFullNameLength);
        form.BusinessName = new string('b', ProfileRules.MaxBusinessNameLength + 1);
        form.City = new string('c', ProfileRules.MaxCityLength + 1);
        form.Address = new string('a', ProfileRules.MaxAddressLength + 1);
        form.Description = new string('d', ProfileRules.MaxDescriptionLength + 1);
        form.Email = new string('e', ProfileRules.MaxEmailLength) + "@test.com";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Equal(new[] { "Address", "BusinessName", "City", "Description", "Email", "FullName" }, result.FieldErrors.Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task Save_AcceptsTextExactlyAtTheLimit_AndCountsABrowserLineBreakAsOneCharacter()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.City = new string('c', ProfileRules.MaxCityLength);
        form.Description = new string('a', 127) + "\r\n" + new string('b', 127);        // 255 characters once the line break counts as one

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal(255, saved.Description!.Length);
        Assert.Contains("\n", saved.Description);
        Assert.DoesNotContain("\r", saved.Description);
    }

    [Fact]
    public async Task Save_ChangesOnlyTheSignedInProvidersData()
    {
        var (svc, db) = Create();
        var mine = await AddProviderAsync(db, business: "Mine", email: "mine@test.com");
        var other = await AddProviderAsync(db, business: "Other", first: "Liza", last: "Tan", email: "other@test.com");
        var form = FormFrom(mine);
        form.BusinessName = "Mine Renamed";
        form.FullName = "Marco Renamed";

        Assert.True((await svc.SaveAsync(mine.UserId, form)).Saved);

        var untouched = await ReloadAsync(db, other.ProviderId);
        Assert.Equal("Other", untouched.BusinessName);
        Assert.Equal("Liza", untouched.User.FirstName);
        Assert.Equal("other@test.com", untouched.User.Email);
        Assert.Equal("Mine Renamed", (await ReloadAsync(db, mine.ProviderId)).BusinessName);
    }

    [Fact]
    public async Task Save_NeverChangesApprovalRatingOrAccountStatus()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var tracked = await db.Providers.FirstAsync(x => x.ProviderId == p.ProviderId);
        tracked.AvgRating = 4.5m;
        tracked.ReviewCount = 7;
        tracked.MinNoticeDays = 3;
        tracked.MaxJobsPerDay = 2;
        await db.SaveChangesAsync();
        var form = FormFrom(p);
        form.BusinessName = "Changed";

        await svc.SaveAsync(p.UserId, form);

        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal(ApprovalStatuses.Approved, saved.ApprovalStatus);
        Assert.Equal(AccountStatuses.Active, saved.User.AccountStatus);
        Assert.Equal(Roles.ServiceProvider, saved.User.Role);
        Assert.Equal(4.5m, saved.AvgRating);
        Assert.Equal(7, saved.ReviewCount);
        Assert.Equal(3, saved.MinNoticeDays);
        Assert.Equal(2, saved.MaxJobsPerDay);
    }
}
