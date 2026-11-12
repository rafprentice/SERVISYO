using Servisyo.Web.Models;

namespace Servisyo.Web.Data;

/// Database v2.7: which days off keep customers from booking a provider.
public static class DayOffQueries
{
    /// A day blocks bookings when it has no request (the provider's own day from before v2.7, or an extra day an
    /// administrator gave) or when its request is APPROVED. A Pending request holds its dates - no other provider can
    /// ask for them - but customers can still book until an administrator approves it.
    /// The database checks the same in fn_IsProviderAvailable and usp_CreateBooking (DayOffRequestsSql).
    public static IQueryable<ProviderUnavailableDate> BlockingBookings(this IQueryable<ProviderUnavailableDate> days) =>
        days.Where(u => u.RequestId == null || u.Request!.Status == DayOffStatuses.Approved);
}
