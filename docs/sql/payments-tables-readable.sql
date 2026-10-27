/* =====================================================================
   PAYMENTS and CASH_PAYMENT_DETAILS: READABLE COPY FOR DOCUMENTATION ONLY.
   Do NOT run this file. The EF Core migration "AddPayments"
   (tools\add-payments.ps1, then dotnet ef database update) creates these tables.
   ===================================================================== */

-- 16. PAYMENTS (one-to-one with BOOKINGS: one payment per booking)
CREATE TABLE PAYMENTS (
    PAYMENT_ID            INT IDENTITY(1,1) PRIMARY KEY,
    BOOKING_ID            INT            NOT NULL UNIQUE,
    AMOUNT                DECIMAL(10,2)  NOT NULL,
    PAYMENT_METHOD        NVARCHAR(50)   NOT NULL,
    PAYMENT_STATUS        NVARCHAR(50)   NOT NULL,
    TRANSACTION_REFERENCE NVARCHAR(50)   NOT NULL UNIQUE,
    CREATED_AT            DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    PAID_AT               DATETIME2      NULL,
    CONSTRAINT CK_PAYMENTS_METHOD       CHECK (PAYMENT_METHOD IN ('CashOnCompletion', 'MockEWallet')),
    CONSTRAINT CK_PAYMENTS_STATUS       CHECK (PAYMENT_STATUS IN ('PendingCollection', 'Paid', 'Refunded')),
    CONSTRAINT CK_PAYMENTS_AMOUNT       CHECK (AMOUNT >= 0),
    CONSTRAINT CK_PAYMENTS_PENDING_CASH CHECK (PAYMENT_STATUS <> 'PendingCollection' OR PAYMENT_METHOD = 'CashOnCompletion'),
    CONSTRAINT CK_PAYMENTS_PAID_AT      CHECK (PAYMENT_STATUS = 'PendingCollection' OR PAID_AT IS NOT NULL),
    FOREIGN KEY (BOOKING_ID) REFERENCES BOOKINGS(BOOKING_ID)
);
GO

-- 17. CASH_PAYMENT_DETAILS (one-to-one with PAYMENTS, same key; only for CashOnCompletion)
CREATE TABLE CASH_PAYMENT_DETAILS (
    PAYMENT_ID               INT       NOT NULL PRIMARY KEY,
    IS_COLLECTED_BY_PROVIDER BIT       NOT NULL DEFAULT 0,
    COLLECTED_AT             DATETIME2 NULL,
    CONSTRAINT CK_CASH_COLLECTED CHECK ((IS_COLLECTED_BY_PROVIDER = 0 AND COLLECTED_AT IS NULL)
                                     OR (IS_COLLECTED_BY_PROVIDER = 1 AND COLLECTED_AT IS NOT NULL)),
    FOREIGN KEY (PAYMENT_ID) REFERENCES PAYMENTS(PAYMENT_ID)
);
GO

-- Handy check after testing (LocalDB, in SSMS):
-- SELECT p.*, c.IS_COLLECTED_BY_PROVIDER, c.COLLECTED_AT, b.STATUS, b.PAYMENT_STATUS
-- FROM PAYMENTS p
-- JOIN BOOKINGS b ON b.BOOKING_ID = p.BOOKING_ID
-- LEFT JOIN CASH_PAYMENT_DETAILS c ON c.PAYMENT_ID = p.PAYMENT_ID
-- ORDER BY p.PAYMENT_ID DESC;
