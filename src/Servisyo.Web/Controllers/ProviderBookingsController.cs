using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "Booking Requests": a provider's own bookings. Provider accounts only; the provider is always taken from the sign-in cookie.
[Authorize(Roles = Roles.ServiceProvider)]
[Route("provider/bookings")]
public class ProviderBookingsController : Controller
{
    private readonly IProviderBookingsService _bookings;

    public ProviderBookingsController(IProviderBookingsService bookings) => _bookings = bookings;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _bookings.GetAsync(userId, status, page, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        var vm = result.Page!;
        vm.FlashMessage = TempData["Flash"] as string;
        vm.FlashIsError = TempData["FlashKind"] as string == "error";
        return View(vm);
    }

    [HttpPost("{id:int}/action")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(int id, BookingActionForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        // stay on the tab and page the provider was looking at (only known tab keys are accepted)
        var tab = ProviderBookingTab.Find(form.Tab).Key;
        var page = Math.Max(1, form.Page);

        // The provider comes from the sign-in cookie; the browser only says WHICH booking and WHICH of the four actions.
        if (form.Action is not { } action || !ModelState.IsValid)
        {
            TempData["Flash"] = "That action is not available.";
            TempData["FlashKind"] = "error";
            return RedirectBack(tab, page);
        }

        var result = await _bookings.ChangeStatusAsync(userId, id, action, ct);
        var denied = Denied(result.Access);
        if (denied is not null) return await denied;

        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Succeeded ? "ok" : "error";
        return RedirectBack(tab, page);                 // reload so a refresh does not repeat the action
    }

    /// Database v2.11: the "Confirm Service Completion" window (multipart/form-data, sent by booking-completion.js).
    /// The provider comes from the sign-in cookie; the browser only says WHICH booking, the message and the photo.
    /// Answers with JSON: 200 { redirect } when completed (the page shows the green message), 400 { errors } when the
    /// message or the photo is missing or wrong, 404 / 409 { message } when the booking is not theirs or not In Progress.
    [HttpPost("{id:int}/complete")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(CompletionRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CompletionRules.MaxRequestBytes)]
    public async Task<IActionResult> Complete(int id, [FromForm] string? completionMessage, IFormFile? proofPhoto,
        [FromForm] string? tab, [FromForm] int page = 1, CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _bookings.CompleteAsync(userId, id, completionMessage, proofPhoto, ct);
        switch (result.Access)
        {
            case ProviderDashboardAccess.Allowed: break;
            case ProviderDashboardAccess.Pending: return StatusCode(StatusCodes.Status403Forbidden, new { redirect = Url.Action("PendingApproval", "Account") });
            case ProviderDashboardAccess.Rejected: return StatusCode(StatusCodes.Status403Forbidden, new { redirect = Url.Action("Provider", "Dashboard") });
            default:
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Unauthorized(new { redirect = Url.Action("Login", "Account") });
        }

        switch (result.Outcome)
        {
            case BookingActionOutcome.Success:
                TempData["Flash"] = result.Message;
                TempData["FlashKind"] = "ok";
                var back = ProviderBookingTab.Find(tab).Key;
                return Ok(new
                {
                    message = result.Message,
                    redirect = Url.Action(nameof(Index), new { status = back == ProviderBookingTab.AllKey ? null : back, page = page > 1 ? page : (int?)null })
                });
            case BookingActionOutcome.ProofMissing:
                return BadRequest(new
                {
                    message = result.Message,
                    errors = new { completionMessage = result.Errors?.Message, proofPhoto = result.Errors?.Photo }
                });
            case BookingActionOutcome.NotFound:
                return NotFound(new { message = result.Message });
            default:
                return Conflict(new { message = result.Message });
        }
    }

    /// Database v2.12: "Mark In Progress" and "Mark In Progress (Late)" (booking-start.js sends both with fetch; without
    /// JavaScript they are plain forms). The provider comes from the sign-in cookie; the browser only says WHICH booking
    /// and, when late, the reason. The start window is checked on the server in Philippine time:
    ///   400 { message }                   before the scheduled date and time, or more than 2 hours after it;
    ///   400 { message, errors.lateReason } more than 15 minutes late without a reason (or one over 500 characters);
    ///   200 { message, redirect }          started (the page shows the green message);
    ///   404 / 409 { message }             not their booking / not Accepted and paid any more.
    /// A plain form post is answered with the same message on the reloaded page instead.
    [HttpPost("{id:int}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id, [FromForm] string? lateReason, [FromForm] string? tab, [FromForm] int page = 1,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var back = ProviderBookingTab.Find(tab).Key;
        page = Math.Max(1, page);
        var result = await _bookings.StartAsync(userId, id, lateReason, ct);

        if (!WantsJson())
        {
            var denied = Denied(result.Access);
            if (denied is not null) return await denied;
            TempData["Flash"] = result.Message;
            TempData["FlashKind"] = result.Succeeded ? "ok" : "error";
            return RedirectBack(back, page);
        }

        switch (result.Access)
        {
            case ProviderDashboardAccess.Allowed: break;
            case ProviderDashboardAccess.Pending: return StatusCode(StatusCodes.Status403Forbidden, new { redirect = Url.Action("PendingApproval", "Account") });
            case ProviderDashboardAccess.Rejected: return StatusCode(StatusCodes.Status403Forbidden, new { redirect = Url.Action("Provider", "Dashboard") });
            default:
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Unauthorized(new { redirect = Url.Action("Login", "Account") });
        }

        switch (result.Outcome)
        {
            case BookingActionOutcome.Success:
                TempData["Flash"] = result.Message;
                TempData["FlashKind"] = "ok";
                return Ok(new
                {
                    message = result.Message,
                    redirect = Url.Action(nameof(Index), new { status = back == ProviderBookingTab.AllKey ? null : back, page = page > 1 ? page : (int?)null })
                });
            case BookingActionOutcome.LateReasonRequired:
                return BadRequest(new { message = result.Message, errors = new { lateReason = result.Message } });
            case BookingActionOutcome.TooEarly:
            case BookingActionOutcome.StartWindowExpired:
                return BadRequest(new { message = result.Message });
            case BookingActionOutcome.NotFound:
                return NotFound(new { message = result.Message });
            default:
                return Conflict(new { message = result.Message });
        }
    }

    /// fetch() asks for JSON (booking-start.js, booking-completion.js); a plain form post does not.
    private bool WantsJson() =>
        Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private IActionResult RedirectBack(string tab, int page) =>
        RedirectToAction(nameof(Index), new
        {
            status = tab == ProviderBookingTab.AllKey ? null : tab,
            page = page > 1 ? page : (int?)null
        });

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
