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
[Route("admin/categories")]
public class AdminCategoriesController : Controller
{
    private readonly ICategoryManagementService _categories;

    public AdminCategoriesController(ICategoryManagementService categories) => _categories = categories;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        var vm = await _categories.GetCategoriesAsync(adminId, ct);
        if (vm is null) return await EndSessionAsync();

        vm.FlashMessage = TempData["Flash"] as string;
        vm.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(vm);
    }

    // "New Category" panel. If something is wrong the page is shown again with the panel open and the text kept.
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        var result = await _categories.CreateAsync(adminId, form.Name, form.Description, ct);
        if (result.Outcome == CategoryOutcome.NotAllowed) return await EndSessionAsync();
        if (result.Succeeded) return Flash("ok", result.Message);

        var vm = await _categories.GetCategoriesAsync(adminId, ct);
        if (vm is null) return await EndSessionAsync();
        vm.ShowAddForm = true;
        vm.NewCategory = form;
        vm.AddFormError = result.Message;
        return View(nameof(Index), vm);
    }

    [HttpPost("update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(CategoryForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        var result = await _categories.UpdateAsync(adminId, form.CategoryId, form.Name, form.Description, ct);
        if (result.Outcome == CategoryOutcome.NotAllowed) return await EndSessionAsync();
        return Flash(result.Succeeded ? "ok" : "error", result.Message);
    }

    [HttpPost("status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(CategoryToggleForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Forbid();

        // The admin id comes from the sign-in cookie; the browser only says WHICH category and enable/disable.
        var result = await _categories.SetActiveAsync(adminId, form.CategoryId, form.Enable, ct);
        if (result.Outcome == CategoryOutcome.NotAllowed) return await EndSessionAsync();
        return Flash(result.Succeeded ? "ok" : "error", result.Message);
    }

    private IActionResult Flash(string kind, string message)
    {
        TempData["Flash"] = message;
        TempData["FlashKind"] = kind;
        return RedirectToAction(nameof(Index));
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
