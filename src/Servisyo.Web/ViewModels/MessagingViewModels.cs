using System.Globalization;
using Servisyo.Web.Models;

namespace Servisyo.Web.ViewModels;

/// The rules for what a chat message may contain.
public static class MessageRules
{
    public const int MaxLength = 1000;        // MESSAGES.MESSAGE_TEXT is NVARCHAR(1000)
    public const int PreviewLength = 40;      // characters of the last message shown in the conversation list
    public const int ThreadLimit = 100;       // newest messages loaded for the open conversation

    /// What is actually saved: no spaces around it, browser line breaks (\r\n) turned into one "\n".
    /// Returns null when nothing is left (an empty or blank message is never sent).
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return t.Length == 0 ? null : t;
    }

    /// The one-line version shown in the list: line breaks become spaces, long text is cut with "…".
    public static string Preview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var oneLine = string.Join(' ', text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return oneLine.Length <= PreviewLength ? oneLine : oneLine[..(PreviewLength - 1)].TrimEnd() + "…";
    }
}

/// How times are written in the chat. Everything is stored in UTC and shown in Philippine time.
public static class ChatFormat
{
    private const int ManilaOffsetHours = 8;      // the Philippines has no daylight saving

    private static DateTime Local(DateTime utc) => utc.AddHours(ManilaOffsetHours);

    /// Conversation list: today "10:42 AM", yesterday "Yesterday", the last week "Mon", older "Sep 3".
    public static string ListTime(DateTime sentUtc, DateTime nowUtc)
    {
        var sent = Local(sentUtc);
        var days = (Local(nowUtc).Date - sent.Date).Days;
        if (days <= 0) return sent.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (days == 1) return "Yesterday";
        if (days < 7) return sent.ToString("ddd", CultureInfo.InvariantCulture);
        return sent.ToString("MMM d", CultureInfo.InvariantCulture);
    }

    /// Under a chat bubble: today "10:20 AM", another day "Sep 3 · 10:20 AM".
    public static string BubbleTime(DateTime sentUtc, DateTime nowUtc)
    {
        var sent = Local(sentUtc);
        return sent.Date == Local(nowUtc).Date
            ? sent.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : sent.ToString("MMM d", CultureInfo.InvariantCulture) + " · " + sent.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }
}

/// The pill next to the name in the chat header: it describes the booking the two people share.
public static class ChatStatus
{
    public static (string Text, string Tone) For(string bookingStatus) => bookingStatus switch
    {
        BookingStatuses.Pending => ("Pending Negotiation", "pending"),
        BookingStatuses.Accepted or BookingStatuses.InProgress => ("Active Booking", "completed"),
        BookingStatuses.Completed => ("Completed", "muted"),
        BookingStatuses.Rejected => ("Rejected", "danger"),
        _ => ("Cancelled", "muted")
    };

    /// Pending, Accepted and In Progress bookings are "live"; the conversation shows the live one first.
    public static bool IsLive(string bookingStatus) =>
        bookingStatus is BookingStatuses.Pending or BookingStatuses.Accepted or BookingStatuses.InProgress;
}

/// One row of the conversation list (and one entry of the JSON the page asks for every few seconds).
public class ConversationViewModel
{
    public int PartnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;

    /// The line under the name: the provider's category for a customer ("Plumbing"), the booked service for a provider
    /// ("Pipe Repair & Waterproofing").
    public string? ContextText { get; set; }

    public string StatusText { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "muted";

    public string Preview { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public DateTime SortKeyUtc { get; set; }
    public int Unread { get; set; }

    /// False when the other person's account is suspended or deactivated: the history stays readable, sending is off.
    public bool CanSend { get; set; } = true;

    public string SearchText => $"{Name} {ContextText}".ToLowerInvariant();
}

public class ChatMessageViewModel
{
    public int Id { get; set; }
    public bool Mine { get; set; }
    public string Text { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
}

public class MessagesPageViewModel
{
    public PortalShellViewModel Shell { get; set; } = new();
    public IReadOnlyList<ConversationViewModel> Conversations { get; set; } = Array.Empty<ConversationViewModel>();

    /// The conversation shown on the right (null when the person has no conversations yet).
    public ConversationViewModel? Selected { get; set; }
    public IReadOnlyList<ChatMessageViewModel> Messages { get; set; } = Array.Empty<ChatMessageViewModel>();

    /// The newest message number on screen; the page asks the server for anything after it.
    public int LastMessageId { get; set; }

    public string? FlashMessage { get; set; }
    public bool FlashIsError { get; set; }

    public string PartnerNoun => Shell.Role == "Provider" ? "customer" : "provider";
}

public record MessagesPageResult(ProviderDashboardAccess Access, MessagesPageViewModel? Page);

/// What the page asks for every few seconds: the new messages of the open conversation, the refreshed list, and the unread total.
public record PollResult(ProviderDashboardAccess Access, IReadOnlyList<ChatMessageViewModel> Messages,
    IReadOnlyList<ConversationViewModel> Conversations, int UnreadTotal);

public record MarkReadResult(ProviderDashboardAccess Access, int UnreadTotal);

public enum SendOutcome
{
    Sent,
    Invalid,       // empty, or longer than the limit
    NotAllowed     // not a person this user may message (no booking together, or the account is unavailable)
}

public record SendMessageResult(ProviderDashboardAccess Access, SendOutcome Outcome, string Message, ChatMessageViewModel? Sent)
{
    public bool Succeeded => Outcome == SendOutcome.Sent;
}

/// What the browser posts with the Send button. The receiver is in the address and the sender always
/// comes from the sign-in cookie; the browser only supplies the words.
public class SendMessageForm
{
    public string? Text { get; set; }
}
