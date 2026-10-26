using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

[Authorize(Roles = Roles.Administrator)]
[Route("admin/reviews")]
public class AdminReviewsController : Controller
{
    private readonly IReviewModerationService _reviews;

    public AdminReviewsController(IReviewModerationService reviews) => _reviews = reviews;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        var vm = await _reviews.GetReviewsAsync(adminId, new ReviewListQuery { Status = status, Page = page }, ct);
        if (vm is null) return await EndSessionAsync();

        vm.FlashMessage = TempData["Flash"] as string;
        vm.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(vm);
    }

    [HttpPost("moderate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Moderate(ReviewModerationForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        if (!ModelState.IsValid)
            return Flash("error", "Please check the form and try again.", form);

        if (!Enum.TryParse<ReviewAction>(form.Action, ignoreCase: true, out var action) || !Enum.IsDefined(action))
            return Flash("error", "That action is not recognised.", form);

        // The admin id comes from the sign-in cookie; the browser only says WHICH review and WHAT to do.
        var result = await _reviews.ModerateAsync(adminId, form.ReviewId, action, form.Reason, ct);
        if (result.Outcome == ReviewOutcome.NotAllowed) return await EndSessionAsync();

        return Flash(result.Succeeded ? "ok" : "error", result.Message, form);
    }

    private IActionResult Flash(string kind, string message, ReviewModerationForm form)
    {
        TempData["Flash"] = message;
        TempData["FlashKind"] = kind;
        return RedirectToAction(nameof(Index), new { status = form.Status, page = form.Page });
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
