using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Servisyo.Web.Hubs;

/// Live booking updates (SignalR). The browser only LISTENS here: it cannot send anything that changes data.
/// Messages are addressed to a user id taken from the sign-in cookie (ClaimTypes.NameIdentifier),
/// so a page only ever hears about that user's own bookings.
[Authorize]
public class BookingHub : Hub
{
    public const string Path = "/hubs/bookings";
    public const string BookingChangedEvent = "bookingChanged";
}
