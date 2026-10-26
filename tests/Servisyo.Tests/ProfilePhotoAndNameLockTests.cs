using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

internal sealed class TempWebEnv : IWebHostEnvironment, IDisposable
{
    public TempWebEnv()
    {
        WebRootPath = Path.Combine(Path.GetTempPath(), "servisyo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WebRootPath);
        WebRootFileProvider = new NullFileProvider();
        ContentRootPath = WebRootPath;
        ContentRootFileProvider = new NullFileProvider();
    }

    public string WebRootPath { get; set; }
    public IFileProvider WebRootFileProvider { get; set; }
    public string ApplicationName { get; set; } = "Servisyo.Tests";
    public IFileProvider ContentRootFileProvider { get; set; }
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Test";

    public string AvatarFolder => Path.Combine(WebRootPath, "uploads", "avatars");

    public void Dispose()
    {
        try { Directory.Delete(WebRootPath, recursive: true); } catch { /* temp folder: ignore */ }
    }
}

public class ProfilePhotoTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3 };
    private static readonly byte[] Jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 1, 2, 3 };

    private static IFormFile FileOf(byte[] bytes, string name = "me.png", string type = "image/png")
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "photo", name) { Headers = new HeaderDictionary(), ContentType = type };
    }

    private static ServisyoDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User NewUser(string role, string status = AccountStatuses.Active) =>
        new() { FirstName = "Ana", LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status };

    [Fact]
    public async Task Customer_UploadsAPng_ItIsSavedUnderARandomName_AndShownInTheSidebar()
    {
        using var env = new TempWebEnv();
        var db = NewDb();
        var ana = new Customer { User = NewUser(Roles.Customer) };
        db.Customers.Add(ana);
        await db.SaveChangesAsync();
        var svc = new ProfilePhotoService(db, env);

        var result = await svc.SaveAsync(ana.UserId, FileOf(Png, "../../evil.exe"));

        Assert.Equal(PhotoOutcome.Saved, result.Outcome);
        Assert.Equal(Roles.Customer, result.Role);
        var url = (await db.Customers.AsNoTracking().SingleAsync()).ProfilePhoto!;
        Assert.StartsWith("/uploads/avatars/", url);
        Assert.EndsWith(".png", url);                       // decided by the bytes, not by the name the browser sent
        Assert.DoesNotContain("evil", url);
        Assert.True(File.Exists(Path.Combine(env.AvatarFolder, url["/uploads/avatars/".Length..])));
        Assert.Equal(url, await svc.GetUrlAsync(ana.UserId));
    }

    [Fact]
    public async Task Provider_ReplacingAPhoto_DeletesTheOldFile()
    {
        using var env = new TempWebEnv();
        var db = NewDb();
        var p = new Provider { BusinessName = "Vincent Valencia", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser(Roles.ServiceProvider) };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        var svc = new ProfilePhotoService(db, env);

        await svc.SaveAsync(p.UserId, FileOf(Png));
        var first = (await db.Providers.AsNoTracking().SingleAsync()).ProfilePhoto!;
        var second = await svc.SaveAsync(p.UserId, FileOf(Jpg, "b.jpg", "image/jpeg"));

        Assert.Equal(Roles.ServiceProvider, second.Role);
        var now = (await db.Providers.AsNoTracking().SingleAsync()).ProfilePhoto!;
        Assert.EndsWith(".jpg", now);
        Assert.False(File.Exists(Path.Combine(env.AvatarFolder, first["/uploads/avatars/".Length..])));
        Assert.Single(Directory.GetFiles(env.AvatarFolder));
    }

    [Fact]
    public async Task ATextFileNamedPng_IsRefused()
    {
        using var env = new TempWebEnv();
        var db = NewDb();
        var ana = new Customer { User = NewUser(Roles.Customer) };
        db.Customers.Add(ana);
        await db.SaveChangesAsync();

        var result = await new ProfilePhotoService(db, env).SaveAsync(ana.UserId, FileOf(Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "x.png"));

        Assert.Equal(PhotoOutcome.NotAnImage, result.Outcome);
        Assert.Null((await db.Customers.AsNoTracking().SingleAsync()).ProfilePhoto);
    }

    [Fact]
    public async Task TooLarge_And_NoFile_AreRefused()
    {
        using var env = new TempWebEnv();
        var db = NewDb();
        var ana = new Customer { User = NewUser(Roles.Customer) };
        db.Customers.Add(ana);
        await db.SaveChangesAsync();
        var svc = new ProfilePhotoService(db, env);

        var big = new byte[ProfilePhotoService.MaxBytes + 1];
        Png.CopyTo(big, 0);
        Assert.Equal(PhotoOutcome.TooLarge, (await svc.SaveAsync(ana.UserId, FileOf(big))).Outcome);
        Assert.Equal(PhotoOutcome.NoFile, (await svc.SaveAsync(ana.UserId, null)).Outcome);
    }

    [Fact]
    public async Task AdminsAndSuspendedAccounts_CannotUpload()
    {
        using var env = new TempWebEnv();
        var db = NewDb();
        var admin = NewUser(Roles.Administrator);
        var suspended = new Customer { User = NewUser(Roles.Customer, AccountStatuses.Suspended) };
        db.Users.Add(admin);
        db.Customers.Add(suspended);
        await db.SaveChangesAsync();
        var svc = new ProfilePhotoService(db, env);

        Assert.Equal(PhotoOutcome.NotAllowed, (await svc.SaveAsync(admin.UserId, FileOf(Png))).Outcome);
        Assert.Equal(PhotoOutcome.NotAllowed, (await svc.SaveAsync(suspended.UserId, FileOf(Png))).Outcome);
    }

    [Fact]
    public async Task Remove_ClearsThePhoto_AndDeletesTheFile()
    {
        using var env = new TempWebEnv();
        var db = NewDb();
        var ana = new Customer { User = NewUser(Roles.Customer) };
        db.Customers.Add(ana);
        await db.SaveChangesAsync();
        var svc = new ProfilePhotoService(db, env);
        await svc.SaveAsync(ana.UserId, FileOf(Png));

        var result = await svc.RemoveAsync(ana.UserId);

        Assert.Equal(PhotoOutcome.Removed, result.Outcome);
        Assert.Null(await svc.GetUrlAsync(ana.UserId));
        Assert.Empty(Directory.GetFiles(env.AvatarFolder));
    }

    [Fact]
    public async Task AStoredValueOutsideTheAvatarFolder_IsNeverShownOrDeleted()
    {
        using var env = new TempWebEnv();
        var db = NewDb();
        var ana = new Customer { User = NewUser(Roles.Customer), ProfilePhoto = "/uploads/avatars/../../appsettings.json" };
        db.Customers.Add(ana);
        await db.SaveChangesAsync();

        Assert.Null(await new ProfilePhotoService(db, env).GetUrlAsync(ana.UserId));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, ".webp")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }, null)]
    [InlineData(new byte[] { }, null)]
    public void DetectImage_ReadsTheFileSignature(byte[] bytes, string? expected)
        => Assert.Equal(expected, ProfilePhotoService.DetectImage(bytes));
}

