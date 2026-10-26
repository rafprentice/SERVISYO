using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "Book this Provider": a customer asks one provider for one or more of their services at a date and time.
/// Customer accounts only. The customer is always the signed-in user; the provider comes from the address and is
/// checked again on the server; prices and durations are looked up, never read from the form.
[Authorize(Roles = Roles.Customer)]
[Route("customer/book/{providerId:int}")]
public class BookProviderController : Controller
{
    private readonly IBookProviderService _booking;

    public BookProviderController(IBookProviderService booking) => _booking = booking;

    [HttpGet("")]
    public async Task<IActionResult> Index(int providerId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _booking.GetPageAsync(userId, providerId, ct);
        switch (result.Outcome)
        {
            case BookPageOutcome.NotAllowed:
                return await EndSessionAsync();
            case BookPageOutcome.ProviderUnavailable:
                Response.StatusCode = StatusCodes.Status404NotFound;
                return View("Unavailable", result.Shell);
            default:
                return View(result.Page!);
        }
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int providerId, BookProviderForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _booking.CreateAsync(userId, providerId, form, ct);

        switch (result.Outcome)
        {
            case BookOutcome.NotAllowed:
                return await EndSessionAsync();

            case BookOutcome.ProviderUnavailable:
                TempData["Flash"] = result.Message;
                TempData["FlashKind"] = "error";
                return RedirectToAction("Index", "FindServices");

            case BookOutcome.Created:
                // reload as a normal page so a refresh does not send the request twice
                TempData["Flash"] = $"Booking #{result.BookingId} was sent to {result.ProviderName}. You'll be notified when they accept or reject it.";
                TempData["FlashKind"] = "ok";
                return RedirectToAction("Index", "CustomerBookings");
        }

        // something to fix: show the form again with what the customer typed and the problems
        var again = await _booking.GetPageAsync(userId, providerId, ct);
        if (again.Outcome == BookPageOutcome.NotAllowed) return await EndSessionAsync();
        if (again.Outcome == BookPageOutcome.ProviderUnavailable)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("Unavailable", again.Shell);
        }

        var page = again.Page!;
        page.Form = form;
        page.FieldErrors = result.FieldErrors;
        page.FormError = result.FieldErrors.Count == 0 ? result.Message : null;
        return View(nameof(Index), page);
    }

    /// The free start times for a date, asked by the page's script as the customer picks services and a date.
    [HttpGet("slots")]
    public async Task<IActionResult> Slots(int providerId, DateOnly? date, [FromQuery] int[]? services, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (date is null)
            return Json(new { ok = true, message = "Choose a date.", slots = Array.Empty<object>() });

        var result = await _booking.GetSlotsAsync(userId, providerId, date.Value, services ?? Array.Empty<int>(), ct);
        if (!result.Allowed)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Unauthorized();
        }

        Response.Headers.CacheControl = "no-store";
        return Json(new { ok = result.ProviderAvailable, message = result.Message, slots = result.Slots });
    }

    // ------------------------------------------------------------------ helpers

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
