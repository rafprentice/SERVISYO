using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IMessagingService
{
    /// The Messages page: the conversation list plus the open conversation. Opening a conversation marks what
    /// the other person sent as read. Customers and approved providers only.
    Task<MessagesPageResult> GetPageAsync(int userId, int? withUserId, CancellationToken ct = default);

    /// Send one message to a person the user shares a booking with.
    Task<SendMessageResult> SendAsync(int userId, int partnerUserId, string? text, CancellationToken ct = default);

    /// What changed since the page last asked: messages of the open conversation newer than <paramref name="afterMessageId"/>,
    /// the refreshed conversation list, and the total number of unread messages.
    Task<PollResult> PollAsync(int userId, int? withUserId, int afterMessageId, CancellationToken ct = default);

    /// Mark everything the partner sent to this user as read.
    Task<MarkReadResult> MarkReadAsync(int userId, int partnerUserId, CancellationToken ct = default);
}

public class MessagingService : IMessagingService
{
    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<MessagingService> _logger;

    public MessagingService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<MessagingService>? logger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MessagingService>.Instance;
    }

    private DateTime NowUtc => _clock.GetUtcNow().UtcDateTime;

    /// The signed-in person: a customer (Active) or a service provider (Active and Approved).
    private sealed record Me(int UserId, bool IsProvider, int ProfileId, string FirstName, string LastName, string? BusinessName);

    /// Found from the SIGNED-IN user id only (never from the browser).
    private async Task<(ProviderDashboardAccess Access, Me? Me)> FindAsync(int userId, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Role, x.AccountStatus, x.FirstName, x.LastName })
            .FirstOrDefaultAsync(ct);
        if (u is null || u.AccountStatus != AccountStatuses.Active) return (ProviderDashboardAccess.NotAllowed, null);

        if (u.Role == Roles.Customer)
        {
            var customerId = await _db.Customers.AsNoTracking()
                .Where(c => c.UserId == userId)
                .Select(c => (int?)c.CustomerId)
                .FirstOrDefaultAsync(ct);
            return customerId is null
                ? (ProviderDashboardAccess.NotAllowed, null)
                : (ProviderDashboardAccess.Allowed, new Me(userId, false, customerId.Value, u.FirstName, u.LastName, null));
        }

        if (u.Role == Roles.ServiceProvider)
        {
            var p = await _db.Providers.AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => new { x.ProviderId, x.ApprovalStatus, x.BusinessName })
                .FirstOrDefaultAsync(ct);
            if (p is null) return (ProviderDashboardAccess.NotAllowed, null);
            if (p.ApprovalStatus == ApprovalStatuses.Pending) return (ProviderDashboardAccess.Pending, null);
            if (p.ApprovalStatus != ApprovalStatuses.Approved) return (ProviderDashboardAccess.Rejected, null);
            return (ProviderDashboardAccess.Allowed, new Me(userId, true, p.ProviderId, u.FirstName, u.LastName, p.BusinessName));
        }

        return (ProviderDashboardAccess.NotAllowed, null);       // administrators do not use the chat
    }

    /// People can only talk to the other side of a booking they share: a customer with the provider of one of their
    /// bookings, a provider with the customer of one of theirs. Nobody else can be messaged or read.
    private Task<bool> IsCounterpartAsync(Me me, int partnerUserId, CancellationToken ct) =>
        me.IsProvider
            ? _db.Bookings.AsNoTracking().AnyAsync(b => b.ProviderId == me.ProfileId && b.Customer.UserId == partnerUserId, ct)
            : _db.Bookings.AsNoTracking().AnyAsync(b => b.CustomerId == me.ProfileId && b.Provider.UserId == partnerUserId, ct);

    private sealed record BookingLink(int PartnerUserId, int BookingId, string Status, DateTime CreatedAt);

    // ------------------------------------------------------------------ conversations

    /// One entry per person the user shares a booking with, newest activity first.
    private async Task<List<ConversationViewModel>> BuildConversationsAsync(Me me, DateTime nowUtc, CancellationToken ct)
    {
        var myId = me.UserId;

        // 1. the other side of every booking
        var links = me.IsProvider
            ? await _db.Bookings.AsNoTracking()
                .Where(b => b.ProviderId == me.ProfileId)
                .Select(b => new BookingLink(b.Customer.UserId, b.BookingId, b.Status, b.CreatedAt))
                .ToListAsync(ct)
            : await _db.Bookings.AsNoTracking()
                .Where(b => b.CustomerId == me.ProfileId)
                .Select(b => new BookingLink(b.Provider.UserId, b.BookingId, b.Status, b.CreatedAt))
                .ToListAsync(ct);
        if (links.Count == 0) return new List<ConversationViewModel>();

        // the booking that describes the conversation: a live one if there is one, otherwise the newest
        var chosen = links.GroupBy(l => l.PartnerUserId)
            .Select(g => g.OrderByDescending(l => ChatStatus.IsLive(l.Status))
                          .ThenByDescending(l => l.CreatedAt)
                          .ThenByDescending(l => l.BookingId)
                          .First())
            .ToList();
        var partnerIds = chosen.Select(l => l.PartnerUserId).ToList();
        var bookingIds = chosen.Select(l => l.BookingId).ToList();

        // 2. who they are
        var people = (await _db.Users.AsNoTracking()
                .Where(u => partnerIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.FirstName, u.LastName, u.AccountStatus })
                .ToListAsync(ct))
            .ToDictionary(u => u.UserId);

        // 3. last message and unread count per person (messages with anyone else are never loaded)
        var groups = await _db.Messages.AsNoTracking()
            .Where(m => (m.SenderId == myId && partnerIds.Contains(m.ReceiverId))
                     || (m.ReceiverId == myId && partnerIds.Contains(m.SenderId)))
            .GroupBy(m => m.SenderId == myId ? m.ReceiverId : m.SenderId)
            .Select(g => new
            {
                PartnerId = g.Key,
                LastId = g.Max(m => m.MessageId),
                Unread = g.Count(m => m.ReceiverId == myId && !m.IsRead)
            })
            .ToListAsync(ct);
        var lastIds = groups.Select(g => g.LastId).ToList();
        var lastMessages = (await _db.Messages.AsNoTracking()
                .Where(m => lastIds.Contains(m.MessageId))
                .Select(m => new { m.MessageId, m.MessageText, m.SentAt })
                .ToListAsync(ct))
            .ToDictionary(m => m.MessageId);
        var byPartner = groups.ToDictionary(g => g.PartnerId);

        // 4. what the booking is about
        var titles = await _db.GetBookingTitlesAsync(bookingIds, ct);
        var categoryRows = await _db.BookingServices.AsNoTracking()
            .Where(bs => bookingIds.Contains(bs.BookingId))
            .OrderBy(bs => bs.BookingServiceId)
            .Select(bs => new { bs.BookingId, Name = bs.Service.Category.CategoryName })
            .ToListAsync(ct);
        var categories = categoryRows.GroupBy(x => x.BookingId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct()));

        var result = new List<ConversationViewModel>();
        foreach (var link in chosen)
        {
            if (!people.TryGetValue(link.PartnerUserId, out var person)) continue;

            var name = $"{person.FirstName} {person.LastName}".Trim();
            var title = titles.GetValueOrDefault(link.BookingId);
            var category = categories.GetValueOrDefault(link.BookingId);
            var (statusText, statusTone) = ChatStatus.For(link.Status);

            var vm = new ConversationViewModel
            {
                PartnerUserId = link.PartnerUserId,
                Name = name,
                Initials = NameFormat.Initials(name),
                // a customer sees the provider's trade; a provider sees what the customer booked
                ContextText = me.IsProvider ? title ?? category : category ?? title,
                StatusText = statusText,
                StatusTone = statusTone,
                CanSend = person.AccountStatus == AccountStatuses.Active,
                SortKeyUtc = link.CreatedAt
            };

            if (byPartner.TryGetValue(link.PartnerUserId, out var g) && lastMessages.TryGetValue(g.LastId, out var last))
            {
                vm.Preview = MessageRules.Preview(last.MessageText);
                vm.TimeText = ChatFormat.ListTime(last.SentAt, nowUtc);
                vm.SortKeyUtc = last.SentAt;
                vm.Unread = g.Unread;
            }
            else
            {
                vm.Preview = "No messages yet";
            }

            result.Add(vm);
        }

        return result.OrderByDescending(c => c.SortKeyUtc).ThenBy(c => c.PartnerUserId).ToList();
    }

    private async Task<List<ChatMessageViewModel>> LoadThreadAsync(Me me, int partnerId, int afterId, DateTime nowUtc, CancellationToken ct)
    {
        var myId = me.UserId;

        // only messages between these two people, newest first so the limit keeps the recent ones
        var rows = await _db.Messages.AsNoTracking()
            .Where(m => m.MessageId > afterId
                     && ((m.SenderId == myId && m.ReceiverId == partnerId)
                      || (m.SenderId == partnerId && m.ReceiverId == myId)))
            .OrderByDescending(m => m.MessageId)
            .Take(MessageRules.ThreadLimit)
            .Select(m => new { m.MessageId, m.SenderId, m.MessageText, m.SentAt })
            .ToListAsync(ct);

        rows.Reverse();      // oldest first, the way a chat reads
        return rows.Select(m => new ChatMessageViewModel
        {
            Id = m.MessageId,
            Mine = m.SenderId == myId,
            Text = m.MessageText,
            TimeText = ChatFormat.BubbleTime(m.SentAt, nowUtc)
        }).ToList();
    }

    private async Task<int> UnreadTotalAsync(int userId, CancellationToken ct) =>
        await _db.Messages.AsNoTracking().CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct);

    private async Task MarkReadCoreAsync(int myId, int partnerId, CancellationToken ct)
    {
        var unread = await _db.Messages
            .Where(m => m.ReceiverId == myId && m.SenderId == partnerId && !m.IsRead)
            .ToListAsync(ct);
        if (unread.Count == 0) return;

        foreach (var m in unread) m.IsRead = true;
        await _db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ the page

    public async Task<MessagesPageResult> GetPageAsync(int userId, int? withUserId, CancellationToken ct = default)
    {
        var (access, me) = await FindAsync(userId, ct);
        if (me is null) return new(access, null);

        var nowUtc = NowUtc;
        var conversations = await BuildConversationsAsync(me, nowUtc, ct);

        // a person that is not one of my conversations is ignored: the first conversation opens instead
        var selected = conversations.FirstOrDefault(c => c.PartnerUserId == withUserId) ?? conversations.FirstOrDefault();

        var messages = new List<ChatMessageViewModel>();
        if (selected is not null)
        {
            // opening a conversation means reading it
            await MarkReadCoreAsync(me.UserId, selected.PartnerUserId, ct);
            selected.Unread = 0;
            messages = await LoadThreadAsync(me, selected.PartnerUserId, 0, nowUtc, ct);
        }

        var fullName = $"{me.FirstName} {me.LastName}".Trim();
        var vm = new MessagesPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = me.IsProvider ? "Provider" : "Customer",
                Subtitle = me.IsProvider ? me.BusinessName ?? string.Empty : "Customer Account",
                FirstName = me.FirstName,
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await UnreadTotalAsync(userId, ct)
            },
            Conversations = conversations,
            Selected = selected,
            Messages = messages,
            LastMessageId = messages.Count == 0 ? 0 : messages.Max(m => m.Id)
        };

        return new(ProviderDashboardAccess.Allowed, vm);
    }

    // ------------------------------------------------------------------ polling and reading

    public async Task<PollResult> PollAsync(int userId, int? withUserId, int afterMessageId, CancellationToken ct = default)
    {
        var (access, me) = await FindAsync(userId, ct);
        if (me is null) return new(access, Array.Empty<ChatMessageViewModel>(), Array.Empty<ConversationViewModel>(), 0);

        var nowUtc = NowUtc;
        var conversations = await BuildConversationsAsync(me, nowUtc, ct);

        // only a person I share a booking with can be asked about; anybody else just gets an empty answer
        var messages = withUserId is { } partner && conversations.Any(c => c.PartnerUserId == partner)
            ? await LoadThreadAsync(me, partner, Math.Max(0, afterMessageId), nowUtc, ct)
            : new List<ChatMessageViewModel>();

        return new(ProviderDashboardAccess.Allowed, messages, conversations, await UnreadTotalAsync(userId, ct));
    }

    public async Task<MarkReadResult> MarkReadAsync(int userId, int partnerUserId, CancellationToken ct = default)
    {
        var (access, me) = await FindAsync(userId, ct);
        if (me is null) return new(access, 0);

        if (await IsCounterpartAsync(me, partnerUserId, ct))
            await MarkReadCoreAsync(me.UserId, partnerUserId, ct);

        return new(ProviderDashboardAccess.Allowed, await UnreadTotalAsync(userId, ct));
    }

    // ------------------------------------------------------------------ sending

    public async Task<SendMessageResult> SendAsync(int userId, int partnerUserId, string? text, CancellationToken ct = default)
    {
        // 1. who is sending: from the sign-in cookie; only an Active customer or an Active + Approved provider
        var (access, me) = await FindAsync(userId, ct);
        if (me is null) return new(access, SendOutcome.NotAllowed, string.Empty, null);

        // 2. the words
        var cleaned = MessageRules.Normalize(text);
        if (cleaned is null)
            return new(access, SendOutcome.Invalid, "Type a message first.", null);
        if (cleaned.Length > MessageRules.MaxLength)
            return new(access, SendOutcome.Invalid, $"Messages can be at most {MessageRules.MaxLength:N0} characters.", null);

        // 3. the receiver must be the other side of a booking this user shares; a stranger and a missing user look the same
        if (!await IsCounterpartAsync(me, partnerUserId, ct))
        {
            _logger.LogWarning("User {UserId} tried to message user {PartnerId}, whom they share no booking with.", userId, partnerUserId);
            return new(access, SendOutcome.NotAllowed, "You can only message people you have a booking with.", null);
        }

        var partnerActive = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.UserId == partnerUserId && u.AccountStatus == AccountStatuses.Active, ct);
        if (!partnerActive)
            return new(access, SendOutcome.NotAllowed, "This account can't receive messages right now.", null);

        // 4. save it as unread for the receiver
        var now = NowUtc;
        var message = new Message
        {
            SenderId = userId,
            ReceiverId = partnerUserId,
            MessageText = cleaned,
            IsRead = false,
            SentAt = now
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} sent message {MessageId} to user {PartnerId}.", userId, message.MessageId, partnerUserId);
        return new(access, SendOutcome.Sent, string.Empty, new ChatMessageViewModel
        {
            Id = message.MessageId,
            Mine = true,
            Text = cleaned,
            TimeText = ChatFormat.BubbleTime(now, now)
        });
    }
}
