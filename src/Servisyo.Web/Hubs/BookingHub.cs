using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Servisyo.Web.Models;

namespace Servisyo.Web.Hubs;

/// Live booking updates (SignalR). The browser only LISTENS here: it cannot send anything that changes data.
/// Messages are addressed to a user id taken from the sign-in cookie (ClaimTypes.NameIdentifier),
/// so a page only ever hears about that user's own bookings.
[Authorize]
public class BookingHub : Hub
{
    public const string Path = "/hubs/bookings";
    public const string BookingChangedEvent = "bookingChanged";

    /// A new chat message arrived for this user: { fromUserId, unreadTotal }.
    public const string ReceiveMessageEvent = "ReceiveMessage";

    /// This user read messages (maybe in another tab): { unreadTotal }.
    public const string MessagesReadEvent = "MessagesRead";

    /// Database v2.7: a provider's days off changed (a request was sent, cancelled, approved or rejected, or an
    /// extra day was given): { providerId, what }. Sent to every administrator and to that provider.
    public const string DayOffChangedEvent = "dayOffChanged";

    /// Every signed-in ACTIVE administrator's open pages. The group comes from the sign-in cookie's role,
    /// never from the browser (a browser cannot join a group itself: the hub has no methods for that).
    public const string AdminsGroup = "admins";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(Roles.Administrator) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        await base.OnConnectedAsync();
    }
}
