using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "Approve & Pay" (demo payment, no gateway): Cash on Service Completion or Mock E-Wallet.
/// Customer accounts only; the customer comes from the sign-in cookie, the browser only says WHICH booking and HOW to pay.
[Authorize(Roles = Roles.Customer)]
public class CustomerPaymentsController : Controller
{
    private readonly IBookingPaymentService _payments;

    public CustomerPaymentsController(IBookingPaymentService payments) => _payments = payments;

    [HttpPost("customer/bookings/{id:int}/pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int id, PaymentChoiceForm form, string? returnTo, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();

        var result = await _payments.ApproveAndPayAsync(userId, id, form, ct);
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
