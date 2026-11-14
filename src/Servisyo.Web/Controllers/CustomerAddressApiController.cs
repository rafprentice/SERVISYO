using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Controllers;

/// What PUT /api/profile/address receives: { "address": "Osmeña Blvd, Brgy. Capitol Site, Cebu City" }.
public record AddressUpdateRequest(string? Address);

/// The saved address, also split into its parts. The database keeps ONE column (CUSTOMERS.ADDRESS) with the full
/// text; street, barangay and city are worked out from the Cebu City option (CebuLocations), never stored twice.
public record SavedAddressResponse(string Message, string Address, string StreetAddress, string Barangay, string City);

/// Every refusal: { "message": "..." }.
public record AddressApiMessage(string Message);

/// The customer's saved address as JSON (used by the Saved Address card on "My Profile").
///   PUT    /api/profile/address  -> changes it to one of the 5 Cebu City options (200, or 400 with a message;
///                                    400 also while the customer has an active booking: CustomerProfileLock)
///   DELETE /api/profile/address  -> always 400: a saved address can be edited but never removed
/// The customer is always the signed-in user (the cookie), never an id sent by the browser.
[ApiController]
[Authorize(Roles = Roles.Customer)]
[Route(CustomerAddressApiController.RoutePath)]
public class CustomerAddressApiController : ControllerBase
{
    public const string RoutePath = "api/profile/address";
    public const string NotAllowedMessage = "Your account can't change its address right now. Please sign in again.";

    private readonly ICustomerProfileService _profile;

    public CustomerAddressApiController(ICustomerProfileService profile) => _profile = profile;

    /// Same rules as the form on the My Profile page (CustomerProfileService.SaveAddressAsync).
    /// The antiforgery token comes in the "RequestVerificationToken" header (customer-profile.js sends it).
    [HttpPut("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Put(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AddressUpdateRequest? request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return StatusCode(StatusCodes.Status403Forbidden, new AddressApiMessage(NotAllowedMessage));

        var result = await _profile.SaveAddressAsync(userId, new CustomerAddressForm { Address = request?.Address }, ct);
        if (!result.Allowed)
            return StatusCode(StatusCodes.Status403Forbidden, new AddressApiMessage(NotAllowedMessage));
        if (!result.Saved)
            return BadRequest(new AddressApiMessage(result.Message));

        var saved = CebuLocations.Find(result.Address)!;           // the service only saves one of the options
        return Ok(new SavedAddressResponse(result.Message, saved.Full, saved.Street, saved.Barangay, saved.City));
    }

    /// Removing a saved address is not allowed. Nothing is changed, so no antiforgery token is needed:
    /// the answer is always the same 400 message.
    [HttpDelete("")]
    [IgnoreAntiforgeryToken]
    public IActionResult Delete() => BadRequest(new AddressApiMessage(SavedAddressRules.CannotRemoveMessage));
}

/// Requests under /api/ get a status code instead of being sent to the login page (a script can't follow that).
public static class ApiPaths
{
    public static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
}
