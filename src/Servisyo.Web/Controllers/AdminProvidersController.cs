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
[Route("admin/providers")]
public class AdminProvidersController : Controller
{
    private readonly IProviderManagementService _providers;

    public AdminProvidersController(IProviderManagementService providers) => _providers = providers;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)) return Forbid();

        var vm = await _providers.GetProvidersAsync(adminId, new ProviderListQuery { Status = status, Page = page }, ct);
        if (vm is null)
        {
            // the signed-in account is no longer an active administrator
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        return View(vm);
    }
}
