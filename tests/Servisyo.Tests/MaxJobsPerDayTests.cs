using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.6: a provider accepts at most 20 jobs a day. The number lives in ProviderLimits; the registration form,
/// the service checks, the Availability page, the database CHECK and usp_RegisterProvider must all say 20.
public class MaxJobsPerDayTests
{
    private static ServisyoDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static string DesignFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "sql", "DatabaseDesign-v2.sql"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "sql", "DatabaseDesign-v2.sql"));
    }

    [Fact]
    public void TheLimit_Is20()
    {
        Assert.Equal(20, ProviderLimits.MaxJobsPerDay);
        Assert.Equal(20, ProviderAvailabilityService.MaxJobsLimit);
        Assert.Equal("Enter a whole number of jobs from 1 to 20.", ProviderLimits.MaxJobsMessage);
    }

    // ------------------------------------------------------------ registration

    [Theory]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    [InlineData(50, false)]
    [InlineData(0, false)]
    public void TheRegistrationForm_Accepts1To20(int jobs, bool ok)
    {
        var form = new RegisterProviderViewModel { MaxJobsPerDay = jobs };
        var results = new List<ValidationResult>();
        Validator.TryValidateProperty(form.MaxJobsPerDay,
            new ValidationContext(form) { MemberName = nameof(RegisterProviderViewModel.MaxJobsPerDay) }, results);

        Assert.Equal(ok, results.Count == 0);
        if (!ok) Assert.Equal(ProviderLimits.MaxJobsMessage, results.Single().ErrorMessage);
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public async Task TheService_RefusesMoreThan20_EvenIfThePageIsSkipped(int jobs, bool ok)
    {
        var db = NewDb();
        var auth = new AuthService(db, new PasswordHasher<User>());

        var result = await auth.RegisterProviderAsync(new RegisterProviderViewModel
        {
            FirstName = "Juan", MiddleName = "Reyes", LastName = "Dela Cruz", Email = "juan@email.com", PhoneNumber = "09171234567",
            Password = "Passw0rd1", ConfirmPassword = "Passw0rd1", BusinessName = "Juan Plumbing Services",
            Description = "Pipe repair and installation.", Address = "Lahug, Cebu City", BaseFee = 500m, MinNoticeDays = 1, MaxJobsPerDay = jobs
        });

        Assert.Equal(ok, result.Succeeded);
        Assert.Equal(ok ? 1 : 0, await db.Providers.CountAsync());
        if (ok) Assert.Equal(20, (await db.Providers.SingleAsync()).MaxJobsPerDay);
    }

    // ------------------------------------------------------------ the database

    [Fact]
    public void TheDatabaseCheck_AllowsOnly1To20()
    {
        var model = NewDb().GetService<IDesignTimeModel>().Model;
        var check = Assert.Single(model.FindEntityType(typeof(Provider))!.GetCheckConstraints(), c => c.ModelName == "CK_PROVIDERS_LIMITS");
        Assert.Equal("[MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] BETWEEN 1 AND 20", check.Sql);
        Assert.Contains($"BETWEEN 1 AND {ProviderLimits.MaxJobsPerDay}", ServisyoDbContext.ProviderLimitsCheck);
    }

    [Fact]
    public void TheRegisterProcedure_OnlyChangesItsLimitLine()
    {
        Assert.StartsWith("EXEC(N'CREATE OR ALTER PROCEDURE dbo.usp_RegisterProvider", MaxJobsSql.RegisterProvider20);
        Assert.Contains("@MaxJobsPerDay > 20 THROW 50110, N''Enter a whole number of jobs per day from 1 to 20.''", MaxJobsSql.RegisterProvider20);
        Assert.DoesNotContain("1 to 50", MaxJobsSql.RegisterProvider20);
        Assert.Contains("1 to 50", MaxJobsSql.RegisterProviderV23);                          // what Down puts back
        Assert.Contains(DatabaseV2Sql.Marker, MaxJobsSql.RegisterProvider20);                 // still recognised as ours

        // everything else is the same text, line by line
        var before = MaxJobsSql.RegisterProviderV23.Split('\n');
        var after = MaxJobsSql.RegisterProvider20.Split('\n');
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(2, before.Zip(after).Count(pair => pair.First != pair.Second));          // the marker comment and the limit
        Assert.Equal("UPDATE dbo.SERVICE_PROVIDERS SET MAX_JOBS_PER_DAY = 20 WHERE MAX_JOBS_PER_DAY > 20;", MaxJobsSql.CapExisting);
    }

    [Fact]
    public void TheDesignFile_SaysTheSame()
    {
        var design = DesignFile();
        Assert.Contains("DATABASE DESIGN v2.8", design);                         // v2.7 and v2.8 came after
        Assert.Contains("CONSTRAINT CK_PROVIDERS_LIMITS CHECK (" + ServisyoDbContext.ProviderLimitsCheck + ")", design);
        Assert.Contains("@MaxJobsPerDay > 20 THROW 50110, N'Enter a whole number of jobs per day from 1 to 20.', 1;", design);
        Assert.DoesNotContain("jobs per day from 1 to 50", design);
    }

    [Fact]
    public void TheRegisterPage_AndTheAvailabilityPage_Use20()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Servisyo.Web"))) dir = dir.Parent;
        var web = Path.Combine(dir!.FullName, "src", "Servisyo.Web");
        Assert.Contains("max=\"@Servisyo.Web.Models.ProviderLimits.MaxJobsPerDay\"", File.ReadAllText(Path.Combine(web, "Views", "Account", "RegisterProvider.cshtml")));
        Assert.Contains("max=\"@ProviderAvailabilityService.MaxJobsLimit\"", File.ReadAllText(Path.Combine(web, "Views", "ProviderAvailability", "Index.cshtml")));
        Assert.DoesNotContain("|| 50", File.ReadAllText(Path.Combine(web, "wwwroot", "js", "provider-availability.js")));
    }
}
