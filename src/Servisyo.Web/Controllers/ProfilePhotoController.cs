using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;

namespace Servisyo.Web.Controllers;

/// Upload / remove the signed-in user's profile photo (customers and providers). The user comes from the sign-in cookie.
[Authorize(Roles = Roles.Customer + "," + Roles.ServiceProvider)]
[Route("account/photo")]
public class ProfilePhotoController : Controller
{
    private readonly IProfilePhotoService _photos;

    public ProfilePhotoController(IProfilePhotoService photos) => _photos = photos;

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(3 * 1024 * 1024)]          // a bit above the 2 MB photo limit, so the friendly message can be shown
    public async Task<IActionResult> Upload(IFormFile? photo, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        return await DoneAsync(await _photos.SaveAsync(userId, photo, ct));
    }

    [HttpPost("remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        return await DoneAsync(await _photos.RemoveAsync(userId, ct));
    }

    private async Task<IActionResult> DoneAsync(PhotoResult result)
    {
        if (result.Outcome == PhotoOutcome.NotAllowed)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Succeeded ? "ok" : "error";
        return result.Role == Roles.ServiceProvider
            ? RedirectToAction("Index", "ProviderProfile")
            : RedirectToAction("Index", "CustomerProfile");
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
