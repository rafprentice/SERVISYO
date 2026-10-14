using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "Booking Requests": a provider's own bookings. Provider accounts only; the provider is always taken from the sign-in cookie.
[Authorize(Roles = Roles.ServiceProvider)]
[Route("provider/bookings")]
public class ProviderBookingsController : Controller
{
    private readonly IProviderBookingsService _bookings;

    public ProviderBookingsController(IProviderBookingsService bookings) => _bookings = bookings;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _bookings.GetAsync(userId, status, page, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        var vm = result.Page!;
        vm.FlashMessage = TempData["Flash"] as string;
        vm.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(vm);
    }

    [HttpPost("{id:int}/action")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(int id, BookingActionForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        // stay on the tab and page the provider was looking at (only known tab keys are accepted)
        var tab = ProviderBookingTab.Find(form.Tab).Key;
        var page = Math.Max(1, form.Page);

        // The provider comes from the sign-in cookie; the browser only says WHICH booking and WHICH of the four actions.
        if (form.Action is not { } action || !ModelState.IsValid)
        {
            TempData["Flash"] = "That action is not available.";
            TempData["FlashKind"] = "error";
            return RedirectBack(tab, page);
        }

        var result = await _bookings.ChangeStatusAsync(userId, id, action, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Succeeded ? "ok" : "error";
        return RedirectBack(tab, page);                 // reload so a refresh does not repeat the action
    }

    private IActionResult RedirectBack(string tab, int page) =>
        RedirectToAction(nameof(Index), new
        {
            status = tab == ProviderBookingTab.AllKey ? null : tab,
            page = page > 1 ? page : (int?)null
        });

    // Pending applicants wait on their own page; rejected providers see the dashboard message; anything else is signed out.
    private Task<IActionResult>? Denied(ProviderDashboardAccess access) => access switch
    {
        ProviderDashboardAccess.Allowed => null,
        ProviderDashboardAccess.Pending => Task.FromResult<IActionResult>(RedirectToAction("PendingApproval", "Account")),
        ProviderDashboardAccess.Rejected => Task.FromResult<IActionResult>(RedirectToAction("Provider", "Dashboard")),
        _ => EndSessionAsync()
    };

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
