using Microsoft.AspNetCore.SignalR;
using Servisyo.Web.Hubs;

namespace Servisyo.Web.Services;

/// What is pushed to the browsers of the customer and the provider of a booking after it changed.
/// It carries no personal details: the page asks the server for the fresh content itself (with all the normal checks).
public sealed record BookingChange(int BookingId, string Status, string PaymentStatus, string What);

public interface IBookingLiveUpdates
{
    /// Tells the customer and the provider (their open pages) that a booking changed. Never throws:
    /// the change is already saved, and a page that misses the push still shows it on the next load.
    Task BookingChangedAsync(BookingChange change, params int[] userIds);
}

public class BookingLiveUpdates : IBookingLiveUpdates
{
    private readonly IHubContext<BookingHub> _hub;
    private readonly ILogger<BookingLiveUpdates> _logger;

    public BookingLiveUpdates(IHubContext<BookingHub> hub, ILogger<BookingLiveUpdates> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task BookingChangedAsync(BookingChange change, params int[] userIds)
    {
        var ids = userIds.Where(id => id > 0).Distinct().Select(id => id.ToString()).ToList();
        if (ids.Count == 0) return;

        try
        {
            await _hub.Clients.Users(ids).SendAsync(BookingHub.BookingChangedEvent, change);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not push the live update for booking {BookingId}.", change.BookingId);
        }
    }
}
