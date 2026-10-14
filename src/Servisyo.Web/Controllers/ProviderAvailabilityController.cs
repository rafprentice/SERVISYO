using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

[Authorize(Roles = Roles.ServiceProvider)]
[Route("provider/availability")]
public class ProviderAvailabilityController : Controller
{
    private readonly IProviderAvailabilityService _availability;

    public ProviderAvailabilityController(IProviderAvailabilityService availability) => _availability = availability;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _availability.GetAsync(userId, null, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        var page = result.Page!;
        page.FlashMessage = TempData["Flash"] as string;
        page.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(page);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AvailabilityForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        IReadOnlyList<string> errors;

        if (!ModelState.IsValid)
        {
            // e.g. a time or date the server could not read
            errors = new[] { "Some times or dates could not be read. Please check them and try again." };
        }
        else
        {
            var result = await _availability.SaveAsync(userId, form, ct);
            var denied = Denied(result.Access);
            if (denied is not null) return await denied;

            if (result.Saved)
            {
                TempData["Flash"] = result.Message;
                TempData["FlashKind"] = "ok";
                return RedirectToAction(nameof(Index));          // reload so a refresh does not send the form twice
            }
            errors = result.Errors;
        }

        // show the page again with what the provider typed, plus the problems
        var again = await _availability.GetAsync(userId, form, ct);
        var deniedAgain = Denied(again.Access);
        if (deniedAgain is not null) return await deniedAgain;

        var page = again.Page!;
        page.Errors = errors;
        return View(nameof(Index), page);
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
