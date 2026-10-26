using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// "Leave a Review" for a Completed booking. Customer accounts only; providers and admins cannot write reviews here.
[Authorize(Roles = Roles.Customer)]
[Route("customer/bookings/{bookingId:int}/review")]
public class CustomerReviewsController : Controller
{
    private readonly ICustomerReviewService _reviews;

    public CustomerReviewsController(ICustomerReviewService reviews) => _reviews = reviews;

    [HttpGet("")]
    public async Task<IActionResult> Create(int bookingId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        var result = await _reviews.GetPageAsync(userId, bookingId, ct);
        if (result.Outcome == ReviewEligibility.NotAllowed) return await EndSessionAsync();
        if (result.Outcome != ReviewEligibility.Ok) return BackToBookings(result.Message!, isError: result.Outcome != ReviewEligibility.AlreadyReviewed);

        return View(result.Page!);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int bookingId, [Bind(Prefix = "Form")] LeaveReviewForm form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Forbid();

        if (ModelState.IsValid)
        {
            var result = await _reviews.SubmitAsync(userId, bookingId, form, ct);
            switch (result.Outcome)
            {
                case SubmitReviewOutcome.NotAllowed:
                    return await EndSessionAsync();
                case SubmitReviewOutcome.Ok:
                    return BackToBookings(result.Message, isError: false, bookingId);
                case SubmitReviewOutcome.Invalid:
                    ModelState.AddModelError("Form.Rating", result.Message);
                    break;
                default:
                    return BackToBookings(result.Message, isError: result.Outcome != SubmitReviewOutcome.AlreadyReviewed);
            }
        }

        // Show the page again with what the customer typed and the messages.
        var page = await _reviews.GetPageAsync(userId, bookingId, ct);
        if (page.Outcome == ReviewEligibility.NotAllowed) return await EndSessionAsync();
        if (page.Outcome != ReviewEligibility.Ok) return BackToBookings(page.Message!, isError: true);
        page.Page!.Form = form;
        return View(page.Page);
    }

    private IActionResult BackToBookings(string message, bool isError, int? bookingId = null)
    {
        TempData["Flash"] = message;
        TempData["FlashKind"] = isError ? "error" : "ok";
        return bookingId is int id
            ? RedirectToAction("Index", "CustomerBookings", null, "booking-" + id)
            : RedirectToAction("Index", "CustomerBookings");
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    // The account was suspended / deactivated after signing in: end the session.
    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
