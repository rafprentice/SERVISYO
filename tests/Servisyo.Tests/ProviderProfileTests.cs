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

    /// NormalizePhone puts a number into the saved form: separators removed and +63 / 63 turned into 0
    /// (whether the typed number is allowed at all is InputRules.Phone's job).
    [Theory]
    [InlineData("+639175551234", "09175551234")]
    [InlineData("+63 917 555 1234", "09175551234")]   // old saved numbers may still have spaces
    [InlineData("639175551234", "09175551234")]
    [InlineData("0917.555.1234", "09175551234")]
    [InlineData("09171234567", "09171234567")]
    [InlineData("(032) 123-4567", "0321234567")]       // not a mobile number: cleaned, then refused by IsValidPhone
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizePhone_SavesThe09Form(string? typed, string? expected)
        => Assert.Equal(expected, ProfileRules.NormalizePhone(typed));

    /// A SAVED phone number (USERS.PHONE_NUMBER, NVARCHAR(11)) is always an 11-digit mobile number: 09XXXXXXXXX.
    [Theory]
    [InlineData("09171234567", true)]
    [InlineData("09991234567", true)]
    [InlineData("+639175551234", false)]               // the + form is typed, never saved
    [InlineData("08171234567", false)]                 // not a mobile number
    [InlineData("0917123456", false)]                  // 10 digits
    [InlineData("091712345678", false)]                // 12 digits
    [InlineData("1234567", false)]
    [InlineData("0321234567", false)]
    [InlineData("abc1234567", false)]
    public void IsValidPhone_OnlyAcceptsTheSaved09Form(string phone, bool valid)
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
    public void HeaderLine_ShowsTheBusinessAndCebuCity()
        => Assert.Equal("Marco's Plumbing Works · Cebu City", new ProviderProfilePageViewModel { BusinessName = "Marco's Plumbing Works" }.HeaderLine);

    [Fact]
    public void ErrorFor_ReturnsTheMessage_OrNull()
    {
        var vm = new ProviderProfilePageViewModel { FieldErrors = new Dictionary<string, string> { ["Email"] = "Bad email." } };

        Assert.Equal("Bad email.", vm.ErrorFor("Email"));
        Assert.Null(vm.ErrorFor("Address"));
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
        string? address = "12 Mango St", string? description = "Licensed master plumber.",
        string? photo = null, decimal? baseFee = 500m)
    {
        var p = new Provider
        {
            BusinessName = business, Address = address, Description = description, ProfilePhoto = photo, BaseFee = baseFee,
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

    /// What the page would send if nothing were disabled: every value as it is saved.
    private static ProviderProfileForm FormFrom(Provider p) => new()
    {
        FirstName = p.User.FirstName,
        MiddleName = p.User.MiddleName,
        LastName = p.User.LastName,
        BusinessName = p.BusinessName,
        PhoneNumber = p.User.PhoneNumber,
        Address = p.Address,
        BaseFee = p.BaseFee,
        Email = p.User.Email,
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

            var save = await svc.SaveAsync(userId, new ProviderProfileForm { Email = "hacked@test.com", Description = "Hacked bio" });
            Assert.Equal(access, save.Access);
            Assert.Equal(access == ProviderDashboardAccess.Allowed, save.Saved);
        }

        // only the approved provider's own data changed
        Assert.Equal("Hacked bio", (await ReloadAsync(db, approved.ProviderId)).Description);
        foreach (var other in new[] { pending, rejected, suspended, deactivated })
            Assert.Equal("Licensed master plumber.", (await ReloadAsync(db, other.ProviderId)).Description);
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
        Assert.Equal("Marco's Plumbing Works · Cebu City", page.HeaderLine);
        Assert.Equal("Marco", page.Form.FirstName);
        Assert.Equal("Reyes", page.Form.LastName);
        Assert.Equal("Marco's Plumbing Works", page.Form.BusinessName);
        Assert.Equal(500m, page.Form.BaseFee);
        Assert.True(page.InfoLocked);                                       // approved: the core information is read-only
        Assert.Equal("+639175551234", page.Form.PhoneNumber);
        Assert.Equal("marco@marcoplumbing.ph", page.Form.Email);
        Assert.Equal("12 Mango St", page.Form.Address);
        Assert.Equal("Licensed master plumber.", page.Form.Description);
        Assert.Empty(page.FieldErrors);
        Assert.Null(page.FormError);
    }

    [Fact]
    public async Task Strength_IsTheSameSixChecksAsTheDashboard()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);                                  // description, address, phone: 3 of 6
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
        var p = await AddProviderAsync(db, phone: null, address: null, description: null);

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
    public async Task ARejectedForm_ShowsTheTypedEmailAndBio_ButTheLockedValuesFromTheDatabase()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var typed = new ProviderProfileForm { Email = "not-an-email", Description = "Typed but not saved" };   // locked boxes are not sent
        var rejected = await svc.SaveAsync(p.UserId, typed);

        var page = (await svc.GetAsync(p.UserId, typed, rejected)).Page!;

        Assert.False(rejected.Saved);
        Assert.Equal("Typed but not saved", page.Form.Description);           // what was typed stays in the boxes
        Assert.Equal("not-an-email", page.Form.Email);
        Assert.Equal("Marco's Plumbing Works", page.Form.BusinessName);       // locked: always the saved value
        Assert.Equal("12 Mango St", page.Form.Address);
        Assert.Equal("Marco's Plumbing Works · Cebu City", page.HeaderLine);
        Assert.NotNull(page.ErrorFor("Email"));
    }

    // -------------------------------------------------------------- approved = core information locked

    [Fact]
    public void TheLockedFields_AreTheOnesTheSpecLists()
    {
        Assert.Equal(new[] { "FirstName", "MiddleName", "LastName", "BusinessName", "PhoneNumber", "Address", "BaseFee" }, ProviderLockRules.LockedFields);
        Assert.Equal("Your provider account is approved and verified. Profile updates require Admin assistance.", ProviderLockRules.ApprovedNotice);
        Assert.True(ProviderLockRules.IsLocked(ApprovalStatuses.Approved));
        Assert.False(ProviderLockRules.IsLocked(ApprovalStatuses.Pending));
        Assert.False(ProviderLockRules.IsLocked(ApprovalStatuses.Rejected));
    }

    [Fact]
    public async Task ThePage_OfAnApprovedProvider_IsLocked_AndShowsTheMiddleName()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, middle: "Castro");

        var page = (await svc.GetAsync(p.UserId)).Page!;

        Assert.True(page.InfoLocked);
        Assert.Equal("Castro", page.Form.MiddleName);
    }

    /// Changes one locked field of the form (a value different from the saved one).
    private static void ChangeLocked(ProviderProfileForm form, string field)
    {
        switch (field)
        {
            case "FirstName": form.FirstName = "Mark"; break;
            case "MiddleName": form.MiddleName = "Castro"; break;
            case "LastName": form.LastName = "Santos"; break;
            case "BusinessName": form.BusinessName = "A New Business Name"; break;
            case "PhoneNumber": form.PhoneNumber = "09998887777"; break;
            case "Address": form.Address = "7 Gorordo Ave"; break;
            case "BaseFee": form.BaseFee = 750m; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    [Theory]
    [InlineData("FirstName")]
    [InlineData("MiddleName")]
    [InlineData("LastName")]
    [InlineData("BusinessName")]
    [InlineData("PhoneNumber")]
    [InlineData("Address")]
    [InlineData("BaseFee")]
    public async Task Save_RefusesAnyChangeToALockedField_AndSavesNothingAtAll(string field)
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, email: "marco@test.com");
        var form = FormFrom(p);
        form.Description = "A new bio";                 // a good change in the same request is NOT saved either
        ChangeLocked(form, field);

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.True(result.LockViolation);              // the controller answers 403 Forbidden
        Assert.Equal(ProviderLockRules.ApprovedNotice, result.Message);
        Assert.Equal(ProviderLockRules.FieldLockedMessage, result.FieldErrors[field]);
        Assert.Single(result.FieldErrors);

        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("Marco", saved.User.FirstName);
        Assert.Null(saved.User.MiddleName);
        Assert.Equal("Reyes", saved.User.LastName);
        Assert.Equal("Marco's Plumbing Works", saved.BusinessName);
        Assert.Equal("+639175551234", saved.User.PhoneNumber);
        Assert.Equal("12 Mango St", saved.Address);
        Assert.Equal(500m, saved.BaseFee);
        Assert.Equal("Licensed master plumber.", saved.Description);
    }

    [Fact]
    public async Task Save_UpdatesEmailAndBio_TheFieldsAnApprovedProviderCanStillEdit()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, middle: "Santos");
        var form = FormFrom(p);
        form.Email = "  Marco.New@MarcoPlumbing.PH ";
        form.Description = "  Fast and reliable.  ";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        Assert.False(result.LockViolation);
        Assert.Equal("Marco Reyes", result.FullName);
        Assert.Equal("marco.new@marcoplumbing.ph", result.Email);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("marco.new@marcoplumbing.ph", saved.User.Email);   // emails are stored in lower case, like registration
        Assert.Equal("Fast and reliable.", saved.Description);
        Assert.Equal("Santos", saved.User.MiddleName);                 // the locked values are untouched
        Assert.Equal("Marco's Plumbing Works", saved.BusinessName);
        Assert.Equal(500m, saved.BaseFee);
    }

    [Fact]
    public async Task Save_WithoutTheLockedFields_AsTheBrowserSendsIt_KeepsThem()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);

        // disabled boxes are never sent: only Email and Bio arrive
        var result = await svc.SaveAsync(p.UserId, new ProviderProfileForm { Email = p.User.Email, Description = "Only the bio" });

        Assert.True(result.Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("Only the bio", saved.Description);
        Assert.Equal("Marco", saved.User.FirstName);
        Assert.Equal("+639175551234", saved.User.PhoneNumber);
        Assert.Equal("12 Mango St", saved.Address);
        Assert.Equal(500m, saved.BaseFee);
    }

    [Fact]
    public async Task Save_SendingTheSavedValuesBack_EvenWithExtraSpaces_IsNotAChange()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, first: "Maria", last: "Dela Cruz", phone: "+639175551234");
        var form = FormFrom(p);
        form.LastName = "  Dela   Cruz ";
        form.PhoneNumber = "09175551234";             // the same number in the saved 09 form
        form.BaseFee = 500.00m;
        form.Description = "New bio";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("Dela Cruz", saved.User.LastName);
        Assert.Equal("+639175551234", saved.User.PhoneNumber);         // left exactly as it was
    }

    [Fact]
    public async Task Save_ABlankLockedValue_CountsAsNotSent_AndNeverClearsIt()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.PhoneNumber = "   ";
        form.Address = "";
        form.BusinessName = null;

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("+639175551234", saved.User.PhoneNumber);
        Assert.Equal("12 Mango St", saved.Address);
        Assert.Equal("Marco's Plumbing Works", saved.BusinessName);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("0")]
    public async Task AnOlderProvider_WithNoOrAZeroBaseFee_CanStillSaveEmailAndBio(string old)
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db, baseFee: old == "none" ? null : 0m);
        var form = FormFrom(p);
        form.Description = "Updated bio";

        Assert.True((await svc.SaveAsync(p.UserId, form)).Saved);
        var saved = await ReloadAsync(db, p.ProviderId);
        Assert.Equal("Updated bio", saved.Description);
        Assert.Equal(old == "none" ? null : 0m, saved.BaseFee);          // only an administrator can fix it now
    }

    [Fact]
    public async Task Save_WithActiveBookings_StillUpdatesEmailAndBio()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var customer = new Customer { User = NewUser("Elena", "Cruz", Roles.Customer) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.Bookings.Add(new Booking
        {
            CustomerId = customer.CustomerId, ProviderId = p.ProviderId, BookingDate = new DateOnly(2026, 11, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 500m, Status = BookingStatuses.Accepted, RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();
        var form = FormFrom(p);
        form.Description = "Busy but reachable";

        Assert.True((await svc.SaveAsync(p.UserId, form)).Saved);
        Assert.Equal("Busy but reachable", (await ReloadAsync(db, p.ProviderId)).Description);
    }

    // -------------------------------------------------------------------- save: email and bio

    [Fact]
    public async Task Save_RequiresAnEmail()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);

        var result = await svc.SaveAsync(p.UserId, new ProviderProfileForm { Email = null });

        Assert.False(result.Saved);
        Assert.False(result.LockViolation);
        Assert.Equal(new[] { "Email" }, result.FieldErrors.Keys);
    }

    [Fact]
    public async Task Save_IsAllOrNothing_OneBadBoxMeansNothingIsSaved()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.Description = "A perfectly good new bio";
        form.Email = "not-an-email";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Equal("Licensed master plumber.", (await ReloadAsync(db, p.ProviderId)).Description);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two@@test.com")]
    [InlineData("spaces in@test.com")]
    [InlineData("marco@gmail2.com")]
    [InlineData("marco@a.c")]
    public async Task Save_RefusesAnInvalidEmail(string email)
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.Email = email;

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Equal(InputRules.EmailMessage, result.FieldErrors["Email"]);
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
    public async Task Save_TurnsABlankBio_IntoNothing()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.Description = "  \r\n ";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        Assert.Null((await ReloadAsync(db, p.ProviderId)).Description);
    }

    [Fact]
    public async Task Save_EnforcesTheDatabaseLengths()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
        form.Description = new string('d', ProfileRules.MaxDescriptionLength + 1);
        form.Email = new string('e', ProfileRules.MaxEmailLength) + "@test.com";

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Equal(new[] { "Description", "Email" }, result.FieldErrors.Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task Save_AcceptsABioExactlyAtTheLimit_AndCountsABrowserLineBreakAsOneCharacter()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = FormFrom(p);
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
        form.Description = "Mine, updated";

        Assert.True((await svc.SaveAsync(mine.UserId, form)).Saved);

        var untouched = await ReloadAsync(db, other.ProviderId);
        Assert.Equal("Licensed master plumber.", untouched.Description);
        Assert.Equal("other@test.com", untouched.User.Email);
        Assert.Equal("Mine, updated", (await ReloadAsync(db, mine.ProviderId)).Description);
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
        form.Description = "Changed";

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
