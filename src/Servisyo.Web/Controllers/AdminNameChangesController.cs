using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// Admin > Name Changes (database v2.8): approved providers' requests to change the name on their account.
/// GET  admin/name-changes                  -> the list (tab Pending by default)
/// POST admin/name-changes/{requestId}      -> Approve / Reject
[Authorize(Roles = Roles.Administrator)]
[Route("admin/name-changes")]
public class AdminNameChangesController : Controller
{
    private readonly INameChangeRequestService _requests;

    public AdminNameChangesController(INameChangeRequestService requests) => _requests = requests;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        var vm = await _requests.GetListAsync(adminId, status, page, ct);
        if (vm is null) return await EndSessionAsync();     // the signed-in account is no longer an active administrator

        return View(vm);
    }

    [HttpPost("{requestId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(int requestId, NameChangeDecisionForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        bool approve;
        if (string.Equals(form.Decision, "Approve", StringComparison.OrdinalIgnoreCase)) approve = true;
        else if (string.Equals(form.Decision, "Reject", StringComparison.OrdinalIgnoreCase)) approve = false;
        else return RedirectWithToast("error", "That action is not recognised.", form.ReturnStatus, form.ReturnPage);

        // The administrator comes from the sign-in cookie; the request is in the address.
        var result = await _requests.DecideAsync(adminId, requestId, approve, ct);
        if (result.Outcome == NameChangeDecisionOutcome.NotAllowed) return await EndSessionAsync();

        return RedirectWithToast(result.Outcome == NameChangeDecisionOutcome.Success ? "ok" : "error", result.Message, form.ReturnStatus, form.ReturnPage);
    }

    private IActionResult RedirectWithToast(string kind, string message, string? status, int page)
    {
        TempData["Toast"] = message;
        TempData["ToastKind"] = kind;

        var tab = NameChangeTabs.Normalize(status);
        return RedirectToAction(nameof(Index), new { status = tab == NameChangeStatuses.Pending ? null : tab, page = Math.Max(1, page) });
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
