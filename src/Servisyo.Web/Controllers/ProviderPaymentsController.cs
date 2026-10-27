using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "Confirm Cash Received" on a Completed Cash-on-Completion booking. Provider accounts only;
/// the provider always comes from the sign-in cookie, the browser only says WHICH booking.
[Authorize(Roles = Roles.ServiceProvider)]
public class ProviderPaymentsController : Controller
{
    private readonly IBookingPaymentService _payments;

    public ProviderPaymentsController(IBookingPaymentService payments) => _payments = payments;

    [HttpPost("provider/bookings/{id:int}/cash-received")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CashReceived(int id, string? tab, int page = 1, CancellationToken ct = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();

        var result = await _payments.ConfirmCashReceivedAsync(userId, id, ct);
        switch (result.Access)
        {
            case ProviderDashboardAccess.Pending:
                return RedirectToAction("PendingApproval", "Account");
            case ProviderDashboardAccess.Rejected:
                return RedirectToAction("Provider", "Dashboard");
            case ProviderDashboardAccess.Allowed:
                break;
            default:
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction("Login", "Account");
        }

        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Succeeded ? "ok" : "error";

        // back to the same tab and page (only known tab keys), scrolled to the booking
        var key = ProviderBookingTab.Find(tab).Key;
        return RedirectToAction("Index", "ProviderBookings", new
        {
            status = key == ProviderBookingTab.AllKey ? null : key,
            page = page > 1 ? page : (int?)null
        }, "booking-" + id);
    }
}