public class NameLockTests
{
    private static ServisyoDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User NewUser(string first, string last, string role) =>
        new() { FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = AccountStatuses.Active };

    private static async Task<(ServisyoDbContext Db, Customer Ana, Provider Vince)> WorldAsync()
    {
        var db = NewDb();
        var ana = new Customer { User = NewUser("Ana", "Cruz", Roles.Customer), City = "Cebu" };
        var vince = new Provider { BusinessName = "Vincent Valencia", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Vincent", "Valencia", Roles.ServiceProvider) };
        db.AddRange(ana, vince);
        await db.SaveChangesAsync();
        return (db, ana, vince);
    }

    private static async Task BookAsync(ServisyoDbContext db, Customer c, Provider p, string status)
    {
        db.Bookings.Add(new Booking
        {
            CustomerId = c.CustomerId, ProviderId = p.ProviderId, BookingDate = new DateOnly(2026, 10, 30), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 500m, Status = status, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();
    }

    private static CustomerProfileForm AnaForm(Customer ana, string name) =>
        new() { FullName = name, Email = ana.User.Email, PhoneNumber = null, City = "Cebu" };

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task Customer_CannotRename_WithAnActiveBooking(string status)
    {
        var (db, ana, vince) = await WorldAsync();
        await BookAsync(db, ana, vince, status);
        var svc = new CustomerProfileService(db, new PasswordHasher<User>());

        var page = (await svc.GetAsync(ana.UserId)).Page!;
        var result = await svc.SaveAsync(ana.UserId, AnaForm(ana, "Ana Santos"));

        Assert.True(page.NameLocked);
        Assert.False(result.Saved);
        Assert.Equal(ActiveBookingRules.NameLockedMessage, result.FieldErrors["FullName"]);
        Assert.Equal("Cruz", (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == ana.UserId)).LastName);
    }

    [Theory]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.Rejected)]
    public async Task Customer_CanRename_WhenAllBookingsAreFinished(string status)
    {
        var (db, ana, vince) = await WorldAsync();
        await BookAsync(db, ana, vince, status);
        var svc = new CustomerProfileService(db, new PasswordHasher<User>());

        Assert.False((await svc.GetAsync(ana.UserId)).Page!.NameLocked);
        Assert.True((await svc.SaveAsync(ana.UserId, AnaForm(ana, "Ana Santos"))).Saved);
    }

