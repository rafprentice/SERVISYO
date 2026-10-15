# Notification bell (customer, provider, administrator)

## What it does
- The bell in the top bar is now a button. A red number shows how many notifications are unread (99+ above 99; hidden at 0).
- Clicking it opens a panel with the 8 newest notifications: icon, title, message, time ("10 mins ago", "Yesterday"), blue dot when unread,
  "Mark all as read", and "View all notifications".
- Clicking a notification marks it read and opens the page it is about (bookings page for booking news, the dashboard otherwise;
  admins go to Providers > Pending for a new application).
- "View all notifications" opens /notifications (paged, 15 per page).
- The number refreshes every 30 seconds while the tab is visible. Esc or a click outside closes the panel.

## New notification: provider applications -> administrators
When a provider registers, every Active administrator receives "New provider application from <business> (<name>)."
(a failure here is only logged; the registration itself is never affected).

## Rules
- Only the signed-in person's own notifications are ever shown or changed; ids from the browser are checked against the owner.
- Suspended / deactivated accounts get nothing (the session is ended).
- Marking read is the only change; nothing is deleted.

## Limits / choices
- The NOTIFICATIONS table has only a message (no type, title or link), so the title/icon/target are worked out from the wording
  (`NotificationKinds`). Unknown wording shows as a general "Notification" that opens the dashboard. No schema change was made.
- New chat messages do not create bell notifications: they keep their own number on the Messages link.
- Pages that do not use the portal layout (home page, "pending approval" page) have no bell.

## Install
1. `Expand-Archive -Path "$env:USERPROFILE\Downloads\notifications.zip" -DestinationPath . -Force`
2. In Program.cs: `builder.Services.AddScoped<INotificationService, NotificationService>();`
3. `dotnet build`, `dotnet test`, `dotnet run`
Note: this zip contains the newest _PortalLayout.cshtml, portal.css and AuthService.cs (optional logger added; the constructor still works as before).
