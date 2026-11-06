/* SERVISYO - run this BEFORE updating a database to v2.3 (EF migration DerivedData).
   It only READS and changes nothing. Run it on LocalDB (and later on MonsterASP before deploying).

   Grid 1: every payment and what it becomes (NEW_PAYMENT_ID: 1 Cash on Completion, 2 GCash, 3 Maya).
   Grid 2: payments the update cannot convert. If grid 2 is EMPTY, the payments are safe.
           If it lists rows, the update stops without changing anything - send grid 2 to Claude.
   Grid 3: how many rows the three removed tables have now. NOTIFICATIONS and EMAIL_OTPS only held derivable or
           temporary rows. BOOKING_REPORTS was never filled by the app; if it has rows, they are only in your backup
           after the update (the update script makes one first). */

-- 1. Every payment, before -> after
SELECT p.PAYMENT_ID AS OLD_PAYMENT_ID,
       p.BOOKING_ID,
       p.PAYMENT_METHOD AS OLD_METHOD,
       CASE WHEN p.PAYMENT_METHOD = N'CashOnCompletion' THEN 1
            WHEN p.PAYMENT_METHOD = N'MockEWallet' AND p.TRANSACTION_REFERENCE LIKE N'GCASH-%' THEN 2
            WHEN p.PAYMENT_METHOD = N'MockEWallet' AND p.TRANSACTION_REFERENCE LIKE N'MAYA-%' THEN 3
       END AS NEW_PAYMENT_ID,
       p.PAYMENT_STATUS,
       p.TRANSACTION_REFERENCE,
       p.CREATED_AT,
       p.PAID_AT,
       p.AMOUNT,
       b.TOTAL_FEE,
       cash.IS_COLLECTED_BY_PROVIDER,
       CASE
           WHEN p.PAYMENT_METHOD NOT IN (N'CashOnCompletion', N'MockEWallet')
             OR (p.PAYMENT_METHOD = N'MockEWallet' AND p.TRANSACTION_REFERENCE NOT LIKE N'GCASH-%' AND p.TRANSACTION_REFERENCE NOT LIKE N'MAYA-%')
               THEN N'the payment method is unknown'
           WHEN p.AMOUNT <> b.TOTAL_FEE
               THEN N'the amount is not the booking''s TOTAL_FEE'
           WHEN p.PAYMENT_METHOD = N'CashOnCompletion' AND cash.PAYMENT_ID IS NULL
               THEN N'a cash payment without its CASH_PAYMENT_DETAILS row'
           WHEN p.PAYMENT_METHOD = N'CashOnCompletion'
                AND cash.IS_COLLECTED_BY_PROVIDER <> CASE WHEN p.PAYMENT_STATUS = N'Paid' THEN 1 ELSE 0 END
               THEN N'"collected" does not match the payment status'
       END AS PROBLEM
FROM dbo.PAYMENTS p
JOIN dbo.BOOKINGS b ON b.BOOKING_ID = p.BOOKING_ID
LEFT JOIN dbo.CASH_PAYMENT_DETAILS cash ON cash.PAYMENT_ID = p.PAYMENT_ID
ORDER BY p.PAYMENT_ID;

-- 2. Payments that cannot be converted (must be EMPTY)
SELECT x.OLD_PAYMENT_ID, x.BOOKING_ID, x.OLD_METHOD, x.TRANSACTION_REFERENCE, x.AMOUNT, x.TOTAL_FEE, x.PROBLEM
FROM (
    SELECT p.PAYMENT_ID AS OLD_PAYMENT_ID,
           p.BOOKING_ID,
           p.PAYMENT_METHOD AS OLD_METHOD,
           CASE WHEN p.PAYMENT_METHOD = N'CashOnCompletion' THEN 1
                WHEN p.PAYMENT_METHOD = N'MockEWallet' AND p.TRANSACTION_REFERENCE LIKE N'GCASH-%' THEN 2
                WHEN p.PAYMENT_METHOD = N'MockEWallet' AND p.TRANSACTION_REFERENCE LIKE N'MAYA-%' THEN 3
           END AS NEW_PAYMENT_ID,
           p.PAYMENT_STATUS,
           p.TRANSACTION_REFERENCE,
           p.CREATED_AT,
           p.PAID_AT,
           p.AMOUNT,
           b.TOTAL_FEE,
           cash.IS_COLLECTED_BY_PROVIDER,
           CASE
               WHEN p.PAYMENT_METHOD NOT IN (N'CashOnCompletion', N'MockEWallet')
                 OR (p.PAYMENT_METHOD = N'MockEWallet' AND p.TRANSACTION_REFERENCE NOT LIKE N'GCASH-%' AND p.TRANSACTION_REFERENCE NOT LIKE N'MAYA-%')
                   THEN N'the payment method is unknown'
               WHEN p.AMOUNT <> b.TOTAL_FEE
                   THEN N'the amount is not the booking''s TOTAL_FEE'
               WHEN p.PAYMENT_METHOD = N'CashOnCompletion' AND cash.PAYMENT_ID IS NULL
                   THEN N'a cash payment without its CASH_PAYMENT_DETAILS row'
               WHEN p.PAYMENT_METHOD = N'CashOnCompletion'
                    AND cash.IS_COLLECTED_BY_PROVIDER <> CASE WHEN p.PAYMENT_STATUS = N'Paid' THEN 1 ELSE 0 END
                   THEN N'"collected" does not match the payment status'
           END AS PROBLEM
    FROM dbo.PAYMENTS p
    JOIN dbo.BOOKINGS b ON b.BOOKING_ID = p.BOOKING_ID
    LEFT JOIN dbo.CASH_PAYMENT_DETAILS cash ON cash.PAYMENT_ID = p.PAYMENT_ID
) x
WHERE x.PROBLEM IS NOT NULL
ORDER BY x.OLD_PAYMENT_ID;

-- 3. The tables that are removed
SELECT N'NOTIFICATIONS' AS TABLE_NAME, COUNT(*) AS ROWS_NOW FROM dbo.NOTIFICATIONS
UNION ALL SELECT N'EMAIL_OTPS', COUNT(*) FROM dbo.EMAIL_OTPS
UNION ALL SELECT N'BOOKING_REPORTS', COUNT(*) FROM dbo.BOOKING_REPORTS
UNION ALL SELECT N'CASH_PAYMENT_DETAILS', COUNT(*) FROM dbo.CASH_PAYMENT_DETAILS;