    [Fact]
    public async Task Customer_CanStillSaveOtherFields_WhileTheNameIsLocked()
    {
        var (db, ana, vince) = await WorldAsync();
        await BookAsync(db, ana, vince, BookingStatuses.Accepted);
        var svc = new CustomerProfileService(db, new PasswordHasher<User>());

        var form = AnaForm(ana, "Ana Cruz");        // same name as now
        form.City = "Mandaue";
        Assert.True((await svc.SaveAsync(ana.UserId, form)).Saved);
    }

    [Fact]
    public async Task Provider_CannotChangeNameOrBusinessName_WithAnActiveBooking()
    {
        var (db, ana, vince) = await WorldAsync();
        await BookAsync(db, ana, vince, BookingStatuses.InProgress);
        var svc = new ProviderProfileService(db);

        var page = (await svc.GetAsync(vince.UserId)).Page!;
        var form = page.Form;
        form.FullName = "Vince Valencia";
        form.BusinessName = "VV Cleaning";
        var result = await svc.SaveAsync(vince.UserId, form);

        Assert.True(page.NameLocked);
        Assert.False(result.Saved);
        Assert.Equal(ActiveBookingRules.NameLockedMessage, result.FieldErrors["FullName"]);
        Assert.Equal(ActiveBookingRules.NameLockedMessage, result.FieldErrors["BusinessName"]);
        Assert.Equal("Vincent Valencia", (await db.Providers.AsNoTracking().SingleAsync()).BusinessName);
    }

    [Fact]
    public async Task Provider_CanRename_WithNoActiveBookings()
    {
        var (db, ana, vince) = await WorldAsync();
        await BookAsync(db, ana, vince, BookingStatuses.Completed);
        var svc = new ProviderProfileService(db);

        var form = (await svc.GetAsync(vince.UserId)).Page!.Form;
        form.BusinessName = "VV Cleaning";
        var result = await svc.SaveAsync(vince.UserId, form);

        Assert.True(result.Saved);
        Assert.Equal("VV Cleaning", (await db.Providers.AsNoTracking().SingleAsync()).BusinessName);
    }
}
