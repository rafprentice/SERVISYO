/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: sample chat messages, so the Messages page has something to show.

   WHO? Set @UserEmail below to the e-mail of a CUSTOMER or a SERVICE PROVIDER you can SIGN IN as.
   (The sample users from dev-sample-users-data.sql cannot sign in.)

   RUN THESE FIRST (the chat only lists people you share a booking with):
     docs\dev-sample-providers-data.sql
     docs\dev-sample-customer-bookings-data.sql   (for a customer)     or
     docs\dev-sample-provider-bookings-data.sql   (for a provider)
   If the user has no bookings with anybody, this script stops with a clear message and changes nothing.

   WHAT IT DOES
   - Finds up to 3 people (Active accounts) that the user shares a booking with, most recent booking first.
   - For each of them that has NO messages with the user yet, adds a short conversation of 3-4 lines.
     The last line is always from the OTHER person and is UNREAD, so you see an unread badge on the list,
     in the sidebar, and watch it clear when you open the conversation.
   - People who already have any message with the user are skipped, so it is safe to re-run.
   - It never deletes or changes existing messages. (MESSAGES has no "sample" marker column, so to start over
     delete the rows yourself, e.g. in SSMS: DELETE FROM MESSAGES WHERE SENDER_ID = <id> OR RECEIVER_ID = <id>.)
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -I -i docs\dev-sample-messages-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @UserEmail NVARCHAR(155) = N'you@email.com';   -- <-- change this to your own customer's or provider's e-mail

DECLARE @UserId INT = (SELECT USER_ID FROM USERS WHERE EMAIL = LOWER(@UserEmail));
IF @UserId IS NULL
BEGIN
    DECLARE @NoUser NVARCHAR(500) = N'No user has the e-mail ' + @UserEmail
        + N'. Change @UserEmail at the top of this script (register the account on the website first).';
    THROW 50001, @NoUser, 1;
END

DECLARE @IsCustomer BIT = CASE WHEN EXISTS (SELECT 1 FROM CUSTOMERS WHERE USER_ID = @UserId) THEN 1 ELSE 0 END;
DECLARE @IsProvider BIT = CASE WHEN EXISTS (SELECT 1 FROM SERVICE_PROVIDERS WHERE USER_ID = @UserId) THEN 1 ELSE 0 END;

IF @IsCustomer = 0 AND @IsProvider = 0
    THROW 50002, N'That user is neither a customer nor a service provider (administrators do not use the chat).', 1;

-- ---------- Who can be chatted with: the other side of this user's bookings (Active accounts only) ----------
DECLARE @Partners TABLE (N INT IDENTITY(1,1) PRIMARY KEY, PartnerUserId INT NOT NULL, PartnerName NVARCHAR(120) NOT NULL);

IF @IsCustomer = 1
    INSERT INTO @Partners (PartnerUserId, PartnerName)
    SELECT TOP (3) x.PartnerUserId, x.PartnerName
    FROM (SELECT pu.USER_ID AS PartnerUserId, pu.FIRST_NAME + N' ' + pu.LAST_NAME AS PartnerName, MAX(b.CREATED_AT) AS LastBooking
          FROM BOOKINGS b
          JOIN CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
          JOIN SERVICE_PROVIDERS p ON p.PROVIDER_ID = b.PROVIDER_ID
          JOIN USERS pu ON pu.USER_ID = p.USER_ID
          WHERE c.USER_ID = @UserId AND pu.ACCOUNT_STATUS = N'Active'
          GROUP BY pu.USER_ID, pu.FIRST_NAME, pu.LAST_NAME) x
    ORDER BY x.LastBooking DESC, x.PartnerUserId;
ELSE
    INSERT INTO @Partners (PartnerUserId, PartnerName)
    SELECT TOP (3) x.PartnerUserId, x.PartnerName
    FROM (SELECT cu.USER_ID AS PartnerUserId, cu.FIRST_NAME + N' ' + cu.LAST_NAME AS PartnerName, MAX(b.CREATED_AT) AS LastBooking
          FROM BOOKINGS b
          JOIN SERVICE_PROVIDERS p ON p.PROVIDER_ID = b.PROVIDER_ID
          JOIN CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
          JOIN USERS cu ON cu.USER_ID = c.USER_ID
          WHERE p.USER_ID = @UserId AND cu.ACCOUNT_STATUS = N'Active'
          GROUP BY cu.USER_ID, cu.FIRST_NAME, cu.LAST_NAME) x
    ORDER BY x.LastBooking DESC, x.PartnerUserId;

IF NOT EXISTS (SELECT 1 FROM @Partners)
    THROW 50003, N'This user has no bookings with an Active person yet. Run the sample bookings script first, then run this one again.', 1;

