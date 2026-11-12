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
/// Database v2.8: an approved provider's name is locked, so they send a name change request here (an administrator answers).
[Authorize(Roles = Roles.ServiceProvider)]
[Route("provider/profile")]
public class ProviderProfileController : Controller
{
    private readonly IProviderProfileService _profile;
    private readonly INameChangeRequestService _nameChanges;

    public ProviderProfileController(IProviderProfileService profile, INameChangeRequestService nameChanges)
    {
        _profile = profile;
        _nameChanges = nameChanges;
    }

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
        page.NameChange = await _nameChanges.GetSectionAsync(userId, ct);

        // an administrator may have approved a name change since the provider signed in: the public pages show the
        // name from the sign-in cookie, so bring it up to date (the database is always the source)
        if (!string.Equals((User.FindFirstValue(ClaimTypes.Name) ?? string.Empty).Trim(), page.FullName, StringComparison.Ordinal))
            await RefreshSignInAsync(page.FullName, page.Form.Email ?? User.FindFirstValue(ClaimTypes.Email) ?? string.Empty);

        return View(page);
    }

    // ---------------------------------------------------------- name change requests (database v2.8)

    [HttpPost("name-change")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestNameChange(NameChangeForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        // the provider comes from the sign-in cookie; the service checks they are an Active, Approved provider
        var result = await _nameChanges.RequestAsync(userId, form, ct);
        if (result.Done || result.Outcome == NameChangeOutcome.NotAllowed)
        {
            if (result.Done)
            {
                TempData["Flash"] = result.Message;
                TempData["FlashKind"] = "ok";
            }
            return RedirectToAction(nameof(Index));     // NotAllowed: Index sends a pending / rejected / inactive account away
        }

        // refused: the page again (400) with what was typed and the problems, the pop-up open
        var shown = await _profile.GetAsync(userId, null, null, ct);
        var denied = Denied(shown.Access);
        if (denied is not null) return await denied;

        var page = shown.Page!;
        var section = await _nameChanges.GetSectionAsync(userId, ct);
        if (section is not null)
        {
            section.Form = form;
            section.FieldErrors = result.FieldErrors;
            section.FormError = result.FieldErrors.Count == 0 ? result.Message : null;
            section.OpenDialog = section.CanRequest;
        }
        if (section is null || !section.CanRequest)
        {
            // e.g. a request is already waiting: say it at the top (the pop-up cannot open while one waits)
            page.FlashMessage = result.Message;
            page.FlashIsError = true;
        }
        page.NameChange = section;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Index), page);
    }

    [HttpPost("name-change/{requestId:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelNameChange(int requestId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        // only the signed-in provider's OWN request, and only while it is Pending (checked by the service)
        var result = await _nameChanges.CancelAsync(userId, requestId, ct);
        if (result.Outcome != NameChangeOutcome.NotAllowed)
        {
            TempData["Flash"] = result.Message;
            TempData["FlashKind"] = result.Done ? "ok" : "error";
        }
        return RedirectToAction(nameof(Index));
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
            shown.Page!.NameChange = await _nameChanges.GetSectionAsync(userId, ct);
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

        again.Page!.NameChange = await _nameChanges.GetSectionAsync(userId, ct);
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
