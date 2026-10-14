using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "My Profile": a provider's own business information. Provider accounts only; the provider is always taken from the sign-in cookie.
[Authorize(Roles = Roles.ServiceProvider)]
[Route("provider/profile")]
public class ProviderProfileController : Controller
{
    private readonly IProviderProfileService _profile;

    public ProviderProfileController(IProviderProfileService profile) => _profile = profile;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _profile.GetAsync(userId, null, null, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        var page = result.Page!;
        page.FlashMessage = TempData["Flash"] as string;
        page.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(page);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProviderProfileForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _profile.SaveAsync(userId, form, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        if (result.Saved)
        {
            // the name and email also live in the sign-in cookie (the public pages show the name), so refresh them there
            await RefreshSignInAsync(result.FullName, result.Email);

            TempData["Flash"] = result.Message;
            TempData["FlashKind"] = "ok";
            return RedirectToAction(nameof(Index));          // reload so a refresh does not send the form twice
        }

        // show the page again with what the provider typed and the problems
        var again = await _profile.GetAsync(userId, form, result, ct);
        var deniedAgain = Denied(again.Access);
        if (deniedAgain is not null) return await deniedAgain;

        return View(nameof(Index), again.Page!);
    }

    /// Re-issues the sign-in cookie with the new name and email. The user id and role are carried over unchanged,
    /// and so are the "remember me" settings.
    private async Task RefreshSignInAsync(string fullName, string email)
    {
        var current = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!current.Succeeded || current.Principal is null) return;

        var claims = current.Principal.Claims
            .Where(c => c.Type != ClaimTypes.Name && c.Type != ClaimTypes.Email)
            .ToList();
        claims.Add(new Claim(ClaimTypes.Name, fullName));
        claims.Add(new Claim(ClaimTypes.Email, email));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            current.Properties);
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
