using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "My Services": a provider's own offerings. Provider accounts only; the provider is always taken from the sign-in cookie.
[Authorize(Roles = Roles.ServiceProvider)]
[Route("provider/services")]
public class ProviderServicesController : Controller
{
    private readonly IProviderOfferingsService _offerings;

    public ProviderServicesController(IProviderOfferingsService offerings) => _offerings = offerings;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _offerings.GetAsync(userId, null, null, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        var page = result.Page!;
        page.FlashMessage = TempData["Flash"] as string;
        page.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(page);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AddServiceForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _offerings.AddAsync(userId, form, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        if (result.Added)
        {
            TempData["Flash"] = result.Message;
            TempData["FlashKind"] = "ok";
            return RedirectToAction(nameof(Index));          // reload so a refresh does not add the service twice
        }

        // show the page again with the dialog open, what the provider typed, and the problems
        var again = await _offerings.GetAsync(userId, form, result, ct);
        var deniedAgain = Denied(again.Access);
        if (deniedAgain is not null) return await deniedAgain;

        return View(nameof(Index), again.Page!);
    }

    [HttpPost("{serviceId:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int serviceId, ServiceStatusForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        // The provider comes from the sign-in cookie; the browser only says WHICH service and what state it wants.
        var result = await _offerings.SetActiveAsync(userId, serviceId, form.Activate, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Succeeded ? "ok" : "error";
        return RedirectToAction(nameof(Index));
    }

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
