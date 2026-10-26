using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// Review and decide on provider applications. Admin only.
/// GET  admin/applications/{id}            -> the review page (or just the dialog contents with ?partial=true)
/// POST admin/applications/{id}/decision   -> approve or reject
[Authorize(Roles = Roles.Administrator)]
[Route("admin/applications")]
public class AdminApplicationsController : Controller
{
    private readonly IProviderApprovalService _approvals;

    public AdminApplicationsController(IProviderApprovalService approvals) => _approvals = approvals;

    [HttpGet("{providerId:int}")]
    public async Task<IActionResult> Review(int providerId, string? returnTo, string? status, int page = 1,
        bool partial = false, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        // The service re-checks that this is an Active administrator.
        var lookup = await _approvals.GetApplicationAsync(adminId, providerId, ct);
        if (lookup.Outcome == ApprovalOutcome.NotAllowed) return await EndSessionAsync();

        if (lookup.Application is null)
        {
            // JavaScript treats an error answer as "open the normal page instead", which then shows the message.
            if (partial) return NotFound();
            return RedirectWithToast("error", "That application no longer exists.", returnTo, status, page);
        }

        var vm = lookup.Application;
        vm.ReturnTo = ApplicationReturn.Normalize(returnTo);
        vm.ReturnStatus = ProviderStatuses.Normalize(status);
        vm.ReturnPage = Math.Max(1, page);

        return partial ? PartialView("_ApplicationReview", vm) : View("Review", vm);
    }

    [HttpPost("{providerId:int}/decision")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(int providerId, ApplicationDecisionForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        if (!ModelState.IsValid)
            return RedirectWithToast("error", "The note can be at most 255 characters. Please try again.", form);

        if (!Enum.TryParse<ApprovalDecision>(form.Decision, ignoreCase: true, out var decision) || !Enum.IsDefined(decision))
            return RedirectWithToast("error", "That action is not recognised.", form);

        // The administrator comes from the sign-in cookie; the browser only says WHICH application and WHAT to do.
        var result = await _approvals.DecideAsync(adminId, providerId, decision, form.Reason, ct);
        if (result.Outcome == ApprovalOutcome.NotAllowed) return await EndSessionAsync();

        return RedirectWithToast(result.Succeeded ? "ok" : "error", result.Message, form);
    }

    private IActionResult RedirectWithToast(string kind, string message, ApplicationDecisionForm form) =>
        RedirectWithToast(kind, message, form.ReturnTo, form.ReturnStatus, form.ReturnPage);

    private IActionResult RedirectWithToast(string kind, string message, string? returnTo, string? status, int page)
    {
        TempData["Toast"] = message;
        TempData["ToastKind"] = kind;

        if (ApplicationReturn.Normalize(returnTo) == ApplicationReturn.Providers)
        {
            var tab = ProviderStatuses.Normalize(status);
            return RedirectToAction("Index", "AdminProviders",
                new { status = tab == ProviderStatuses.All ? null : tab, page = Math.Max(1, page) });
        }
        return RedirectToAction("Admin", "Dashboard");
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
