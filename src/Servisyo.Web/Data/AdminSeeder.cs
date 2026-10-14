using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Models;

namespace Servisyo.Web.Data;

public static class AdminSeeder
{
    /// Creates the first administrator once, using a password from user-secrets
    /// (Seed:AdminPassword). The password is never stored in code or Git.
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServisyoDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeeder");

        try
        {
            if (await db.Users.AnyAsync(u => u.Role == Roles.Administrator))
                return;

            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("No administrator exists and Seed:AdminPassword is not set. Skipping admin seed.");
                return;
            }

            var email = (config["Seed:AdminEmail"] ?? "admin@servisyo.com").Trim().ToLowerInvariant();
            var admin = new User
            {
                FirstName = "Admin",
                LastName = "Servisyo",
                Email = email,
                Role = Roles.Administrator,
                AccountStatus = AccountStatuses.Active
            };
            admin.PasswordHash = hasher.HashPassword(admin, password);

            db.Users.Add(admin);
            await db.SaveChangesAsync();
            logger.LogInformation("Administrator account created.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin seeding failed.");
        }
    }
}
