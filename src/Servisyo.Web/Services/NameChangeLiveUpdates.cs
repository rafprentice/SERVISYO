using Microsoft.AspNetCore.SignalR;
using Servisyo.Web.Hubs;

namespace Servisyo.Web.Services;

/// Database v2.8: pushes "the name change request of provider X changed" to the open pages of every administrator
/// (dashboard, Name Changes, the bell) and of that provider (My Profile, the bell). Same connection as the live
/// bookings. The message carries no personal details: each page asks the server for the fresh data itself.
public interface INameChangeLiveUpdates
{
    /// what: "requested", "cancelled", "approved" or "rejected". Never throws: the change is already saved, and a
    /// page that misses the push shows it on its next refresh.
    Task NameChangeChangedAsync(int providerId, int providerUserId, string what);
}

public class NameChangeLiveUpdates : INameChangeLiveUpdates
{
    private readonly IHubContext<BookingHub> _hub;
    private readonly ILogger<NameChangeLiveUpdates> _logger;

    public NameChangeLiveUpdates(IHubContext<BookingHub> hub, ILogger<NameChangeLiveUpdates> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NameChangeChangedAsync(int providerId, int providerUserId, string what)
    {
        var payload = new { providerId, what };
        try
        {
            await _hub.Clients.Group(BookingHub.AdminsGroup).SendAsync(BookingHub.NameChangeChangedEvent, payload);
            if (providerUserId > 0)
                await _hub.Clients.User(providerUserId.ToString()).SendAsync(BookingHub.NameChangeChangedEvent, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not push the name change ({What}) of provider {ProviderId}.", what, providerId);
        }
    }
}
