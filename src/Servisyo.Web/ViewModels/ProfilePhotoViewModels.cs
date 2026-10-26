using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// The avatar block on a profile page: a photo when there is one, otherwise the initials.
public sealed record ProfilePhotoViewModel(string Initials, string? PhotoUrl, string AvatarClass);

/// Bookings that are still "open" (Requested, Quotation, Paid/Scheduled, In Progress).
/// While a user has one of these, their name (and a provider's business name) cannot be changed,
/// so the other side always sees the same name they booked with.
public static class ActiveBookingRules
{
    public static readonly string[] Statuses = { BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress };
    public const string NameLockedMessage = "Name editing is locked while you have active bookings.";
}
