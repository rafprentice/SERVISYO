# Messages (customer and service provider) - notes

Page: `/messages` (both roles use the same page; the sidebar "Messages" link opens it).
Files: `Services/MessagingService.cs`, `Controllers/MessagesController.cs`, `ViewModels/MessagingViewModels.cs`,
`Views/Messages/Index.cshtml`, `wwwroot/js/messages.js`, `.sv-ms*` rules at the end of `portal.css`,
`tests/Servisyo.Tests/MessagingTests.cs`.

## What it does
- Left: every person you share a booking with (a customer sees providers, a provider sees customers), newest
  activity first, with the last message, its time, an unread badge, and a search box (filters by name / service).
- Right: the open conversation - header with name, a context line and a status pill, the message bubbles
  (theirs left/white, yours right/indigo), and a box to type in. Enter sends, Shift+Enter makes a new line.
- New messages show up by themselves within about 3 seconds, the unread badge in the sidebar follows, and a
  conversation you are looking at is marked read automatically.

## Rules (and where each is enforced)
| Rule | Enforced in |
|---|---|
| Only Active customers and Active + Approved providers can use the chat; administrators cannot | `MessagingService.FindAsync` (every call) + `[Authorize(Roles = ...)]` on the controller |
| You can only talk to the other side of a booking you share (customer of your booking / provider of your booking) | `IsCounterpartAsync` on send and mark-read; the conversation list is built only from your own bookings; poll for anybody else returns nothing |
| The sender is always the signed-in user; the browser only sends the words and (in the address) the receiver | `MessagesController` reads `ClaimTypes.NameIdentifier`; `SendMessageForm` has no sender field |
| A message is 1-1000 characters after trimming (database column is `NVARCHAR(1000)`) | `MessageRules.Normalize` + length check in `SendAsync` (also `maxlength` in the browser) |
| You cannot message a suspended / deactivated person; the old conversation stays readable | `SendAsync` partner check; `ConversationViewModel.CanSend` turns the composer off |
| Opening a conversation marks only what the OTHER person sent you as read | `MarkReadCoreAsync` (`ReceiverId == me && SenderId == partner`) |
| Messages are saved in UTC and shown in Philippine time | `ChatFormat` |

## The status pill
It describes the booking the two people share. If they have several, a live one (Pending / Accepted / In Progress)
is used first, otherwise the newest.

| Booking | Pill |
|---|---|
| Pending | Pending Negotiation (yellow) |
| Accepted, In Progress | Active Booking (green) |
| Completed | Completed (grey) |
| Rejected | Rejected (red) |
| Cancelled | Cancelled (grey) |

## How "live" works
`messages.js` asks `GET /messages/poll?with=<partner>&after=<last message number>` every 3 seconds while the tab is
visible (nothing while the tab is hidden; one immediate check when you come back). If three checks in a row fail
it slows to every 15 seconds. The answer contains the new messages of the open conversation, the refreshed list
and the unread total. Sending uses `fetch` (JSON answer); if JavaScript is off, the form still works as a normal
post with a redirect back.
Message text is always put on the page with `textContent` (never as HTML) and the bubbles use `white-space: pre-wrap`.

## Deliberately NOT built (and why)
- **Service Proposal card / "Accept Plan & Price" / "Send Counter Offer"** (in the mockup). The project document says
  *only the assigned provider can accept or reject a Pending booking*, but the mockup lets the customer accept
  and sets the booking to Accepted. It also needs new tables (a quotation with price, inclusions, validity) and ties
  into the open "quotation / payment" decision. It needs your decision before any code.
- **Attachments** (the paperclip is shown disabled). Needs file storage outside `wwwroot`, type/size limits and a
  new column or table - same design as the profile-photo question.
- **SignalR / WebSockets.** Polling is much simpler for a beginner project and needs no extra setup; only
  `messages.js` (and a small hub) would change if you want push later. The service layer stays the same.
- Typing indicators, "seen" ticks, deleting or editing messages, loading messages older than the newest 100,
  rate limiting. Add rate limiting before putting this on the internet.

## Known limits
- A customer can only start talking to a provider after a booking exists. Customer-side booking creation
  ("Book this Provider") is not built yet, so use the sample scripts to get conversations.
- A new message does not create a notification-bell entry; the unread count in the sidebar is the signal.
- Unread counts of people you no longer share a booking with (cannot happen through the app) still count in
  the sidebar total.

## Install
1. `Expand-Archive -Path "$env:USERPROFILE\Downloads\messages.zip" -DestinationPath . -Force`
2. In `Program.cs`, next to the other `AddScoped` lines, add:
   `builder.Services.AddScoped<IMessagingService, MessagingService>();`
3. `dotnet build` then `dotnet test`
4. Optional sample chat: set `@UserEmail` in `docs\dev-sample-messages-data.sql`, then
   `sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -I -i docs\dev-sample-messages-data.sql`
5. `dotnet run`, sign in as a customer and as a provider (two browsers / one private window) and chat between them.
