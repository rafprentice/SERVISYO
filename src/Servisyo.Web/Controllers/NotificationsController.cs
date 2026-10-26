using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// The notification bell, for customers, providers and administrators.
/// Everything here is about the SIGNED-IN person's own notifications; the browser never says whose.
///   GET  notifications            -> "all notifications" page
///   GET  notifications/panel      -> JSON for the dropdown (and for the bell's number)
///   POST notifications/read-all   -> mark everything read
///   GET  notifications/{id}/go    -> mark that one read, then open the page it is about
[Authorize(Roles = Roles.Customer + "," + Roles.ServiceProvider + "," + Roles.Administrator)]
[Route("notifications")]
public class NotificationsController : Controller
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var vm = await _notifications.GetPageAsync(userId, page, ct);
        if (vm is null) return await EndSessionAsync();

        return View(vm);
    }

    [HttpGet("panel")]
    public async Task<IActionResult> Panel(CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var panel = await _notifications.GetPanelAsync(userId, ct);
        if (panel is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Unauthorized();
        }

        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            unread = panel.Unread,
            viewAllUrl = Url.Action(nameof(Index), "Notifications"),
            items = panel.Items.Select(i => new
            {
                id = i.Id,
                title = i.Title,
                message = i.Message,
                time = i.TimeText,
                read = i.IsRead,
                icon = i.Icon,
                url = Url.Action(nameof(Go), "Notifications", new { id = i.Id })
            })
        });
    }

    [HttpPost("read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var wantsJson = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (!TryGetUserId(out var userId)) return wantsJson ? Unauthorized() : Forbid();

        var unread = await _notifications.MarkAllReadAsync(userId, ct);
        if (unread is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return wantsJson ? Unauthorized() : RedirectToAction("Login", "Account");
        }

        return wantsJson ? Json(new { unread = 0 }) : RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/go")]
    public async Task<IActionResult> Go(int id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _notifications.OpenAsync(userId, id, ct);
        if (!result.Allowed) return await EndSessionAsync();
        if (result.Item is null) return RedirectToAction(nameof(Index));      // not found (or not yours)

        return Redirect(TargetFor(result.Item.Kind));
    }

    /// Where a notification leads, depending on who is looking. Always a page of this site.
    private string TargetFor(string kind)
    {
        var admin = User.IsInRole(Roles.Administrator);
        var provider = User.IsInRole(Roles.ServiceProvider);

        string? url = kind switch
        {
            NotificationKinds.BookingNew or NotificationKinds.BookingCancelled or NotificationKinds.BookingUpdate =>
                admin ? Url.Action("Index", "AdminBookings")
                : provider ? Url.Action("Index", "ProviderBookings")
                : Url.Action("Index", "CustomerBookings"),
            NotificationKinds.ApplicationNew when admin => Url.Action("Index", "AdminProviders", new { status = "Pending" }),
            _ => null
        };

        return url ?? (admin ? Url.Action("Admin", "Dashboard")
                     : provider ? Url.Action("Provider", "Dashboard")
                     : Url.Action("Customer", "Dashboard")) ?? "/";
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
