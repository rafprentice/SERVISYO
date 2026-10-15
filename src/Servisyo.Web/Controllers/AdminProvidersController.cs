using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// Admin > Service Providers.
/// GET  admin/providers                    -> the provider cards
/// GET  admin/providers/{id}               -> details page (or just the pop-up contents with ?partial=true)
/// POST admin/providers/{id}/status        -> suspend / deactivate / reactivate / restore the account
[Authorize(Roles = Roles.Administrator)]
[Route("admin/providers")]
public class AdminProvidersController : Controller
{
    private readonly IProviderManagementService _providers;

    public AdminProvidersController(IProviderManagementService providers) => _providers = providers;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        var vm = await _providers.GetProvidersAsync(adminId, new ProviderListQuery { Status = status, Page = page }, ct);
        if (vm is null) return await EndSessionAsync();     // the signed-in account is no longer an active administrator

        return View(vm);
    }

    [HttpGet("{providerId:int}")]
    public async Task<IActionResult> Details(int providerId, string? status, int page = 1, bool partial = false, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        // The service re-checks that this is an Active administrator.
        var lookup = await _providers.GetDetailsAsync(adminId, providerId, ct);
        if (lookup.Outcome == ProviderDetailsOutcome.NotAllowed) return await EndSessionAsync();

        if (lookup.Details is null)
        {
            // JavaScript treats an error answer as "open the normal page instead", which then shows the message.
            if (partial) return NotFound();
            return RedirectWithToast("error", "That provider no longer exists.", status, page);
        }

        var vm = lookup.Details;
        vm.ReturnStatus = ProviderStatuses.Normalize(status);
        vm.ReturnPage = Math.Max(1, page);

        return partial ? PartialView("_ProviderDetails", vm) : View("Details", vm);
    }

    [HttpPost("{providerId:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int providerId, ProviderStatusForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        if (!ModelState.IsValid)
            return RedirectWithToast("error", "The reason can be at most 255 characters. Please try again.", form.ReturnStatus, form.ReturnPage);

        if (!Enum.TryParse<AccountAction>(form.Action, ignoreCase: true, out var action) || !Enum.IsDefined(action))
            return RedirectWithToast("error", "That action is not recognised.", form.ReturnStatus, form.ReturnPage);

        // The administrator comes from the sign-in cookie; the browser only says WHAT to do (the provider is in the address).
        var result = await _providers.ChangeStatusAsync(adminId, providerId, action, form.Reason, ct);
        if (result.Outcome == ChangeStatusOutcome.NotAllowed) return await EndSessionAsync();

        return RedirectWithToast(result.Outcome == ChangeStatusOutcome.Success ? "ok" : "error", result.Message, form.ReturnStatus, form.ReturnPage);
    }

    private IActionResult RedirectWithToast(string kind, string message, string? status, int page)
    {
        TempData["Toast"] = message;
        TempData["ToastKind"] = kind;

        var tab = ProviderStatuses.Normalize(status);
        return RedirectToAction(nameof(Index), new { status = tab == ProviderStatuses.All ? null : tab, page = Math.Max(1, page) });
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
