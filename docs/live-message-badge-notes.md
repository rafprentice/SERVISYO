# Live unread-message badge

- No database change: MESSAGES.IS_READ already exists (PDF) and is set to 1 when a conversation is opened.
- Unread total per user = messages where ReceiverId = me AND IsRead = 0. Per conversation the list already shows its own count.
- On send (`MessagingService.SendAsync`): SignalR `ReceiveMessage` { fromUserId, unreadTotal } to the receiver only.
- On read (`MarkReadAsync`): `MessagesRead` { unreadTotal } to the reader (their other tabs).
- `live-bookings.js` sets every `[data-sv-msg-badge]` (sidebar "Messages"), hides it at 0, and tells the Messages page
  to fetch at once (`sv:message-received`), so an open chat shows the new message immediately and marks it read.
- If SignalR is unavailable, the Messages page's normal polling still updates everything.
