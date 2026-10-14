using Microsoft.EntityFrameworkCore;

namespace Servisyo.Web.Data;

public static class BookingQueryExtensions
{
    /// One display title per booking: its services joined with " & ", e.g. "Pipe Repair & Waterproofing".
    public static async Task<Dictionary<int, string>> GetBookingTitlesAsync(
        this ServisyoDbContext db, IReadOnlyCollection<int> bookingIds, CancellationToken ct = default)
    {
        if (bookingIds.Count == 0) return new Dictionary<int, string>();

        var ids = bookingIds.ToList();
        var rows = await db.BookingServices.AsNoTracking()
            .Where(bs => ids.Contains(bs.BookingId))
            .OrderBy(bs => bs.BookingServiceId)
            .Select(bs => new { bs.BookingId, bs.Service.ServiceName })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.BookingId)
                   .ToDictionary(g => g.Key, g => string.Join(" & ", g.Select(x => x.ServiceName)));
    }
}
