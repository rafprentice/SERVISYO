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
            // e.g. a time the server could not read
            errors = new[] { "Some times could not be read. Please check them and try again." };
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

        // 400 Bad Request: nothing was saved (e.g. a break on a short shift); the page shows what to fix
        var view = View(nameof(Index), page);
        view.StatusCode = StatusCodes.Status400BadRequest;
        return view;
    }

    // ---------------------------------------------------------- day-off requests (database v2.7)

    [HttpPost("day-off")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestDayOff(DayOffRequestForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = ModelState.IsValid
            ? await _availability.RequestDayOffAsync(userId, form, ct)
            : DayOffResult.Refused("The dates could not be read. Please choose them again.");
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        if (result.Done)
        {
            TempData["Flash"] = result.Message;
            TempData["FlashKind"] = "ok";
            return RedirectToAction(nameof(Index));
        }

        // 400 Bad Request: nothing was sent (e.g. more than 7 days, a same-day request); the form opens again with the problems
        var again = await _availability.GetAsync(userId, null, ct);
        var deniedAgain = Denied(again.Access);
        if (deniedAgain is not null) return await deniedAgain;

        var page = again.Page!;
        page.DayOffForm = form;
        page.DayOffErrors = result.Errors;
        var view = View(nameof(Index), page);
        view.StatusCode = StatusCodes.Status400BadRequest;
        return view;
    }

    [HttpPost("day-off/{requestId:int}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRequest(int requestId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        return await AfterAsync(await _availability.RemoveRequestAsync(userId, requestId, ct));
    }

    [HttpPost("days-off/{unavailableId:int}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDay(int unavailableId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        return await AfterAsync(await _availability.RemoveDayAsync(userId, unavailableId, ct));
    }

    private async Task<IActionResult> AfterAsync(DayOffResult result)
    {
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;
        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Done ? "ok" : "error";
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
