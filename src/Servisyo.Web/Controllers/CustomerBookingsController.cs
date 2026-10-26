using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "My Bookings": a customer's own booking history. Customer accounts only; the customer is always taken from the sign-in cookie.
[Authorize(Roles = Roles.Customer)]
[Route("customer/bookings")]
public class CustomerBookingsController : Controller
{
    private readonly ICustomerBookingsService _bookings;

    public CustomerBookingsController(ICustomerBookingsService bookings) => _bookings = bookings;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _bookings.GetAsync(userId, status, page, ct);
        if (!result.Allowed || result.Page is null) return await EndSessionAsync();

        var vm = result.Page;
        vm.FlashMessage = TempData["Flash"] as string;
        vm.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(vm);
    }

    [HttpPost("{id:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancelBookingForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        // stay on the tab and page the customer was looking at (only known tab keys are accepted)
        var tab = CustomerBookingTab.Find(form.Tab).Key;
        var page = Math.Max(1, form.Page);

        // The customer comes from the sign-in cookie; the browser only says WHICH booking.
        var result = await _bookings.CancelAsync(userId, id, ct);
        if (!result.Allowed) return await EndSessionAsync();

        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Succeeded ? "ok" : "error";

        // A cancelled booking leaves the "Pending" tab, so go to "All", where it still shows (as Cancelled).
        if (result.Succeeded && tab == "pending")
        {
            tab = CustomerBookingTab.AllKey;
            page = 1;
        }

        return RedirectToAction(nameof(Index), new
        {
            status = tab == CustomerBookingTab.AllKey ? null : tab,
            page = page > 1 ? page : (int?)null
        });
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    // The account was suspended / deactivated (or removed) after signing in: end the session.
    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
