using System.Globalization;
using Servisyo.Web.Models;
using Servisyo.Web.Services;

namespace Servisyo.Web.ViewModels;

/// Database v2.11: the proof a provider gave when completing a job (Views/Shared/_CompletionProof.cshtml).
/// Shown on the customer's My Bookings card, in Admin > Bookings > View, and on the provider's own card.
public sealed class CompletionProofViewModel
{
    private const int ManilaOffsetHours = 8;      // the Philippines has no daylight saving

    public string? Message { get; init; }
    public string? PhotoUrl { get; init; }        // only an address inside /uploads/completions/ (CompletionPhotoStore.IsOurs)
    public string? CompletedText { get; init; }   // "Nov 14, 2026 3:05 PM" (Cebu time)

    /// Null when the booking is not Completed, or was completed before proof was required (no message, no photo).
    public static CompletionProofViewModel? From(string status, string? message, string? photoUrl, DateTime? completedAtUtc)
    {
        if (status != BookingStatuses.Completed) return null;
        var photo = CompletionPhotoStore.IsOurs(photoUrl) ? photoUrl : null;
        var text = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (photo is null && text is null) return null;
        return new CompletionProofViewModel
        {
            Message = text,
            PhotoUrl = photo,
            CompletedText = completedAtUtc?.AddHours(ManilaOffsetHours).ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture)
        };
    }
}
