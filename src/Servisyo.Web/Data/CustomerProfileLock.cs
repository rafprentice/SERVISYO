using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Models;

namespace Servisyo.Web.Data;

/// The customer profile lock: while a customer has a booking that is not finished, their personal information
/// (name, email, phone) and saved address cannot be changed, so the provider keeps the details they were booked with.
/// "Not finished" = any status except Completed, Cancelled and Rejected: Pending (a request, or a quotation waiting to
/// be approved), Accepted (paid / scheduled) and In Progress. The password can always be changed.
/// ONE definition, used by the My Profile page and by every save (the form posts and PUT /api/profile/address).
public static class CustomerProfileLock
{
    /// The finished statuses: a booking in one of these never locks the profile.
    public static readonly string[] TerminalStatuses = { BookingStatuses.Completed, BookingStatuses.Cancelled, BookingStatuses.Rejected };

    /// What the server answers when a save is attempted while locked.
    public const string SaveRefusedMessage = "Profile information and saved address cannot be modified while you have an active booking.";

    /// The banner on My Profile while locked.
    public const string NoticeText = "Profile editing is currently locked because you have an active service booking. " +
                                     "Please wait until your booking is completed before updating your personal details or saved address.";

    /// The customer's bookings that are not finished.
    public static IQueryable<Booking> ActiveForCustomer(this IQueryable<Booking> bookings, int customerId) =>
        bookings.Where(b => b.CustomerId == customerId && !TerminalStatuses.Contains(b.Status));

    /// True when the customer has at least one booking that is not finished (read only, nothing tracked).
    public static Task<bool> CustomerHasActiveBookingAsync(this ServisyoDbContext db, int customerId, CancellationToken ct = default) =>
        db.Bookings.AsNoTracking().ActiveForCustomer(customerId).AnyAsync(ct);

    /// How many bookings that are not finished the customer has.
    public static Task<int> CountCustomerActiveBookingsAsync(this ServisyoDbContext db, int customerId, CancellationToken ct = default) =>
        db.Bookings.AsNoTracking().ActiveForCustomer(customerId).CountAsync(ct);
}
