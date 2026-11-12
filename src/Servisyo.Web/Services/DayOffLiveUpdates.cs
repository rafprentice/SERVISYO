using Microsoft.AspNetCore.SignalR;
using Servisyo.Web.Hubs;

namespace Servisyo.Web.Services;

/// Database v2.7: pushes "the days off of provider X changed" to the open pages of every administrator and of that
/// provider (SignalR, the same connection as the live bookings). The message carries no personal details: the page
/// asks the server for the fresh Days Off list itself, with all the normal checks.
public interface IDayOffLiveUpdates
{
    /// what: "requested", "cancelled", "removed", "approved", "rejected" or "extra-day". Never throws: the change is
    /// already saved, and a page that misses the push shows it on its next refresh.
    Task DayOffChangedAsync(int providerId, int providerUserId, string what);
}

public class DayOffLiveUpdates : IDayOffLiveUpdates
{
    private readonly IHubContext<BookingHub> _hub;
    private readonly ILogger<DayOffLiveUpdates> _logger;

    public DayOffLiveUpdates(IHubContext<BookingHub> hub, ILogger<DayOffLiveUpdates> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task DayOffChangedAsync(int providerId, int providerUserId, string what)
    {
        var payload = new { providerId, what };
        try
        {
            await _hub.Clients.Group(BookingHub.AdminsGroup).SendAsync(BookingHub.DayOffChangedEvent, payload);
            if (providerUserId > 0)
                await _hub.Clients.User(providerUserId.ToString()).SendAsync(BookingHub.DayOffChangedEvent, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not push the day off change ({What}) of provider {ProviderId}.", what, providerId);
        }
    }
}
