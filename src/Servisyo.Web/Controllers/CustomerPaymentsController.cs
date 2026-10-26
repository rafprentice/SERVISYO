using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;

namespace Servisyo.Web.Controllers;

/// "Approve & Pay" (demo payment, no gateway). Customer accounts only; the customer comes from the sign-in cookie,
/// the browser only says WHICH booking.
[Authorize(Roles = Roles.Customer)]
public class CustomerPaymentsController : Controller
{
    private readonly IBookingPaymentService _payments;

    public CustomerPaymentsController(IBookingPaymentService payments) => _payments = payments;

    [HttpPost("customer/bookings/{id:int}/pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int id, string? returnTo, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();

        var result = await _payments.ApproveAndPayAsync(userId, id, ct);
        if (result.Outcome == PaymentOutcome.NotAllowed)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Succeeded ? "ok" : "error";

        // only two known places to go back to (never a URL from the browser)
        return returnTo == "bookings"
            ? RedirectToAction("Index", "CustomerBookings", null, "booking-" + id)
            : RedirectToAction("Customer", "Dashboard");
    }
}
