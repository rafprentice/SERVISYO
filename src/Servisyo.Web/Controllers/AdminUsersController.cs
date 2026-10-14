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
[Route("admin/users")]
public class AdminUsersController : Controller
{
    private readonly IUserManagementService _users;

    public AdminUsersController(IUserManagementService users) => _users = users;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? status, int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        var vm = await _users.GetCustomersAsync(adminId, new UserListQuery { Q = q, Status = status, Page = page }, ct);
        if (vm is null) return await EndSessionAsync();

        vm.FlashMessage = TempData["Flash"] as string;
        vm.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(vm);
    }

    [HttpPost("status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(ChangeStatusForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        if (!ModelState.IsValid)
            return Flash("error", "Please check the form and try again.", form);

        if (!Enum.TryParse<AccountAction>(form.Action, ignoreCase: true, out var action) || !Enum.IsDefined(action))
            return Flash("error", "That action is not recognised.", form);

        // The admin id comes from the sign-in cookie; the browser only says WHICH customer and WHAT to do.
        var result = await _users.ChangeStatusAsync(adminId, form.UserId, action, form.Reason, ct);

        if (result.Outcome == ChangeStatusOutcome.NotAllowed)
            return await EndSessionAsync();     // this admin account is no longer active

        return Flash(result.Outcome == ChangeStatusOutcome.Success ? "ok" : "error", result.Message, form);
    }

    private IActionResult Flash(string kind, string message, ChangeStatusForm form)
    {
        TempData["Flash"] = message;
        TempData["FlashKind"] = kind;
        return RedirectToAction(nameof(Index), new { q = form.Q, status = form.Status, page = form.Page });
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