-- ---------- The conversations (Variant = which of the 3 people; FromPartner = 1 when the OTHER person writes) ----------
DECLARE @Script TABLE (Variant INT, ViewerIsCustomer BIT, Seq INT, FromPartner BIT, Txt NVARCHAR(1000), MinutesAgo INT, Unread BIT);
INSERT INTO @Script VALUES
 -- the user is a CUSTOMER, the other person is a provider
 (1, 1, 1, 0, N'Hi! I just sent a booking request. Are you free on that day?',                                   2900, 0),
 (1, 1, 2, 1, N'Hello! Yes, I am available. Can you tell me more about the problem?',                            2870, 0),
 (1, 1, 3, 0, N'The pipe under the kitchen sink is leaking, and the cabinet floor is already wet.',            2860, 0),
 (1, 1, 4, 1, N'Thanks for the details. I will bring replacement fittings so we can finish in one visit.',      25,   1),
 (2, 1, 1, 0, N'Good morning! Do you bring your own materials and equipment?',                                   1500, 0),
 (2, 1, 2, 1, N'Good morning! Yes, everything is included in the listed price.',                                 1490, 0),
 (2, 1, 3, 1, N'I will message you the day before to confirm the time.',                                         180,  1),
 (3, 1, 1, 1, N'Hi! Thank you for booking with us. Is the address in your request correct?',                    420,  1),
 (3, 1, 2, 1, N'Please let me know if there is a gate code or a landmark that helps us find the place.',         410,  1),
 -- the user is a PROVIDER, the other person is a customer
 (1, 0, 1, 1, N'Hi! I booked your service for next week. Is the schedule okay for you?',                        2900, 0),
 (1, 0, 2, 0, N'Hello! Yes, that works. Please send photos if you have any, so I can prepare the right tools.',  2880, 0),
 (1, 0, 3, 1, N'Sure, will do. Thank you!',                                                                      30,   1),
 (2, 0, 1, 1, N'Good afternoon. Do you also do weekend visits?',                                                 1400, 0),
 (2, 0, 2, 0, N'Hello! Yes, Saturdays are fine. Which time works best for you?',                                 1390, 0),
 (2, 0, 3, 1, N'Saturday morning would be perfect, around 9 AM.',                                                200,  1),
 (3, 0, 1, 1, N'Hello, I just sent a request. Please tell me if you need anything else from me.',                380,  1);

-- ---------- Add each conversation that does not exist yet ----------
DECLARE @I INT = 1, @Count INT = (SELECT COUNT(*) FROM @Partners), @PartnerId INT, @PartnerName NVARCHAR(120), @Added INT = 0;
DECLARE @Now DATETIME2(0) = SYSUTCDATETIME();      -- messages are stored in UTC

WHILE @I <= @Count
BEGIN
    SELECT @PartnerId = PartnerUserId, @PartnerName = PartnerName FROM @Partners WHERE N = @I;

    IF EXISTS (SELECT 1 FROM MESSAGES
               WHERE (SENDER_ID = @UserId AND RECEIVER_ID = @PartnerId) OR (SENDER_ID = @PartnerId AND RECEIVER_ID = @UserId))
    BEGIN
        PRINT N'Skipped ' + @PartnerName + N': there are already messages with this person.';
    END
    ELSE
    BEGIN
        INSERT INTO MESSAGES (SENDER_ID, RECEIVER_ID, MESSAGE_TEXT, IS_READ, SENT_AT)
        SELECT CASE WHEN s.FromPartner = 1 THEN @PartnerId ELSE @UserId END,
               CASE WHEN s.FromPartner = 1 THEN @UserId ELSE @PartnerId END,
               s.Txt,
               CASE WHEN s.Unread = 1 THEN 0 ELSE 1 END,
               DATEADD(MINUTE, -s.MinutesAgo, @Now)
        FROM @Script s
        WHERE s.Variant = @I AND s.ViewerIsCustomer = @IsCustomer
        ORDER BY s.Seq;

        SET @Added += @@ROWCOUNT;
        PRINT N'Added a conversation with ' + @PartnerName + N'.';
    END

    SET @I += 1;
END

PRINT CAST(@Added AS NVARCHAR(20)) + N' message(s) added.';

SELECT TOP (20) m.MESSAGE_ID AS Id,
       CASE WHEN m.SENDER_ID = @UserId THEN N'me' ELSE su.FIRST_NAME END AS [From],
       LEFT(m.MESSAGE_TEXT, 60) AS [Text], m.IS_READ AS IsRead, m.SENT_AT AS SentAtUtc
FROM MESSAGES m
JOIN USERS su ON su.USER_ID = m.SENDER_ID
WHERE m.SENDER_ID = @UserId OR m.RECEIVER_ID = @UserId
ORDER BY m.MESSAGE_ID DESC;
