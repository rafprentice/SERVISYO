using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "My Profile" for customers: personal information, saved address and password.
/// Customer accounts only; the customer is always taken from the sign-in cookie, never from the form.
[Authorize(Roles = Roles.Customer)]
[Route("customer/profile")]
public class CustomerProfileController : Controller
{
    private readonly ICustomerProfileService _profile;

    public CustomerProfileController(ICustomerProfileService profile) => _profile = profile;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _profile.GetAsync(userId, ct);
        if (!result.Allowed) return await EndSessionAsync();

        var page = result.Page!;
        page.FlashMessage = TempData["Flash"] as string;
        page.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(page);
    }

    // ------------------------------------------------------------------ personal information

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CustomerProfileForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _profile.SaveAsync(userId, form, ct);
        if (!result.Allowed) return await EndSessionAsync();

        if (result.Saved)
        {
            // the name and email also live in the sign-in cookie (the public pages show the name), so refresh them there
            await RefreshSignInAsync(result.FullName, result.Email);
            return Done(result.Message);
        }

        // show the page again with what the customer typed and the problems
        var again = await _profile.GetAsync(userId, ct);
        if (!again.Allowed) return await EndSessionAsync();

        var page = again.Page!;
        page.Form = form;
        page.FieldErrors = result.FieldErrors;
        page.FormError = result.FieldErrors.Count == 0 ? result.Message : null;
        return View(nameof(Index), page);
    }

    // ------------------------------------------------------------------ saved address

    [HttpPost("address")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAddress(CustomerAddressForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _profile.SaveAddressAsync(userId, form, ct);
        if (!result.Allowed) return await EndSessionAsync();
        if (result.Saved) return Done(result.Message);

        var again = await _profile.GetAsync(userId, ct);
        if (!again.Allowed) return await EndSessionAsync();

        var page = again.Page!;
        page.AddressForm = form;
        page.AddressError = result.FieldErrors.TryGetValue("Address", out var problem) ? problem : result.Message;
        return View(nameof(Index), page);
    }

    // ------------------------------------------------------------------ password

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(CustomerPasswordForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _profile.ChangePasswordAsync(userId, form, ct);
        if (!result.Allowed) return await EndSessionAsync();
        if (result.Saved) return Done(result.Message);

        // the typed passwords are NOT sent back to the page
        var again = await _profile.GetAsync(userId, ct);
        if (!again.Allowed) return await EndSessionAsync();

        var page = again.Page!;
        page.PasswordErrors = result.FieldErrors;
        page.PasswordFormError = result.FieldErrors.Count == 0 ? result.Message : null;
        return View(nameof(Index), page);
    }

    // ------------------------------------------------------------------ helpers

    /// Reload the page so a refresh does not send the form twice.
    private IActionResult Done(string message)
    {
        TempData["Flash"] = message;
        TempData["FlashKind"] = "ok";
        return RedirectToAction(nameof(Index));
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

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
