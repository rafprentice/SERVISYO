using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Models;

namespace Servisyo.Web.Data;

public static class CategorySeeder
{
    /// Adds the three starter categories from the project documentation, but only
    /// when the SERVICE_CATEGORIES table is completely empty. Administrators can
    /// rename or add categories later (Stage 6).
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServisyoDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CategorySeeder");

        try
        {
            if (await db.ServiceCategories.AnyAsync())
                return;

            db.ServiceCategories.AddRange(
                new ServiceCategory { CategoryName = "Plumbing", Description = "Leaks, pipes, fixtures and installation." },
                new ServiceCategory { CategoryName = "Cleaning", Description = "Residential and commercial cleaning for homes and offices." },
                new ServiceCategory { CategoryName = "Repair",   Description = "Appliance, furniture and general home repairs." });

            await db.SaveChangesAsync();
            logger.LogInformation("Starter service categories created.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Category seeding failed.");
        }
    }
}
