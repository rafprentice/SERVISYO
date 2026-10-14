using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// Messages: a customer talks to the providers of their bookings, a provider to the customers of theirs.
/// Customers and approved providers only. The person is always taken from the sign-in cookie.
[Authorize(Roles = Roles.Customer + "," + Roles.ServiceProvider)]
[Route("messages")]
public class MessagesController : Controller
{
    private readonly IMessagingService _messages;

    public MessagesController(IMessagingService messages) => _messages = messages;

    // ------------------------------------------------------------------ the page

    [HttpGet("")]
    public async Task<IActionResult> Index(int? with, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _messages.GetPageAsync(userId, with, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        var vm = result.Page!;
        vm.FlashMessage = TempData["Flash"] as string;
        vm.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(vm);
    }

    // ------------------------------------------------------------------ sending

    /// Used by the page's JavaScript (answers with JSON) and, if JavaScript is off, by the plain form (redirects back).
    [HttpPost("{partnerId:int}/send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int partnerId, SendMessageForm form, CancellationToken ct)
    {
        var wantsJson = WantsJson();
        if (!TryGetUserId(out var userId))
        {
            if (wantsJson) return StatusCode(StatusCodes.Status401Unauthorized, new { ok = false, error = "Please sign in again." });
            return Forbid();
        }

        var result = await _messages.SendAsync(userId, partnerId, form.Text, ct);

        if (result.Access != ProviderDashboardAccess.Allowed)
        {
            if (wantsJson)
            {
                await SignOutIfNotApprovedAsync(result.Access);
                return StatusCode(StatusCodes.Status401Unauthorized, new { ok = false, error = "Your session has ended. Please sign in again." });
            }
            var denied = Denied(result.Access);
            if (denied is not null) return await denied;
        }

        if (wantsJson)
        {
            return result.Outcome switch
            {
                SendOutcome.Sent => (IActionResult)Json(new { ok = true, sent = result.Sent }),
                SendOutcome.Invalid => BadRequest(new { ok = false, error = result.Message }),
                _ => StatusCode(StatusCodes.Status403Forbidden, new { ok = false, error = result.Message })
            };
        }

        if (!result.Succeeded)
        {
            TempData["Flash"] = result.Message;
            TempData["FlashKind"] = "error";
        }
        return RedirectToAction(nameof(Index), new { with = partnerId });      // reload so a refresh does not send it again
    }

    // ------------------------------------------------------------------ what is new (asked every few seconds)

    [HttpGet("poll")]
    public async Task<IActionResult> Poll(int? with, int after = 0, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await _messages.PollAsync(userId, with, after, ct);
        if (result.Access != ProviderDashboardAccess.Allowed)
        {
            await SignOutIfNotApprovedAsync(result.Access);
            return Unauthorized();
        }

        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            messages = result.Messages,
            conversations = result.Conversations,
            unreadTotal = result.UnreadTotal
        });
    }

    [HttpPost("{partnerId:int}/read")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(int partnerId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await _messages.MarkReadAsync(userId, partnerId, ct);
        if (result.Access != ProviderDashboardAccess.Allowed)
        {
            await SignOutIfNotApprovedAsync(result.Access);
            return Unauthorized();
        }

        return Json(new { unreadTotal = result.UnreadTotal });
    }

    // ------------------------------------------------------------------ helpers

    private bool WantsJson() =>
        Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    // Pending applicants wait on their own page; rejected providers see the dashboard message; anything else is signed out.
    private Task<IActionResult>? Denied(ProviderDashboardAccess access) => access switch
    {
        ProviderDashboardAccess.Allowed => null,
        ProviderDashboardAccess.Pending => Task.FromResult<IActionResult>(RedirectToAction("PendingApproval", "Account")),
        ProviderDashboardAccess.Rejected => Task.FromResult<IActionResult>(RedirectToAction("Provider", "Dashboard")),
        _ => EndSessionAsync()
    };

    // JSON callers cannot follow a redirect, so a suspended / deactivated account is simply signed out and told 401.
    private async Task SignOutIfNotApprovedAsync(ProviderDashboardAccess access)
    {
        if (access == ProviderDashboardAccess.NotAllowed)
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
