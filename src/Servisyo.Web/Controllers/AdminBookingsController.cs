using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// Read-only monitoring of every booking on the platform. Admin only.
[Authorize(Roles = Roles.Administrator)]
[Route("admin/bookings")]
public class AdminBookingsController : Controller
{
    private readonly IBookingMonitorService _bookings;

    public AdminBookingsController(IBookingMonitorService bookings) => _bookings = bookings;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? status, int page = 1, CancellationToken ct = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)) return Forbid();

        var vm = await _bookings.GetBookingsAsync(adminId, new BookingListQuery { Q = q, Status = status, Page = page }, ct);
        if (vm is null)
        {
            // the signed-in account is no longer an active administrator
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        return View(vm);
    }
}
