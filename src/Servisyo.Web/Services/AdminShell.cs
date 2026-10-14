using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

/// Two jobs for every admin page: (1) check in the database that the signed-in user really is an
/// ACTIVE administrator, and (2) fill in the top bar / sidebar data (name, initials, bell count).
public static class AdminShell
{
    /// Returns null when the user is not an Active administrator.
    public static async Task<PortalShellViewModel?> BuildAsync(ServisyoDbContext db, int userId, CancellationToken ct)
    {
        var admin = await db.Users.AsNoTracking()
            .Where(u => u.UserId == userId
                     && u.Role == Roles.Administrator
                     && u.AccountStatus == AccountStatuses.Active)
            .Select(u => new { u.FirstName })
            .FirstOrDefaultAsync(ct);
        if (admin is null) return null;

        return new PortalShellViewModel
        {
            Role = "Admin",
            FirstName = admin.FirstName,
            FullName = admin.FirstName,
            Initials = NameFormat.Initials(admin.FirstName),
            Subtitle = "System Administrator",
            UnreadNotifications = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct)
        };
    }
}
