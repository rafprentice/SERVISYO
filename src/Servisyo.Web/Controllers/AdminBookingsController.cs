using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// Read-only monitoring of every booking on the platform (list + details of one booking). Admin only.
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

    /// "View" in the bookings table: the booking's full details. With partial=true only the card is returned
    /// (admin-approval.js shows it in a pop-up); without it, the same card is shown as a normal page.
    [HttpGet("{bookingId:int}")]
    public async Task<IActionResult> Details(int bookingId, string? q, string? status, int page = 1, bool partial = false, CancellationToken ct = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)) return Forbid();

        // the service re-checks that this is an Active administrator
        var lookup = await _bookings.GetDetailsAsync(adminId, bookingId, ct);
        if (!lookup.Allowed)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        // no such booking: the pop-up gets 404 (the script then opens the normal page), the normal page goes back to the list
        if (lookup.Details is null)
        {
            if (partial) return NotFound();
            var back = BookingFilters.NormalizeStatus(status);
            return RedirectToAction(nameof(Index), new { q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(), status = back == BookingFilters.All ? null : back, page = Math.Max(1, page) });
        }

        var vm = lookup.Details;
        vm.ReturnStatus = BookingFilters.NormalizeStatus(status);
        vm.ReturnQ = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        vm.ReturnPage = Math.Max(1, page);

        return partial ? PartialView("_BookingDetails", vm) : View("Details", vm);
    }
}
