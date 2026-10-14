using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ICustomerDashboardService _customerDashboard;
    private readonly IProviderDashboardService _providerDashboard;
    private readonly IAdminDashboardService _adminDashboard;

    public DashboardController(
        ICustomerDashboardService customerDashboard,
        IProviderDashboardService providerDashboard,
        IAdminDashboardService adminDashboard)
    {
        _customerDashboard = customerDashboard;
        _providerDashboard = providerDashboard;
        _adminDashboard = adminDashboard;
    }

    [Authorize(Roles = Roles.Customer)]
    public async Task<IActionResult> Customer(CancellationToken ct)
    {
        // Who is asking comes from the sign-in cookie, never from the URL or a form.
        if (!TryGetUserId(out var userId)) return Forbid();

        var vm = await _customerDashboard.GetAsync(userId, ct);
        if (vm is null) return await EndSessionAsync();

        return View(vm);
    }

    [Authorize(Roles = Roles.ServiceProvider)]
    public async Task<IActionResult> Provider(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _providerDashboard.GetAsync(userId, ct);
        return result.Access switch
        {
            ProviderDashboardAccess.Allowed => View(result.Dashboard),
            ProviderDashboardAccess.Pending => RedirectToAction("PendingApproval", "Account"),
            ProviderDashboardAccess.Rejected => View("ApplicationNotApproved"),
            _ => await EndSessionAsync()   // suspended / deactivated / not a provider account
        };
    }

    [Authorize(Roles = Roles.Administrator)]
    public async Task<IActionResult> Admin(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        // The service re-checks that this is an Active administrator account.
        var vm = await _adminDashboard.GetAsync(userId, ct);
        if (vm is null) return await EndSessionAsync();

        return View(vm);
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
