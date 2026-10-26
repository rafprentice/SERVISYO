using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// Customer > Find Services. Read-only: searching and filtering only change what is SHOWN, so it uses GET
/// (the address can be bookmarked / shared) and needs no antiforgery token.
[Authorize(Roles = Roles.Customer)]
[Route("customer/find-services")]
public class FindServicesController : Controller
{
    private readonly IFindServicesService _find;

    public FindServicesController(IFindServicesService find)
    {
        _find = find;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] FindServicesQuery query, CancellationToken ct = default)
    {
        // Who is asking comes from the sign-in cookie, never from the address or a form.
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Forbid();

        var result = await _find.GetAsync(userId, query ?? new FindServicesQuery(), ct);
        if (!result.Allowed || result.Page is null)
        {
            // The account was suspended / deactivated (or removed) after signing in: end the session.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        return View(result.Page);
    }
}
