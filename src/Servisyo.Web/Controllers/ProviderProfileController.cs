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

        // The locked fields (names, business name, phone, address, base fee) are never saved from this form, so a value
        // the model binder could not read there does not matter; a real attempt to change one is refused with 403 below.
        foreach (var field in ProviderLockRules.LockedFields)
            ModelState.Remove(field);

        // nothing is saved while ModelState is invalid
        if (!ModelState.IsValid)
        {
            var fieldErrors = ModelState
                .Where(entry => entry.Value is { Errors.Count: > 0 })
                .ToDictionary(entry => entry.Key,
                              entry => entry.Value!.Errors[0].ErrorMessage is { Length: > 0 } text ? text : "Please check this field.");

            var refused = new ProviderProfileSaveResult(ProviderDashboardAccess.Allowed, false, fieldErrors, "Please fix the highlighted fields.");
            var shown = await _profile.GetAsync(userId, form, refused, ct);   // also checks the account is an approved, active provider
            var deniedShown = Denied(shown.Access);
            if (deniedShown is not null) return await deniedShown;
            return View(nameof(Index), shown.Page!);
        }

        // the service checks the provider's status FIRST: an approved provider who tries to change locked information
        // gets nothing saved, and this request is answered with 403 Forbidden (the page never sends those fields)
        var result = await _profile.SaveAsync(userId, form, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;
        if (result.LockViolation)
            return StatusCode(StatusCodes.Status403Forbidden, ProviderLockRules.ApprovedNotice);

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
