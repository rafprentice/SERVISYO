/* =====================================================================================================
   SERVISYO - Cebu-Based Service Provider Booking Platform            Group 3 | Microsoft SQL Server
   DATABASE DESIGN v2  (improved version of DatabaseDesign.sql)

   CONTENTS
     PART 0  Safety checks (run on the SERVISYO database, after "dotnet ef database update")
     PART 1  Tables, keys, relationships, CHECK rules and indexes  (REFERENCE - created by the EF migrations)
     PART 2  Functions (8)
     PART 3  Views (2)
     PART 4  Stored procedures (19)
     PART 5  Triggers (3 new; the 3 audit triggers trg_Users_Audit / trg_Bookings_Audit / trg_Services_Audit are unchanged)
     PART 6  Self-test: runs every procedure, function and trigger with sample data and ROLLS IT ALL BACK
     PART 7  Examples and subqueries for the defense

   HOW THIS FILE IS USED
     - The web app applies PARTS 2-5 itself with the EF migration "DatabaseV2" (dotnet ef database update):
       the SAME SQL, so both ways give the same result. Running this whole file again is safe: every object is
       CREATE OR ALTER, and PART 0 stops if a different object with the same name already exists.
     - PART 1 is not executed (the tables already exist and belong to the EF migrations); it is here so the
       complete design is in one place for the documentation and the ERD.
     - Open in SSMS, connect to (localdb)\MSSQLLocalDB, choose the SERVISYO database, press Execute (F5).
       The last grid is the self-test result; the Messages tab says whether all tests passed.

   WHAT IS BETTER THAN v1 (DatabaseDesign.sql)
     - Written for the REAL tables of the app (USER_ID, PROVIDER_ID, TOTAL_FEE, BOOKING_SERVICES, PAYMENTS ...).
     - Every procedure that writes runs in a transaction (all or nothing) with SET XACT_ABORT ON, and locks
       what it checks (UPDLOCK / HOLDLOCK), so two users at the same moment cannot double-book or double-pay.
     - Procedures take the acting USER_ID and check ownership + account status themselves (v1 trusted a role
       or customer id given by the caller).
     - Booking rules check the WHOLE job (start + duration) against working hours, breaks and other bookings,
       not only the start time; weekday numbers do not depend on SET DATEFIRST.
     - Search reads the stored AVG_RATING instead of recalculating it for every row.
     - A trigger keeps AVG_RATING and REVIEW_COUNT right after every review change (v1 forgot REVIEW_COUNT on
       moderation); a trigger enforces the booking life cycle; a trigger logs payments into SYSTEM_LOGS.
     - Times are stored in UTC (SYSUTCDATETIME) and compared in Cebu time (fn_ManilaNow), like the app.
   ===================================================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;     -- needed for the filtered index on BOOKINGS (sqlcmd turns it off unless -I is used)
SET NOEXEC OFF;
GO

/* =====================================================================================================
   PART 0 - SAFETY CHECKS
   ===================================================================================================== */
IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL OR OBJECT_ID(N'dbo.BOOKINGS') IS NULL
   OR OBJECT_ID(N'dbo.PAYMENTS') IS NULL OR OBJECT_ID(N'dbo.SYSTEM_LOGS') IS NULL
BEGIN
    RAISERROR(N'Stopped: run this file on the SERVISYO database (choose it in the SSMS toolbar). Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO
-- the app must already know about the new triggers (HasTrigger in ServisyoDbContext.cs), so the EF migration comes first
IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]DatabaseV2')
BEGIN
    RAISERROR(N'Stopped: run "dotnet ef database update --project src\Servisyo.Web" first (it installs these same objects). Then run this file again for the self-test. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

DECLARE @clash NVARCHAR(MAX) = STUFF((
    SELECT N', ' + o.name
    FROM sys.objects o
    WHERE o.schema_id = SCHEMA_ID(N'dbo')
      AND o.name IN (N'fn_DayOfWeek', N'fn_ManilaNow', N'fn_GetFullName', N'fn_GetProviderAvgRating', N'fn_CountProviderJobsOnDate', N'fn_IsProviderAvailable', N'fn_HasBookingConflict', N'fn_GetAvailableSlots', N'vw_BookingOverview', N'vw_ProviderDirectory', N'usp_RegisterCustomer', N'usp_RegisterProvider', N'usp_GetUserForLogin', N'usp_ApproveProvider', N'usp_UpdateAccountStatus', N'usp_ModerateReview', N'usp_AddService', N'usp_SearchServices', N'usp_GetProviderAvailability', N'usp_AddAvailability', N'usp_UpdateAvailability', N'usp_DeleteAvailability', N'usp_CreateBooking', N'usp_CancelBooking', N'usp_UpdateBookingStatus', N'usp_ApproveAndPay', N'usp_ConfirmCashReceived', N'usp_AddReview', N'usp_GetMonthlyReport', N'trg_Reviews_RatingSync', N'trg_Bookings_StatusFlow', N'trg_Payments_Audit')
      AND ISNULL(OBJECT_DEFINITION(o.object_id), N'') NOT LIKE N'%SERVISYO-DB-V2%'
    ORDER BY o.name
    FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'');
IF @clash IS NOT NULL
BEGIN
    DECLARE @msg NVARCHAR(2048) = LEFT(N'Stopped without changing anything: these objects already exist in this database and were not made by SERVISYO DatabaseV2: '
        + @clash + N'. Rename or drop them first (or send this message to Claude).', 2048);
    RAISERROR(@msg, 16, 1);
    SET NOEXEC ON;
END
GO

/* =====================================================================================================
   PART 1 - TABLES (REFERENCE ONLY - NOT EXECUTED)
   The real tables, exactly as the EF migrations created them (column names are UPPER_SNAKE_CASE; all times
   are UTC). Generated from the EF model snapshot.

CREATE TABLE USERS (
    USER_ID                      INT             IDENTITY(1,1) NOT NULL,
    ACCOUNT_STATUS               NVARCHAR(50)    NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    EMAIL                        NVARCHAR(155)   NOT NULL,
    EMAIL_VERIFICATION_PENDING   BIT             NOT NULL,
    FIRST_NAME                   NVARCHAR(155)   NOT NULL,
    LAST_NAME                    NVARCHAR(155)   NOT NULL,
    MIDDLE_NAME                  NVARCHAR(155)   NULL,
    PASSWORD_HASH                NVARCHAR(255)   NOT NULL,
    PHONE_NUMBER                 NVARCHAR(15)    NULL,
    ROLE                         NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_USERS PRIMARY KEY (USER_ID),
    CONSTRAINT CK_USERS_ROLE CHECK ([ROLE] IN ('Customer','Service Provider','Administrator')),
    CONSTRAINT CK_USERS_STATUS CHECK ([ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated'))
);
CREATE UNIQUE INDEX IX_USERS_EMAIL ON USERS (EMAIL);
-- triggers on USERS: trg_Users_Audit

CREATE TABLE CUSTOMERS (
    CUSTOMER_ID                  INT             IDENTITY(1,1) NOT NULL,
    ADDRESS                      NVARCHAR(255)   NULL,
    CITY                         NVARCHAR(255)   NULL,
    PROFILE_PHOTO                NVARCHAR(255)   NULL,
    USER_ID                      INT             NOT NULL,
    CONSTRAINT PK_CUSTOMERS PRIMARY KEY (CUSTOMER_ID),
    CONSTRAINT FK_CUSTOMERS_USERS_USER_ID FOREIGN KEY (USER_ID) REFERENCES USERS(USER_ID)
);
CREATE UNIQUE INDEX IX_CUSTOMERS_USER_ID ON CUSTOMERS (USER_ID);

CREATE TABLE SERVICE_PROVIDERS (
    PROVIDER_ID                  INT             IDENTITY(1,1) NOT NULL,
    ADDRESS                      NVARCHAR(255)   NULL,
    APPROVAL_STATUS              NVARCHAR(50)    NOT NULL,
    AVG_RATING                   DECIMAL(2,1)    NULL,
    BASE_FEE                     DECIMAL(10,2)   NULL,
    BUSINESS_NAME                NVARCHAR(155)   NOT NULL,
    CITY                         NVARCHAR(50)    NULL,
    DATE_APPLIED                 DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    DATE_APPROVED                DATETIME2       NULL,
    DESCRIPTION                  NVARCHAR(255)   NULL,
    MAX_JOBS_PER_DAY             INT             NOT NULL,
    MIN_NOTICE_DAYS              INT             NOT NULL,
    PROFILE_PHOTO                NVARCHAR(255)   NULL,
    REVIEW_COUNT                 INT             NOT NULL,
    USER_ID                      INT             NOT NULL,
    CONSTRAINT PK_SERVICE_PROVIDERS PRIMARY KEY (PROVIDER_ID),
    CONSTRAINT FK_SERVICE_PROVIDERS_USERS_USER_ID FOREIGN KEY (USER_ID) REFERENCES USERS(USER_ID),
    CONSTRAINT CK_PROVIDERS_APPROVAL CHECK ([APPROVAL_STATUS] IN ('Pending','Approved','Rejected')),
    CONSTRAINT CK_PROVIDERS_LIMITS CHECK ([MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] > 0),
    CONSTRAINT CK_PROVIDERS_RATING CHECK ([AVG_RATING] IS NULL OR [AVG_RATING] BETWEEN 0 AND 5)
);
CREATE UNIQUE INDEX IX_SERVICE_PROVIDERS_USER_ID ON SERVICE_PROVIDERS (USER_ID);

CREATE TABLE PROVIDER_AVAILABILITY (
    AVAILABILITY_ID              INT             IDENTITY(1,1) NOT NULL,
    DAY_OF_WEEK                  TINYINT         NOT NULL,
    END_TIME                     TIME            NOT NULL,
    IS_AVAILABLE                 BIT             NOT NULL,
    PROVIDER_ID                  INT             NOT NULL,
    START_TIME                   TIME            NOT NULL,
    CONSTRAINT PK_PROVIDER_AVAILABILITY PRIMARY KEY (AVAILABILITY_ID),
    CONSTRAINT FK_PROVIDER_AVAILABILITY_SERVICE_PROVIDERS_PROVIDER_ID FOREIGN KEY (PROVIDER_ID) REFERENCES SERVICE_PROVIDERS(PROVIDER_ID),
    CONSTRAINT CK_AVAIL_DAY CHECK ([DAY_OF_WEEK] BETWEEN 0 AND 6),
    CONSTRAINT CK_AVAIL_TIMES CHECK ([END_TIME] > [START_TIME])
);
CREATE UNIQUE INDEX IX_PROVIDER_AVAILABILITY_PROVIDER_ID_DAY_OF_WEEK ON PROVIDER_AVAILABILITY (PROVIDER_ID, DAY_OF_WEEK);

CREATE TABLE PROVIDER_BREAKS (
    BREAK_ID                     INT             IDENTITY(1,1) NOT NULL,
    AVAILABILITY_ID              INT             NOT NULL,
    BREAK_END                    TIME            NOT NULL,
    BREAK_START                  TIME            NOT NULL,
    CONSTRAINT PK_PROVIDER_BREAKS PRIMARY KEY (BREAK_ID),
    CONSTRAINT FK_PROVIDER_BREAKS_PROVIDER_AVAILABILITY_AVAILABILITY_ID FOREIGN KEY (AVAILABILITY_ID) REFERENCES PROVIDER_AVAILABILITY(AVAILABILITY_ID) ON DELETE CASCADE,
    CONSTRAINT CK_BREAK_TIMES CHECK ([BREAK_END] > [BREAK_START])
);
CREATE INDEX IX_PROVIDER_BREAKS_AVAILABILITY_ID ON PROVIDER_BREAKS (AVAILABILITY_ID);

CREATE TABLE PROVIDER_UNAVAILABLE_DATES (
    UNAVAILABLE_ID               INT             IDENTITY(1,1) NOT NULL,
    PROVIDER_ID                  INT             NOT NULL,
    REASON                       NVARCHAR(255)   NULL,
    UNAVAILABLE_DATE             DATE            NOT NULL,
    CONSTRAINT PK_PROVIDER_UNAVAILABLE_DATES PRIMARY KEY (UNAVAILABLE_ID),
    CONSTRAINT FK_PROVIDER_UNAVAILABLE_DATES_SERVICE_PROVIDERS_PROVIDER_ID FOREIGN KEY (PROVIDER_ID) REFERENCES SERVICE_PROVIDERS(PROVIDER_ID)
);
CREATE UNIQUE INDEX IX_PROVIDER_UNAVAILABLE_DATES_PROVIDER_ID_UNAVAILABLE_DATE ON PROVIDER_UNAVAILABLE_DATES (PROVIDER_ID, UNAVAILABLE_DATE);

CREATE TABLE SERVICE_CATEGORIES (
    CATEGORY_ID                  INT             IDENTITY(1,1) NOT NULL,
    CATEGORY_NAME                NVARCHAR(100)   NOT NULL,
    DESCRIPTION                  NVARCHAR(255)   NULL,
    IS_ACTIVE                    BIT             NOT NULL,
    CONSTRAINT PK_SERVICE_CATEGORIES PRIMARY KEY (CATEGORY_ID)
);
CREATE UNIQUE INDEX IX_SERVICE_CATEGORIES_CATEGORY_NAME ON SERVICE_CATEGORIES (CATEGORY_NAME);

CREATE TABLE PROVIDER_CATEGORIES (
    PROVIDER_CATEGORY_ID         INT             IDENTITY(1,1) NOT NULL,
    CATEGORY_ID                  INT             NOT NULL,
    PROVIDER_ID                  INT             NOT NULL,
    CONSTRAINT PK_PROVIDER_CATEGORIES PRIMARY KEY (PROVIDER_CATEGORY_ID),
    CONSTRAINT FK_PROVIDER_CATEGORIES_SERVICE_CATEGORIES_CATEGORY_ID FOREIGN KEY (CATEGORY_ID) REFERENCES SERVICE_CATEGORIES(CATEGORY_ID),
    CONSTRAINT FK_PROVIDER_CATEGORIES_SERVICE_PROVIDERS_PROVIDER_ID FOREIGN KEY (PROVIDER_ID) REFERENCES SERVICE_PROVIDERS(PROVIDER_ID)
);
CREATE INDEX IX_PROVIDER_CATEGORIES_CATEGORY_ID ON PROVIDER_CATEGORIES (CATEGORY_ID);
CREATE UNIQUE INDEX IX_PROVIDER_CATEGORIES_PROVIDER_ID_CATEGORY_ID ON PROVIDER_CATEGORIES (PROVIDER_ID, CATEGORY_ID);

CREATE TABLE SERVICES (
    SERVICE_ID                   INT             IDENTITY(1,1) NOT NULL,
    CATEGORY_ID                  INT             NOT NULL,
    DESCRIPTION                  NVARCHAR(255)   NULL,
    DURATION_MINUTES             INT             NOT NULL,
    IS_ACTIVE                    BIT             NOT NULL,
    PRICE                        DECIMAL(10,2)   NOT NULL,
    PROVIDER_ID                  INT             NOT NULL,
    SERVICE_NAME                 NVARCHAR(100)   NOT NULL,
    CONSTRAINT PK_SERVICES PRIMARY KEY (SERVICE_ID),
    CONSTRAINT FK_SERVICES_SERVICE_CATEGORIES_CATEGORY_ID FOREIGN KEY (CATEGORY_ID) REFERENCES SERVICE_CATEGORIES(CATEGORY_ID),
    CONSTRAINT FK_SERVICES_SERVICE_PROVIDERS_PROVIDER_ID FOREIGN KEY (PROVIDER_ID) REFERENCES SERVICE_PROVIDERS(PROVIDER_ID),
    CONSTRAINT CK_SERVICES_DURATION CHECK ([DURATION_MINUTES] > 0),
    CONSTRAINT CK_SERVICES_PRICE CHECK ([PRICE] >= 0)
);
CREATE INDEX IX_SERVICES_CATEGORY_ID ON SERVICES (CATEGORY_ID);
CREATE INDEX IX_SERVICES_PROVIDER_ID ON SERVICES (PROVIDER_ID);
-- triggers on SERVICES: trg_Services_Audit

CREATE TABLE BOOKINGS (
    BOOKING_ID                   INT             IDENTITY(1,1) NOT NULL,
    BOOKING_DATE                 DATE            NOT NULL,
    BOOKING_TIME                 TIME            NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CUSTOMER_ID                  INT             NOT NULL,
    END_TIME                     AS (DATEADD(MINUTE, [TOTAL_DURATION_MINUTES], [BOOKING_TIME])),
    PAYMENT_STATUS               NVARCHAR(50)    NOT NULL,
    PROVIDER_ID                  INT             NOT NULL,
    ROW_VERSION                  ROWVERSION      NOT NULL,
    SERVICE_DESCRIPTION          NVARCHAR(255)   NULL,
    SERVICE_LOCATION             NVARCHAR(255)   NULL,
    STATUS                       NVARCHAR(50)    NOT NULL,
    TOTAL_DURATION_MINUTES       INT             NOT NULL,
    TOTAL_FEE                    DECIMAL(10,2)   NOT NULL,
    UPDATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BOOKINGS PRIMARY KEY (BOOKING_ID),
    CONSTRAINT FK_BOOKINGS_CUSTOMERS_CUSTOMER_ID FOREIGN KEY (CUSTOMER_ID) REFERENCES CUSTOMERS(CUSTOMER_ID),
    CONSTRAINT FK_BOOKINGS_SERVICE_PROVIDERS_PROVIDER_ID FOREIGN KEY (PROVIDER_ID) REFERENCES SERVICE_PROVIDERS(PROVIDER_ID),
    CONSTRAINT CK_BOOKINGS_DURATION CHECK ([TOTAL_DURATION_MINUTES] > 0),
    CONSTRAINT CK_BOOKINGS_PAYMENT CHECK ([PAYMENT_STATUS] IN ('Unpaid','Paid','Refunded')),
    CONSTRAINT CK_BOOKINGS_STATUS CHECK ([STATUS] IN ('Pending','Accepted','Rejected','In Progress','Completed','Cancelled'))
);
CREATE INDEX IX_BOOKINGS_CUSTOMER_ID ON BOOKINGS (CUSTOMER_ID);
CREATE UNIQUE INDEX UX_BOOKINGS_ACTIVE_SLOT ON BOOKINGS (PROVIDER_ID, BOOKING_DATE, BOOKING_TIME) WHERE [STATUS] IN ('Pending','Accepted','In Progress');
-- triggers on BOOKINGS: trg_Bookings_Audit

CREATE TABLE BOOKING_SERVICES (
    BOOKING_SERVICE_ID           INT             IDENTITY(1,1) NOT NULL,
    BOOKING_ID                   INT             NOT NULL,
    DURATION_MINUTES_SNAPSHOT    INT             NOT NULL,
    PRICE_SNAPSHOT               DECIMAL(10,2)   NOT NULL,
    SERVICE_ID                   INT             NOT NULL,
    CONSTRAINT PK_BOOKING_SERVICES PRIMARY KEY (BOOKING_SERVICE_ID),
    CONSTRAINT FK_BOOKING_SERVICES_BOOKINGS_BOOKING_ID FOREIGN KEY (BOOKING_ID) REFERENCES BOOKINGS(BOOKING_ID),
    CONSTRAINT FK_BOOKING_SERVICES_SERVICES_SERVICE_ID FOREIGN KEY (SERVICE_ID) REFERENCES SERVICES(SERVICE_ID)
);
CREATE INDEX IX_BOOKING_SERVICES_SERVICE_ID ON BOOKING_SERVICES (SERVICE_ID);
CREATE UNIQUE INDEX IX_BOOKING_SERVICES_BOOKING_ID_SERVICE_ID ON BOOKING_SERVICES (BOOKING_ID, SERVICE_ID);

CREATE TABLE PAYMENTS (
    PAYMENT_ID                   INT             IDENTITY(1,1) NOT NULL,
    AMOUNT                       DECIMAL(10,2)   NOT NULL,
    BOOKING_ID                   INT             NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    PAID_AT                      DATETIME2       NULL,
    PAYMENT_METHOD               NVARCHAR(50)    NOT NULL,
    PAYMENT_STATUS               NVARCHAR(50)    NOT NULL,
    TRANSACTION_REFERENCE        NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_PAYMENTS PRIMARY KEY (PAYMENT_ID),
    CONSTRAINT FK_PAYMENTS_BOOKINGS_BOOKING_ID FOREIGN KEY (BOOKING_ID) REFERENCES BOOKINGS(BOOKING_ID),
    CONSTRAINT CK_PAYMENTS_AMOUNT CHECK ([AMOUNT] >= 0),
    CONSTRAINT CK_PAYMENTS_METHOD CHECK ([PAYMENT_METHOD] IN ('CashOnCompletion','MockEWallet')),
    CONSTRAINT CK_PAYMENTS_PAID_AT CHECK ([PAYMENT_STATUS] = 'PendingCollection' OR [PAID_AT] IS NOT NULL),
    CONSTRAINT CK_PAYMENTS_PENDING_CASH CHECK ([PAYMENT_STATUS] <> 'PendingCollection' OR [PAYMENT_METHOD] = 'CashOnCompletion'),
    CONSTRAINT CK_PAYMENTS_STATUS CHECK ([PAYMENT_STATUS] IN ('PendingCollection','Paid','Refunded'))
);
CREATE UNIQUE INDEX IX_PAYMENTS_BOOKING_ID ON PAYMENTS (BOOKING_ID);
CREATE UNIQUE INDEX IX_PAYMENTS_TRANSACTION_REFERENCE ON PAYMENTS (TRANSACTION_REFERENCE);

CREATE TABLE CASH_PAYMENT_DETAILS (
    PAYMENT_ID                   INT             NOT NULL,
    COLLECTED_AT                 DATETIME2       NULL,
    IS_COLLECTED_BY_PROVIDER     BIT             NOT NULL DEFAULT 0,
    CONSTRAINT PK_CASH_PAYMENT_DETAILS PRIMARY KEY (PAYMENT_ID),
    CONSTRAINT FK_CASH_PAYMENT_DETAILS_PAYMENTS_PAYMENT_ID FOREIGN KEY (PAYMENT_ID) REFERENCES PAYMENTS(PAYMENT_ID),
    CONSTRAINT CK_CASH_COLLECTED CHECK (([IS_COLLECTED_BY_PROVIDER] = 0 AND [COLLECTED_AT] IS NULL) OR ([IS_COLLECTED_BY_PROVIDER] = 1 AND [COLLECTED_AT] IS NOT NULL))
);

CREATE TABLE REVIEWS (
    REVIEW_ID                    INT             IDENTITY(1,1) NOT NULL,
    BOOKING_ID                   INT             NOT NULL,
    COMMENT                      NVARCHAR(255)   NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    RATING                       TINYINT         NOT NULL,
    STATUS                       NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_REVIEWS PRIMARY KEY (REVIEW_ID),
    CONSTRAINT FK_REVIEWS_BOOKINGS_BOOKING_ID FOREIGN KEY (BOOKING_ID) REFERENCES BOOKINGS(BOOKING_ID),
    CONSTRAINT CK_REVIEWS_RATING CHECK ([RATING] BETWEEN 1 AND 5),
    CONSTRAINT CK_REVIEWS_STATUS CHECK ([STATUS] IN ('Visible','Flagged','Removed'))
);
CREATE UNIQUE INDEX IX_REVIEWS_BOOKING_ID ON REVIEWS (BOOKING_ID);

CREATE TABLE MESSAGES (
    MESSAGE_ID                   INT             IDENTITY(1,1) NOT NULL,
    IS_READ                      BIT             NOT NULL,
    MESSAGE_TEXT                 NVARCHAR(1000)  NOT NULL,
    RECEIVER_ID                  INT             NOT NULL,
    SENDER_ID                    INT             NOT NULL,
    SENT_AT                      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MESSAGES PRIMARY KEY (MESSAGE_ID),
    CONSTRAINT FK_MESSAGES_USERS_RECEIVER_ID FOREIGN KEY (RECEIVER_ID) REFERENCES USERS(USER_ID),
    CONSTRAINT FK_MESSAGES_USERS_SENDER_ID FOREIGN KEY (SENDER_ID) REFERENCES USERS(USER_ID)
);
CREATE INDEX IX_MESSAGES_RECEIVER_ID ON MESSAGES (RECEIVER_ID);
CREATE INDEX IX_MESSAGES_SENDER_ID ON MESSAGES (SENDER_ID);

CREATE TABLE NOTIFICATIONS (
    NOTIFICATION_ID              INT             IDENTITY(1,1) NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    IS_READ                      BIT             NOT NULL,
    MESSAGE                      NVARCHAR(255)   NOT NULL,
    USER_ID                      INT             NOT NULL,
    CONSTRAINT PK_NOTIFICATIONS PRIMARY KEY (NOTIFICATION_ID),
    CONSTRAINT FK_NOTIFICATIONS_USERS_USER_ID FOREIGN KEY (USER_ID) REFERENCES USERS(USER_ID)
);
CREATE INDEX IX_NOTIFICATIONS_USER_ID ON NOTIFICATIONS (USER_ID);

CREATE TABLE ADMIN_ACTIONS (
    ACTION_ID                    INT             IDENTITY(1,1) NOT NULL,
    ACTION_TYPE                  NVARCHAR(50)    NOT NULL,
    ADMIN_USER_ID                INT             NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    REASON                       NVARCHAR(255)   NULL,
    TARGET_ID                    INT             NOT NULL,
    TARGET_TYPE                  NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_ADMIN_ACTIONS PRIMARY KEY (ACTION_ID),
    CONSTRAINT FK_ADMIN_ACTIONS_USERS_ADMIN_USER_ID FOREIGN KEY (ADMIN_USER_ID) REFERENCES USERS(USER_ID)
);
CREATE INDEX IX_ADMIN_ACTIONS_ADMIN_USER_ID ON ADMIN_ACTIONS (ADMIN_USER_ID);

CREATE TABLE BOOKING_REPORTS (
    REPORT_ID                    INT             IDENTITY(1,1) NOT NULL,
    BOOKING_ID                   INT             NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    REASON                       NVARCHAR(255)   NOT NULL,
    REPORTED_BY_USER_ID          INT             NOT NULL,
    RESOLUTION_NOTE              NVARCHAR(255)   NULL,
    RESOLVED_AT                  DATETIME2       NULL,
    RESOLVED_BY_USER_ID          INT             NULL,
    STATUS                       NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_BOOKING_REPORTS PRIMARY KEY (REPORT_ID),
    CONSTRAINT FK_BOOKING_REPORTS_BOOKINGS_BOOKING_ID FOREIGN KEY (BOOKING_ID) REFERENCES BOOKINGS(BOOKING_ID),
    CONSTRAINT FK_BOOKING_REPORTS_USERS_REPORTED_BY_USER_ID FOREIGN KEY (REPORTED_BY_USER_ID) REFERENCES USERS(USER_ID),
    CONSTRAINT FK_BOOKING_REPORTS_USERS_RESOLVED_BY_USER_ID FOREIGN KEY (RESOLVED_BY_USER_ID) REFERENCES USERS(USER_ID),
    CONSTRAINT CK_REPORTS_STATUS CHECK ([STATUS] IN ('Open','Under Review','Resolved'))
);
CREATE INDEX IX_BOOKING_REPORTS_BOOKING_ID ON BOOKING_REPORTS (BOOKING_ID);
CREATE INDEX IX_BOOKING_REPORTS_REPORTED_BY_USER_ID ON BOOKING_REPORTS (REPORTED_BY_USER_ID);
CREATE INDEX IX_BOOKING_REPORTS_RESOLVED_BY_USER_ID ON BOOKING_REPORTS (RESOLVED_BY_USER_ID);

CREATE TABLE EMAIL_OTPS (
    EMAIL_OTP_ID                 INT             IDENTITY(1,1) NOT NULL,
    ATTEMPTS                     INT             NOT NULL,
    CODE_HASH                    NVARCHAR(255)   NOT NULL,
    CONSUMED_AT                  DATETIME2       NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    EXPIRES_AT                   DATETIME2       NOT NULL,
    PURPOSE                      NVARCHAR(30)    NOT NULL,
    USER_ID                      INT             NOT NULL,
    CONSTRAINT PK_EMAIL_OTPS PRIMARY KEY (EMAIL_OTP_ID),
    CONSTRAINT FK_EMAIL_OTPS_USERS_USER_ID FOREIGN KEY (USER_ID) REFERENCES USERS(USER_ID)
);
CREATE INDEX IX_EMAIL_OTPS_USER_ID_PURPOSE ON EMAIL_OTPS (USER_ID, PURPOSE);

CREATE TABLE SYSTEM_LOGS (
    SYSTEM_LOG_ID                INT             IDENTITY(1,1) NOT NULL,
    ACTION_TYPE                  NVARCHAR(20)    NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    DESCRIPTION                  NVARCHAR(500)   NOT NULL,
    ENTITY_ID                    INT             NULL,
    ENTITY_NAME                  NVARCHAR(50)    NOT NULL,
    USER_ID                      INT             NULL,
    USER_ROLE                    NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_SYSTEM_LOGS PRIMARY KEY (SYSTEM_LOG_ID)
);
CREATE INDEX IX_SYSTEM_LOGS_CREATED_AT ON SYSTEM_LOGS (CREATED_AT);
CREATE INDEX IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE ON SYSTEM_LOGS (ACTION_TYPE, USER_ROLE);

   Added by the DatabaseV2 migration (EF model, see ServisyoDbContext.cs):
CREATE INDEX IX_BOOKINGS_PROVIDER_STATUS ON BOOKINGS (PROVIDER_ID, STATUS);              -- provider "Booking Requests" lists
CREATE INDEX IX_BOOKINGS_DATE_PAYMENT ON BOOKINGS (BOOKING_DATE, PAYMENT_STATUS);        -- monthly report
CREATE INDEX IX_NOTIFICATIONS_USER_UNREAD ON NOTIFICATIONS (USER_ID, IS_READ);           -- bell count (replaces IX_NOTIFICATIONS_USER_ID)
CREATE INDEX IX_MESSAGES_RECEIVER_UNREAD ON MESSAGES (RECEIVER_ID, IS_READ);             -- unread badge (replaces IX_MESSAGES_RECEIVER_ID)
ALTER TABLE BOOKINGS ADD CONSTRAINT CK_BOOKINGS_FEE CHECK ([TOTAL_FEE] >= 0);
ALTER TABLE SERVICE_PROVIDERS ADD CONSTRAINT CK_PROVIDERS_COUNTS CHECK ([REVIEW_COUNT] >= 0 AND ([BASE_FEE] IS NULL OR [BASE_FEE] >= 0));
   ===================================================================================================== */

/* =====================================================================================================
   PART 2 - FUNCTIONS
   ===================================================================================================== */
-- fn_DayOfWeek
CREATE OR ALTER FUNCTION dbo.fn_DayOfWeek (@Date DATE)
RETURNS TINYINT
WITH SCHEMABINDING
AS
BEGIN
    /* SERVISYO-DB-V2
       The weekday number used by PROVIDER_AVAILABILITY: 0 = Sunday ... 6 = Saturday.
       It does not depend on SET DATEFIRST (2023-01-01 was a Sunday). */
    RETURN CAST(((DATEDIFF(DAY, CONVERT(DATE, '20230101', 112), @Date) % 7) + 7) % 7 AS TINYINT);
END
GO

-- fn_ManilaNow
CREATE OR ALTER FUNCTION dbo.fn_ManilaNow ()
RETURNS DATETIME2(0)
AS
BEGIN
    /* SERVISYO-DB-V2
       The current date and time in Cebu (UTC+8, no daylight saving). The tables store UTC times;
       booking dates and times are Cebu wall-clock values, so the booking rules compare against this. */
    RETURN CAST(DATEADD(HOUR, 8, SYSUTCDATETIME()) AS DATETIME2(0));
END
GO

-- fn_GetFullName
CREATE OR ALTER FUNCTION dbo.fn_GetFullName (@UserId INT)
RETURNS NVARCHAR(500)
AS
BEGIN
    /* SERVISYO-DB-V2
       "First Middle Last" (the middle name only when there is one). NULL when the user does not exist. */
    DECLARE @FullName NVARCHAR(500);

    SELECT @FullName = LTRIM(RTRIM(u.FIRST_NAME))
                     + ISNULL(N' ' + NULLIF(LTRIM(RTRIM(u.MIDDLE_NAME)), N''), N'')
                     + N' ' + LTRIM(RTRIM(u.LAST_NAME))
    FROM dbo.USERS u
    WHERE u.USER_ID = @UserId;

    RETURN @FullName;
END
GO

-- fn_GetProviderAvgRating
CREATE OR ALTER FUNCTION dbo.fn_GetProviderAvgRating (@ProviderId INT)
RETURNS DECIMAL(2,1)
AS
BEGIN
    /* SERVISYO-DB-V2
       Average of the provider's VISIBLE reviews, rounded to 1 decimal (half away from zero, like the web app).
       NULL when there is no visible review yet (the website then shows "New"). */
    DECLARE @Avg DECIMAL(2,1);

    SELECT @Avg = CAST(ROUND(AVG(CAST(r.RATING AS DECIMAL(9,4))), 1) AS DECIMAL(2,1))
    FROM dbo.REVIEWS r
    JOIN dbo.BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
    WHERE b.PROVIDER_ID = @ProviderId
      AND r.STATUS = N'Visible';

    RETURN @Avg;
END
GO

-- fn_CountProviderJobsOnDate
CREATE OR ALTER FUNCTION dbo.fn_CountProviderJobsOnDate (@ProviderId INT, @BookingDate DATE)
RETURNS INT
AS
BEGIN
    /* SERVISYO-DB-V2
       Active bookings (Pending, Accepted, In Progress) of a provider on one date: used for MAX_JOBS_PER_DAY. */
    DECLARE @Total INT;

    SELECT @Total = COUNT(*)
    FROM dbo.BOOKINGS b
    WHERE b.PROVIDER_ID = @ProviderId
      AND b.BOOKING_DATE = @BookingDate
      AND b.STATUS IN (N'Pending', N'Accepted', N'In Progress');

    RETURN @Total;
END
GO

-- fn_IsProviderAvailable
CREATE OR ALTER FUNCTION dbo.fn_IsProviderAvailable (@ProviderId INT, @BookingDate DATE, @StartTime TIME, @DurationMinutes INT)
RETURNS BIT
AS
BEGIN
    /* SERVISYO-DB-V2
       1 when the WHOLE job (start time + duration) fits the provider's working hours for that weekday,
       does not touch one of that day's breaks, and the date is not marked unavailable. Otherwise 0.
       (The PDF version only checked the start time.) Existing bookings are checked by fn_HasBookingConflict. */
    IF @StartTime IS NULL OR @DurationMinutes IS NULL OR @DurationMinutes <= 0 RETURN 0;

    DECLARE @Start INT = DATEDIFF(MINUTE, CAST('00:00' AS TIME), @StartTime);
    DECLARE @End INT = @Start + @DurationMinutes;
    DECLARE @AvailabilityId INT;

    IF EXISTS (SELECT 1 FROM dbo.PROVIDER_UNAVAILABLE_DATES u
               WHERE u.PROVIDER_ID = @ProviderId AND u.UNAVAILABLE_DATE = @BookingDate)
        RETURN 0;

    SELECT @AvailabilityId = a.AVAILABILITY_ID
    FROM dbo.PROVIDER_AVAILABILITY a
    WHERE a.PROVIDER_ID = @ProviderId
      AND a.DAY_OF_WEEK = dbo.fn_DayOfWeek(@BookingDate)
      AND a.IS_AVAILABLE = 1
      AND @Start >= DATEDIFF(MINUTE, CAST('00:00' AS TIME), a.START_TIME)
      AND @End <= DATEDIFF(MINUTE, CAST('00:00' AS TIME), a.END_TIME);

    IF @AvailabilityId IS NULL RETURN 0;

    IF EXISTS (SELECT 1 FROM dbo.PROVIDER_BREAKS br
               WHERE br.AVAILABILITY_ID = @AvailabilityId
                 AND @Start < DATEDIFF(MINUTE, CAST('00:00' AS TIME), br.BREAK_END)
                 AND DATEDIFF(MINUTE, CAST('00:00' AS TIME), br.BREAK_START) < @End)
        RETURN 0;

    RETURN 1;
END
GO

-- fn_HasBookingConflict
CREATE OR ALTER FUNCTION dbo.fn_HasBookingConflict (@ProviderId INT, @BookingDate DATE, @StartTime TIME, @DurationMinutes INT,
                                                    @ExcludeBookingId INT, @IncludePending BIT)
RETURNS BIT
AS
BEGIN
    /* SERVISYO-DB-V2
       1 when another booking of the provider on that date overlaps [start, start + duration).
       Counted bookings: Accepted and In Progress, plus Pending when @IncludePending = 1
       (a new request must not overlap pending ones; accepting only has to avoid confirmed jobs). */
    DECLARE @Start INT = DATEDIFF(MINUTE, CAST('00:00' AS TIME), @StartTime);
    DECLARE @End INT = @Start + ISNULL(@DurationMinutes, 0);

    IF EXISTS (SELECT 1
               FROM dbo.BOOKINGS b
               WHERE b.PROVIDER_ID = @ProviderId
                 AND b.BOOKING_DATE = @BookingDate
                 AND (@ExcludeBookingId IS NULL OR b.BOOKING_ID <> @ExcludeBookingId)
                 AND (b.STATUS IN (N'Accepted', N'In Progress') OR (@IncludePending = 1 AND b.STATUS = N'Pending'))
                 AND @Start < DATEDIFF(MINUTE, CAST('00:00' AS TIME), b.BOOKING_TIME) + b.TOTAL_DURATION_MINUTES
                 AND DATEDIFF(MINUTE, CAST('00:00' AS TIME), b.BOOKING_TIME) < @End)
        RETURN 1;

    RETURN 0;
END
GO

-- fn_GetAvailableSlots
CREATE OR ALTER FUNCTION dbo.fn_GetAvailableSlots (@ProviderId INT, @BookingDate DATE, @DurationMinutes INT)
RETURNS TABLE
AS
RETURN
(
    /* SERVISYO-DB-V2
       Every start time (every 30 minutes) at which a job of @DurationMinutes fits the provider's hours and breaks
       and does not overlap an active booking. Minimum notice and the daily job limit are checked by usp_CreateBooking. */
    SELECT CAST(DATEADD(MINUTE, s.M, CAST('00:00' AS TIME)) AS TIME(0)) AS SLOT_TIME
    FROM (SELECT TOP (48) (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1) * 30 AS M
          FROM sys.all_objects) s
    WHERE dbo.fn_IsProviderAvailable(@ProviderId, @BookingDate, DATEADD(MINUTE, s.M, CAST('00:00' AS TIME)), @DurationMinutes) = 1
      AND dbo.fn_HasBookingConflict(@ProviderId, @BookingDate, DATEADD(MINUTE, s.M, CAST('00:00' AS TIME)), @DurationMinutes, NULL, 1) = 0
);
GO

/* =====================================================================================================
   PART 3 - VIEWS
   ===================================================================================================== */
-- vw_BookingOverview
CREATE OR ALTER VIEW dbo.vw_BookingOverview
AS
/* SERVISYO-DB-V2
   One row per booking with the people, the services, the money and the payment, ready for reports and SSMS.
   Times are stored in UTC; CREATED_AT_CEBU shows them in Cebu time. */
SELECT b.BOOKING_ID,
       CONCAT(N'BK-', YEAR(b.CREATED_AT), N'-',
              CASE WHEN b.BOOKING_ID < 10000 THEN RIGHT(CONCAT(N'0000', b.BOOKING_ID), 4) ELSE CAST(b.BOOKING_ID AS NVARCHAR(12)) END) AS REFERENCE,
       b.STATUS,
       b.PAYMENT_STATUS,
       b.BOOKING_DATE,
       b.BOOKING_TIME,
       b.END_TIME,
       b.TOTAL_DURATION_MINUTES,
       b.TOTAL_FEE,
       c.CUSTOMER_ID,
       cu.USER_ID AS CUSTOMER_USER_ID,
       cu.FIRST_NAME + N' ' + cu.LAST_NAME AS CUSTOMER_NAME,
       p.PROVIDER_ID,
       p.USER_ID AS PROVIDER_USER_ID,
       p.BUSINESS_NAME,
       STUFF((SELECT N', ' + s.SERVICE_NAME
              FROM dbo.BOOKING_SERVICES bs
              JOIN dbo.SERVICES s ON s.SERVICE_ID = bs.SERVICE_ID
              WHERE bs.BOOKING_ID = b.BOOKING_ID
              ORDER BY bs.BOOKING_SERVICE_ID
              FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'') AS SERVICES,
       pay.PAYMENT_METHOD,
       pay.PAYMENT_STATUS AS PAYMENT_RECORD_STATUS,
       pay.TRANSACTION_REFERENCE,
       cash.IS_COLLECTED_BY_PROVIDER,
       b.SERVICE_LOCATION,
       DATEADD(HOUR, 8, b.CREATED_AT) AS CREATED_AT_CEBU
FROM dbo.BOOKINGS b
JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
JOIN dbo.USERS cu ON cu.USER_ID = c.USER_ID
JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = b.PROVIDER_ID
LEFT JOIN dbo.PAYMENTS pay ON pay.BOOKING_ID = b.BOOKING_ID
LEFT JOIN dbo.CASH_PAYMENT_DETAILS cash ON cash.PAYMENT_ID = pay.PAYMENT_ID;
GO

-- vw_ProviderDirectory
CREATE OR ALTER VIEW dbo.vw_ProviderDirectory
AS
/* SERVISYO-DB-V2
   Providers customers may see (Approved + Active account), with their rating and active services. */
SELECT p.PROVIDER_ID,
       p.BUSINESS_NAME,
       u.FIRST_NAME + N' ' + u.LAST_NAME AS OWNER_NAME,
       p.CITY,
       p.BASE_FEE,
       p.AVG_RATING,
       p.REVIEW_COUNT,
       p.MIN_NOTICE_DAYS,
       p.MAX_JOBS_PER_DAY,
       (SELECT COUNT(*) FROM dbo.SERVICES s
        JOIN dbo.SERVICE_CATEGORIES sc ON sc.CATEGORY_ID = s.CATEGORY_ID
        WHERE s.PROVIDER_ID = p.PROVIDER_ID AND s.IS_ACTIVE = 1 AND sc.IS_ACTIVE = 1) AS ACTIVE_SERVICES,
       (SELECT MIN(s.PRICE) FROM dbo.SERVICES s
        JOIN dbo.SERVICE_CATEGORIES sc ON sc.CATEGORY_ID = s.CATEGORY_ID
        WHERE s.PROVIDER_ID = p.PROVIDER_ID AND s.IS_ACTIVE = 1 AND sc.IS_ACTIVE = 1) AS STARTING_PRICE
FROM dbo.SERVICE_PROVIDERS p
JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
WHERE p.APPROVAL_STATUS = N'Approved'
  AND u.ACCOUNT_STATUS = N'Active';
GO

/* =====================================================================================================
   PART 4 - STORED PROCEDURES
   Every procedure that changes data: acting USER_ID -> ownership / role / status checked -> transaction.
   Errors use THROW with numbers 50101-50199 and messages written for users (the app shows them as they are).
   ===================================================================================================== */
-- usp_RegisterCustomer
CREATE OR ALTER PROCEDURE dbo.usp_RegisterCustomer
    @FirstName     NVARCHAR(400),
    @MiddleName    NVARCHAR(400) = NULL,
    @LastName      NVARCHAR(400),
    @Email         NVARCHAR(400),
    @PasswordHash  NVARCHAR(400),
    @PhoneNumber   NVARCHAR(50),
    @Address       NVARCHAR(400),
    @City          NVARCHAR(400) = NULL,
    @NewUserId     INT = NULL OUTPUT,
    @NewCustomerId INT = NULL OUTPUT
AS
BEGIN
    /* SERVISYO-DB-V2
       Customer sign-up in ONE call: checks the input and the email, then creates USERS + CUSTOMERS together
       (all or nothing). The web app hashes the password first: only the hash ever reaches the database.
       Used by the website (AuthService.RegisterCustomerAsync). */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- clean the text: trimmed, blank = NULL, email in lower case, phone without spaces / dashes / dots / brackets
    SET @FirstName    = NULLIF(LTRIM(RTRIM(@FirstName)), N'');
    SET @MiddleName   = NULLIF(LTRIM(RTRIM(@MiddleName)), N'');
    SET @LastName     = NULLIF(LTRIM(RTRIM(@LastName)), N'');
    SET @Email        = LOWER(NULLIF(LTRIM(RTRIM(@Email)), N''));
    SET @PasswordHash = NULLIF(LTRIM(RTRIM(@PasswordHash)), N'');
    SET @PhoneNumber  = NULLIF(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@PhoneNumber, N' ', N''), N'-', N''), N'.', N''), N'(', N''), N')', N''), CHAR(9), N''), N'');
    SET @Address      = NULLIF(LTRIM(RTRIM(@Address)), N'');
    SET @City         = NULLIF(LTRIM(RTRIM(@City)), N'');

    IF @FirstName IS NULL OR @LastName IS NULL THROW 50102, N'Please enter your first and last name.', 1;
    IF LEN(@FirstName) > 155 OR LEN(@LastName) > 155 OR LEN(ISNULL(@MiddleName, N'')) > 155 THROW 50102, N'Names can be at most 155 characters.', 1;
    IF @Email IS NULL OR LEN(@Email) > 155 OR @Email NOT LIKE N'_%@_%' OR @Email LIKE N'%@%@%' THROW 50103, N'Please enter a valid email address.', 1;
    IF @PasswordHash IS NULL OR LEN(@PasswordHash) < 20 OR LEN(@PasswordHash) > 255 THROW 50104, N'The password could not be saved. Please try again.', 1;
    IF @PhoneNumber IS NULL THROW 50105, N'Please enter your phone number.', 1;
    IF LEN(@PhoneNumber) > 15 OR @PhoneNumber LIKE N'_%[^0-9]%' OR @PhoneNumber LIKE N'[^+0-9]%' THROW 50105, N'Please enter a valid phone number.', 1;
    IF @Address IS NULL THROW 50106, N'Please enter your street address and barangay.', 1;
    IF LEN(@Address) > 255 OR LEN(ISNULL(@City, N'')) > 255 THROW 50106, N'The address or city is too long.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        -- UPDLOCK + HOLDLOCK: two sign-ups with the same email at the same moment wait for each other,
        -- so the second one sees the first and is refused (the unique index on EMAIL is the last guard)
        IF EXISTS (SELECT 1 FROM dbo.USERS WITH (UPDLOCK, HOLDLOCK) WHERE EMAIL = @Email)
            THROW 50101, N'That email address is already registered.', 1;

        INSERT INTO dbo.USERS (FIRST_NAME, MIDDLE_NAME, LAST_NAME, EMAIL, PASSWORD_HASH, PHONE_NUMBER,
                               ROLE, ACCOUNT_STATUS, CREATED_AT, EMAIL_VERIFICATION_PENDING)
        VALUES (@FirstName, @MiddleName, @LastName, @Email, @PasswordHash, @PhoneNumber,
                N'Customer', N'Active', SYSUTCDATETIME(), 0);
        SET @NewUserId = CAST(SCOPE_IDENTITY() AS INT);

        INSERT INTO dbo.CUSTOMERS (USER_ID, ADDRESS, CITY)
        VALUES (@NewUserId, @Address, @City);
        SET @NewCustomerId = CAST(SCOPE_IDENTITY() AS INT);

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_RegisterProvider
CREATE OR ALTER PROCEDURE dbo.usp_RegisterProvider
    @FirstName     NVARCHAR(400),
    @MiddleName    NVARCHAR(400),
    @LastName      NVARCHAR(400),
    @Email         NVARCHAR(400),
    @PasswordHash  NVARCHAR(400),
    @PhoneNumber   NVARCHAR(50),
    @BusinessName  NVARCHAR(400),
    @Description   NVARCHAR(400),
    @Address       NVARCHAR(400),
    @City          NVARCHAR(400) = NULL,
    @BaseFee       DECIMAL(12,2),
    @MinNoticeDays INT = 1,
    @MaxJobsPerDay INT = 5,
    @NewUserId     INT = NULL OUTPUT,
    @NewProviderId INT = NULL OUTPUT
AS
BEGIN
    /* SERVISYO-DB-V2
       Service-provider sign-up in ONE call: USERS + SERVICE_PROVIDERS (APPROVAL_STATUS = Pending until an
       administrator decides) + a bell notification for every active administrator, all or nothing.
       Used by the website (AuthService.RegisterProviderAsync). */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @FirstName    = NULLIF(LTRIM(RTRIM(@FirstName)), N'');
    SET @MiddleName   = NULLIF(LTRIM(RTRIM(@MiddleName)), N'');
    SET @LastName     = NULLIF(LTRIM(RTRIM(@LastName)), N'');
    SET @Email        = LOWER(NULLIF(LTRIM(RTRIM(@Email)), N''));
    SET @PasswordHash = NULLIF(LTRIM(RTRIM(@PasswordHash)), N'');
    SET @PhoneNumber  = NULLIF(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@PhoneNumber, N' ', N''), N'-', N''), N'.', N''), N'(', N''), N')', N''), CHAR(9), N''), N'');
    SET @BusinessName = NULLIF(LTRIM(RTRIM(@BusinessName)), N'');
    SET @Description  = NULLIF(LTRIM(RTRIM(@Description)), N'');
    SET @Address      = NULLIF(LTRIM(RTRIM(@Address)), N'');
    SET @City         = NULLIF(LTRIM(RTRIM(@City)), N'');

    IF @FirstName IS NULL OR @LastName IS NULL THROW 50102, N'Please enter your first and last name.', 1;
    IF @MiddleName IS NULL THROW 50102, N'Please enter your middle name.', 1;
    IF LEN(@FirstName) > 155 OR LEN(@LastName) > 155 OR LEN(@MiddleName) > 155 THROW 50102, N'Names can be at most 155 characters.', 1;
    IF @Email IS NULL OR LEN(@Email) > 155 OR @Email NOT LIKE N'_%@_%' OR @Email LIKE N'%@%@%' THROW 50103, N'Please enter a valid email address.', 1;
    IF @PasswordHash IS NULL OR LEN(@PasswordHash) < 20 OR LEN(@PasswordHash) > 255 THROW 50104, N'The password could not be saved. Please try again.', 1;
    IF @PhoneNumber IS NULL THROW 50105, N'Please enter your phone number.', 1;
    IF LEN(@PhoneNumber) > 15 OR @PhoneNumber LIKE N'_%[^0-9]%' OR @PhoneNumber LIKE N'[^+0-9]%' THROW 50105, N'Please enter a valid phone number.', 1;
    IF @BusinessName IS NULL THROW 50107, N'Please enter your business name.', 1;
    IF LEN(@BusinessName) > 155 THROW 50107, N'The business name can be at most 155 characters.', 1;
    IF @Description IS NULL THROW 50108, N'Please describe your services.', 1;
    IF LEN(@Description) > 255 THROW 50108, N'The description can be at most 255 characters.', 1;
    IF @Address IS NULL THROW 50106, N'Please enter your business address.', 1;
    IF LEN(@Address) > 255 OR LEN(ISNULL(@City, N'')) > 50 THROW 50106, N'The address (255) or city (50 characters) is too long.', 1;
    IF @BaseFee IS NULL THROW 50109, N'Please enter your base fee.', 1;
    IF @BaseFee < 0 OR @BaseFee > 1000000 THROW 50109, N'Enter a base fee from 0 to 1,000,000.', 1;
    IF @MinNoticeDays IS NULL OR @MinNoticeDays < 0 OR @MinNoticeDays > 30 THROW 50110, N'Enter a whole number of notice days from 0 to 30.', 1;
    IF @MaxJobsPerDay IS NULL OR @MaxJobsPerDay < 1 OR @MaxJobsPerDay > 50 THROW 50110, N'Enter a whole number of jobs per day from 1 to 50.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM dbo.USERS WITH (UPDLOCK, HOLDLOCK) WHERE EMAIL = @Email)
            THROW 50101, N'That email address is already registered.', 1;

        INSERT INTO dbo.USERS (FIRST_NAME, MIDDLE_NAME, LAST_NAME, EMAIL, PASSWORD_HASH, PHONE_NUMBER,
                               ROLE, ACCOUNT_STATUS, CREATED_AT, EMAIL_VERIFICATION_PENDING)
        VALUES (@FirstName, @MiddleName, @LastName, @Email, @PasswordHash, @PhoneNumber,
                N'Service Provider', N'Active', SYSUTCDATETIME(), 0);
        SET @NewUserId = CAST(SCOPE_IDENTITY() AS INT);

        INSERT INTO dbo.SERVICE_PROVIDERS (USER_ID, BUSINESS_NAME, DESCRIPTION, ADDRESS, CITY, BASE_FEE,
                                           AVG_RATING, REVIEW_COUNT, MIN_NOTICE_DAYS, MAX_JOBS_PER_DAY,
                                           APPROVAL_STATUS, DATE_APPLIED, DATE_APPROVED)
        VALUES (@NewUserId, @BusinessName, @Description, @Address, @City, @BaseFee,
                NULL, 0, @MinNoticeDays, @MaxJobsPerDay,
                N'Pending', SYSUTCDATETIME(), NULL);
        SET @NewProviderId = CAST(SCOPE_IDENTITY() AS INT);

        -- tell every active administrator (their bell)
        DECLARE @Note NVARCHAR(700) = CONCAT(N'New provider application from ', @BusinessName, N' (', @FirstName, N' ', @LastName, N').');
        IF LEN(@Note) > 255 SET @Note = LEFT(@Note, 252) + N'...';

        INSERT INTO dbo.NOTIFICATIONS (USER_ID, MESSAGE, IS_READ, CREATED_AT)
        SELECT u.USER_ID, @Note, 0, SYSUTCDATETIME()
        FROM dbo.USERS u
        WHERE u.ROLE = N'Administrator' AND u.ACCOUNT_STATUS = N'Active';

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_GetUserForLogin
CREATE OR ALTER PROCEDURE dbo.usp_GetUserForLogin
    @Email NVARCHAR(400)
AS
BEGIN
    /* SERVISYO-DB-V2
       The account with this email, so the application can compare the password hash (the database never
       sees or compares passwords). Returns no row when the email is not registered. */
    SET NOCOUNT ON;

    SELECT u.USER_ID,
           dbo.fn_GetFullName(u.USER_ID) AS FULL_NAME,
           u.EMAIL,
           u.PASSWORD_HASH,
           u.ROLE,
           u.ACCOUNT_STATUS,
           u.EMAIL_VERIFICATION_PENDING
    FROM dbo.USERS u
    WHERE u.EMAIL = LOWER(LTRIM(RTRIM(@Email)));
END
GO

-- usp_ApproveProvider
CREATE OR ALTER PROCEDURE dbo.usp_ApproveProvider
    @AdminUserId INT,
    @ProviderId  INT,
    @Decision    NVARCHAR(50),
    @Reason      NVARCHAR(400) = NULL
AS
BEGIN
    /* SERVISYO-DB-V2
       An ACTIVE administrator approves or rejects a PENDING provider application:
       status (+ DATE_APPROVED), ADMIN_ACTIONS log and a notification for the provider, all or nothing.
       If two administrators decide at the same moment, only the first one changes anything. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Decision = LTRIM(RTRIM(@Decision));
    SET @Reason = NULLIF(LTRIM(RTRIM(@Reason)), N'');

    IF NOT EXISTS (SELECT 1 FROM dbo.USERS WHERE USER_ID = @AdminUserId AND ROLE = N'Administrator' AND ACCOUNT_STATUS = N'Active')
        THROW 50111, N'Only an active administrator can approve or reject providers.', 1;
    IF @Decision IS NULL OR @Decision NOT IN (N'Approved', N'Rejected')
        THROW 50112, N'The decision must be Approved or Rejected.', 1;
    IF LEN(ISNULL(@Reason, N'')) > 255
        THROW 50112, N'The note can be at most 255 characters.', 1;

    DECLARE @ProviderUserId INT = (SELECT USER_ID FROM dbo.SERVICE_PROVIDERS WHERE PROVIDER_ID = @ProviderId);
    IF @ProviderUserId IS NULL
        THROW 50113, N'That application no longer exists.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        UPDATE dbo.SERVICE_PROVIDERS
        SET APPROVAL_STATUS = @Decision,
            DATE_APPROVED = CASE WHEN @Decision = N'Approved' THEN SYSUTCDATETIME() ELSE NULL END
        WHERE PROVIDER_ID = @ProviderId
          AND APPROVAL_STATUS = N'Pending';

        IF @@ROWCOUNT = 0
            THROW 50114, N'That application has already been decided, so nothing was changed.', 1;

        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_ID, REASON, CREATED_AT)
        VALUES (@AdminUserId, @Decision, N'SERVICE_PROVIDER', @ProviderId, @Reason, SYSUTCDATETIME());

        INSERT INTO dbo.NOTIFICATIONS (USER_ID, MESSAGE, IS_READ, CREATED_AT)
        VALUES (@ProviderUserId, N'Your provider application was ' + @Decision + N'.', 0, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_UpdateAccountStatus
CREATE OR ALTER PROCEDURE dbo.usp_UpdateAccountStatus
    @AdminUserId  INT,
    @TargetUserId INT,
    @NewStatus    NVARCHAR(50),
    @Reason       NVARCHAR(400) = NULL
AS
BEGIN
    /* SERVISYO-DB-V2
       An ACTIVE administrator suspends, deactivates or re-activates a customer or provider account.
       Allowed changes (same as the website): Active -> Suspended | Deactivated, Suspended -> Active | Deactivated,
       Deactivated -> Active. Administrators cannot be changed here (and nobody can change their own account). */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @NewStatus = LTRIM(RTRIM(@NewStatus));
    SET @Reason = NULLIF(LTRIM(RTRIM(@Reason)), N'');

    IF NOT EXISTS (SELECT 1 FROM dbo.USERS WHERE USER_ID = @AdminUserId AND ROLE = N'Administrator' AND ACCOUNT_STATUS = N'Active')
        THROW 50111, N'Only an active administrator can change account status.', 1;
    IF @TargetUserId = @AdminUserId
        THROW 50115, N'You cannot change the status of your own account.', 1;
    IF LEN(ISNULL(@Reason, N'')) > 255
        THROW 50112, N'The reason can be at most 255 characters.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        DECLARE @Role NVARCHAR(50), @Current NVARCHAR(50);
        SELECT @Role = ROLE, @Current = ACCOUNT_STATUS
        FROM dbo.USERS WITH (UPDLOCK)
        WHERE USER_ID = @TargetUserId;

        IF @Role IS NULL OR @Role = N'Administrator'
            THROW 50116, N'That customer or provider account was not found.', 1;

        IF NOT (   (@Current = N'Active'      AND @NewStatus IN (N'Suspended', N'Deactivated'))
                OR (@Current = N'Suspended'   AND @NewStatus IN (N'Active', N'Deactivated'))
                OR (@Current = N'Deactivated' AND @NewStatus = N'Active'))
            THROW 50117, N'That status change is not allowed from the account''s current status.', 1;

        UPDATE dbo.USERS SET ACCOUNT_STATUS = @NewStatus WHERE USER_ID = @TargetUserId;

        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_ID, REASON, CREATED_AT)
        VALUES (@AdminUserId, N'Account ' + @NewStatus, N'USER', @TargetUserId, @Reason, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_ModerateReview
CREATE OR ALTER PROCEDURE dbo.usp_ModerateReview
    @AdminUserId INT,
    @ReviewId    INT,
    @NewStatus   NVARCHAR(50),
    @Reason      NVARCHAR(400) = NULL
AS
BEGIN
    /* SERVISYO-DB-V2
       An ACTIVE administrator keeps, hides or restores a review (same moves as the website):
       Flagged -> Visible (keep), Visible / Flagged -> Removed (hide), Removed -> Visible (restore).
       The provider's AVG_RATING and REVIEW_COUNT are updated by the trigger trg_Reviews_RatingSync. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @NewStatus = LTRIM(RTRIM(@NewStatus));
    SET @Reason = NULLIF(LTRIM(RTRIM(@Reason)), N'');

    IF NOT EXISTS (SELECT 1 FROM dbo.USERS WHERE USER_ID = @AdminUserId AND ROLE = N'Administrator' AND ACCOUNT_STATUS = N'Active')
        THROW 50111, N'Only an active administrator can moderate reviews.', 1;
    IF @NewStatus IS NULL OR @NewStatus NOT IN (N'Visible', N'Removed')
        THROW 50118, N'The new review status must be Visible or Removed.', 1;
    IF LEN(ISNULL(@Reason, N'')) > 255
        THROW 50112, N'The reason can be at most 255 characters.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        DECLARE @Current NVARCHAR(50) = (SELECT STATUS FROM dbo.REVIEWS WITH (UPDLOCK) WHERE REVIEW_ID = @ReviewId);
        IF @Current IS NULL
            THROW 50119, N'That review no longer exists.', 1;

        IF NOT (   (@NewStatus = N'Visible' AND @Current IN (N'Flagged', N'Removed'))
                OR (@NewStatus = N'Removed' AND @Current IN (N'Visible', N'Flagged')))
            THROW 50120, N'The review already has that status, so nothing was changed.', 1;

        UPDATE dbo.REVIEWS SET STATUS = @NewStatus WHERE REVIEW_ID = @ReviewId;

        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_ID, REASON, CREATED_AT)
        VALUES (@AdminUserId, N'Review ' + @NewStatus, N'REVIEW', @ReviewId, @Reason, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_AddService
CREATE OR ALTER PROCEDURE dbo.usp_AddService
    @ProviderUserId  INT,
    @CategoryId      INT,
    @ServiceName     NVARCHAR(400),
    @Description     NVARCHAR(400) = NULL,
    @Price           DECIMAL(12,2),
    @DurationMinutes INT = 60,
    @NewServiceId    INT = NULL OUTPUT
AS
BEGIN
    /* SERVISYO-DB-V2
       An Active, Approved provider (found from their USER_ID, never from a provider id typed by someone else)
       adds a service in an active category, and is linked to that category if not yet (PROVIDER_CATEGORIES).
       Same rules as the website: name 1-100 characters and not used twice by the same provider, price 0 or more
       with at most 2 decimals, duration one of 30, 60, 90, 120, 180, 240, 300, 360, 480 minutes. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ServiceName = NULLIF(LTRIM(RTRIM(@ServiceName)), N'');
    SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');

    DECLARE @ProviderId INT = (SELECT p.PROVIDER_ID
                               FROM dbo.SERVICE_PROVIDERS p
                               JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
                               WHERE p.USER_ID = @ProviderUserId
                                 AND u.ROLE = N'Service Provider'
                                 AND u.ACCOUNT_STATUS = N'Active'
                                 AND p.APPROVAL_STATUS = N'Approved');
    IF @ProviderId IS NULL
        THROW 50121, N'Only an active, approved service provider can add services.', 1;

    IF @ServiceName IS NULL THROW 50122, N'Enter a service name.', 1;
    IF LEN(@ServiceName) > 100 THROW 50122, N'The service name can be at most 100 characters.', 1;
    IF LEN(ISNULL(@Description, N'')) > 255 THROW 50122, N'The description can be at most 255 characters.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.SERVICE_CATEGORIES WHERE CATEGORY_ID = @CategoryId AND IS_ACTIVE = 1)
        THROW 50123, N'That category does not exist or is not active.', 1;
    IF @Price IS NULL OR @Price < 0 OR @Price > 99999999.99
        THROW 50124, N'Enter a price from 0 to 99,999,999.99.', 1;
    IF @DurationMinutes IS NULL OR @DurationMinutes NOT IN (30, 60, 90, 120, 180, 240, 300, 360, 480)
        THROW 50125, N'Choose a duration of 30, 60, 90, 120, 180, 240, 300, 360 or 480 minutes.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM dbo.SERVICES WITH (UPDLOCK, HOLDLOCK)
                   WHERE PROVIDER_ID = @ProviderId AND LOWER(SERVICE_NAME) = LOWER(@ServiceName))
            THROW 50126, N'You already have a service with this name.', 1;

        INSERT INTO dbo.SERVICES (PROVIDER_ID, CATEGORY_ID, SERVICE_NAME, DESCRIPTION, PRICE, DURATION_MINUTES, IS_ACTIVE)
        VALUES (@ProviderId, @CategoryId, @ServiceName, @Description, @Price, @DurationMinutes, 1);
        SET @NewServiceId = CAST(SCOPE_IDENTITY() AS INT);

        IF NOT EXISTS (SELECT 1 FROM dbo.PROVIDER_CATEGORIES WITH (UPDLOCK, HOLDLOCK)
                       WHERE PROVIDER_ID = @ProviderId AND CATEGORY_ID = @CategoryId)
            INSERT INTO dbo.PROVIDER_CATEGORIES (PROVIDER_ID, CATEGORY_ID) VALUES (@ProviderId, @CategoryId);

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_SearchServices
CREATE OR ALTER PROCEDURE dbo.usp_SearchServices
    @Keyword    NVARCHAR(100) = NULL,
    @CategoryId INT = NULL
AS
BEGIN
    /* SERVISYO-DB-V2
       Customer search: ACTIVE services in ACTIVE categories of providers customers may see (Approved + Active account),
       optionally filtered by a keyword (service, business or category name) and/or a category.
       Faster than the PDF version: it reads the stored AVG_RATING instead of recalculating it for every row,
       and the keyword's % and _ are matched literally. */
    SET NOCOUNT ON;

    DECLARE @Pattern NVARCHAR(400) = NULLIF(LTRIM(RTRIM(@Keyword)), N'');
    IF @Pattern IS NOT NULL
        SET @Pattern = N'%' + REPLACE(REPLACE(REPLACE(@Pattern, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%';

    SELECT s.SERVICE_ID,
           s.SERVICE_NAME,
           s.DESCRIPTION,
           s.PRICE,
           s.DURATION_MINUTES,
           c.CATEGORY_ID,
           c.CATEGORY_NAME,
           p.PROVIDER_ID,
           p.BUSINESS_NAME,
           p.CITY,
           p.AVG_RATING,
           p.REVIEW_COUNT
    FROM dbo.SERVICES s
    JOIN dbo.SERVICE_CATEGORIES c ON c.CATEGORY_ID = s.CATEGORY_ID
    JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = s.PROVIDER_ID
    JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
    WHERE s.IS_ACTIVE = 1
      AND c.IS_ACTIVE = 1
      AND p.APPROVAL_STATUS = N'Approved'
      AND u.ACCOUNT_STATUS = N'Active'
      AND (@CategoryId IS NULL OR s.CATEGORY_ID = @CategoryId)
      AND (@Pattern IS NULL
           OR s.SERVICE_NAME LIKE @Pattern
           OR p.BUSINESS_NAME LIKE @Pattern
           OR c.CATEGORY_NAME LIKE @Pattern)
    ORDER BY ISNULL(p.AVG_RATING, 0) DESC, p.REVIEW_COUNT DESC, s.PRICE ASC, s.SERVICE_NAME;
END
GO

-- usp_GetProviderAvailability
CREATE OR ALTER PROCEDURE dbo.usp_GetProviderAvailability
    @ProviderId INT
AS
BEGIN
    /* SERVISYO-DB-V2
       A provider's weekly schedule (Sunday first) and, as a second result, the breaks of each day. */
    SET NOCOUNT ON;

    SELECT a.AVAILABILITY_ID,
           a.DAY_OF_WEEK,
           CHOOSE(a.DAY_OF_WEEK + 1, N'Sunday', N'Monday', N'Tuesday', N'Wednesday', N'Thursday', N'Friday', N'Saturday') AS DAY_NAME,
           a.START_TIME,
           a.END_TIME,
           a.IS_AVAILABLE
    FROM dbo.PROVIDER_AVAILABILITY a
    WHERE a.PROVIDER_ID = @ProviderId
    ORDER BY a.DAY_OF_WEEK;

    SELECT br.BREAK_ID,
           br.AVAILABILITY_ID,
           a.DAY_OF_WEEK,
           br.BREAK_START,
           br.BREAK_END
    FROM dbo.PROVIDER_BREAKS br
    JOIN dbo.PROVIDER_AVAILABILITY a ON a.AVAILABILITY_ID = br.AVAILABILITY_ID
    WHERE a.PROVIDER_ID = @ProviderId
    ORDER BY a.DAY_OF_WEEK, br.BREAK_START;
END
GO

-- usp_AddAvailability
CREATE OR ALTER PROCEDURE dbo.usp_AddAvailability
    @ProviderUserId    INT,
    @DayOfWeek         TINYINT,
    @StartTime         TIME(0),
    @EndTime           TIME(0),
    @NewAvailabilityId INT = NULL OUTPUT
AS
BEGIN
    /* SERVISYO-DB-V2
       The provider (found from their USER_ID) adds the working hours of one weekday (0 = Sunday ... 6 = Saturday).
       One schedule per day: use usp_UpdateAvailability to change an existing day (usp_GetProviderAvailability lists them). */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ProviderId INT = (SELECT p.PROVIDER_ID
                               FROM dbo.SERVICE_PROVIDERS p
                               JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
                               WHERE p.USER_ID = @ProviderUserId
                                 AND u.ROLE = N'Service Provider'
                                 AND u.ACCOUNT_STATUS = N'Active'
                                 AND p.APPROVAL_STATUS = N'Approved');
    IF @ProviderId IS NULL
        THROW 50121, N'Only an active, approved service provider can manage availability.', 1;
    IF @DayOfWeek IS NULL OR @DayOfWeek > 6
        THROW 50127, N'The day must be 0 (Sunday) to 6 (Saturday).', 1;
    IF @StartTime IS NULL OR @EndTime IS NULL OR @EndTime <= @StartTime
        THROW 50128, N'The end time must be later than the start time.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM dbo.PROVIDER_AVAILABILITY WITH (UPDLOCK, HOLDLOCK)
                   WHERE PROVIDER_ID = @ProviderId AND DAY_OF_WEEK = @DayOfWeek)
            THROW 50129, N'A schedule for that day already exists. Use usp_UpdateAvailability instead.', 1;

        INSERT INTO dbo.PROVIDER_AVAILABILITY (PROVIDER_ID, DAY_OF_WEEK, IS_AVAILABLE, START_TIME, END_TIME)
        VALUES (@ProviderId, @DayOfWeek, 1, @StartTime, @EndTime);
        SET @NewAvailabilityId = CAST(SCOPE_IDENTITY() AS INT);

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_UpdateAvailability
CREATE OR ALTER PROCEDURE dbo.usp_UpdateAvailability
    @ProviderUserId INT,
    @AvailabilityId INT,
    @StartTime      TIME(0),
    @EndTime        TIME(0),
    @IsAvailable    BIT
AS
BEGIN
    /* SERVISYO-DB-V2
       The provider changes the hours of one of THEIR OWN weekdays (or switches the day off / on).
       The day's breaks must still fit inside the new hours. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @StartTime IS NULL OR @EndTime IS NULL OR @EndTime <= @StartTime
        THROW 50128, N'The end time must be later than the start time.', 1;
    IF @IsAvailable IS NULL
        THROW 50128, N'Say whether the day is available (1) or not (0).', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1
                       FROM dbo.PROVIDER_AVAILABILITY a WITH (UPDLOCK)
                       JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = a.PROVIDER_ID
                       JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
                       WHERE a.AVAILABILITY_ID = @AvailabilityId
                         AND p.USER_ID = @ProviderUserId
                         AND u.ACCOUNT_STATUS = N'Active'
                         AND p.APPROVAL_STATUS = N'Approved')
            THROW 50130, N'That schedule was not found.', 1;

        IF EXISTS (SELECT 1 FROM dbo.PROVIDER_BREAKS
                   WHERE AVAILABILITY_ID = @AvailabilityId
                     AND (BREAK_START < @StartTime OR BREAK_END > @EndTime))
            THROW 50131, N'A break of this day falls outside the new hours. Change or remove the break first.', 1;

        UPDATE dbo.PROVIDER_AVAILABILITY
        SET START_TIME = @StartTime,
            END_TIME = @EndTime,
            IS_AVAILABLE = @IsAvailable
        WHERE AVAILABILITY_ID = @AvailabilityId;

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_DeleteAvailability
CREATE OR ALTER PROCEDURE dbo.usp_DeleteAvailability
    @ProviderUserId INT,
    @AvailabilityId INT
AS
BEGIN
    /* SERVISYO-DB-V2
       The provider removes one of THEIR OWN weekdays together with its breaks. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1
                       FROM dbo.PROVIDER_AVAILABILITY a WITH (UPDLOCK)
                       JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = a.PROVIDER_ID
                       JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
                       WHERE a.AVAILABILITY_ID = @AvailabilityId
                         AND p.USER_ID = @ProviderUserId
                         AND u.ACCOUNT_STATUS = N'Active'
                         AND p.APPROVAL_STATUS = N'Approved')
            THROW 50130, N'That schedule was not found.', 1;

        DELETE FROM dbo.PROVIDER_BREAKS WHERE AVAILABILITY_ID = @AvailabilityId;
        DELETE FROM dbo.PROVIDER_AVAILABILITY WHERE AVAILABILITY_ID = @AvailabilityId;

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_CreateBooking
CREATE OR ALTER PROCEDURE dbo.usp_CreateBooking
    @CustomerUserId     INT,
    @ProviderId         INT,
    @ServiceIds         NVARCHAR(400),          -- one or more SERVICE_IDs of that provider, e.g. N'3' or N'3,5'
    @BookingDate        DATE,
    @BookingTime        TIME(0),
    @ServiceLocation    NVARCHAR(400),
    @ServiceDescription NVARCHAR(400),
    @NewBookingId       INT = NULL OUTPUT
AS
BEGIN
    /* SERVISYO-DB-V2
       An Active customer (found from their USER_ID) requests one or more services of ONE provider.
       Same rules as the website: the provider is Approved + Active with active services; 1 to 10 services of that
       provider in active categories; minimum notice days; at most 90 days ahead; not a day off; the whole job fits the
       working hours and breaks; starts on a 30-minute step and not in the past; no overlap with another active booking;
       daily job limit. The provider row is locked first, so two requests for the same provider are checked one after
       the other (no double booking). New bookings start as Pending / Unpaid; the provider gets a notification. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ServiceLocation = NULLIF(LTRIM(RTRIM(@ServiceLocation)), N'');
    SET @ServiceDescription = NULLIF(LTRIM(RTRIM(@ServiceDescription)), N'');
    SET @ServiceIds = REPLACE(NULLIF(LTRIM(RTRIM(@ServiceIds)), N''), N' ', N'');

    DECLARE @CustomerId INT = (SELECT c.CUSTOMER_ID
                               FROM dbo.CUSTOMERS c
                               JOIN dbo.USERS u ON u.USER_ID = c.USER_ID
                               WHERE c.USER_ID = @CustomerUserId
                                 AND u.ROLE = N'Customer'
                                 AND u.ACCOUNT_STATUS = N'Active');
    IF @CustomerId IS NULL
        THROW 50132, N'Only an active customer account can book a service.', 1;

    IF @ServiceIds IS NULL OR @ServiceIds LIKE N'%[^0-9,]%' OR @ServiceIds LIKE N'%,,%' OR @ServiceIds LIKE N',%' OR @ServiceIds LIKE N'%,'
        THROW 50133, N'Choose at least one service (service ids separated by commas, e.g. 3,5).', 1;
    IF @ServiceLocation IS NULL OR LEN(@ServiceLocation) > 255
        THROW 50134, N'Enter the address where the service will be done (at most 255 characters).', 1;
    IF @ServiceDescription IS NULL OR LEN(@ServiceDescription) > 255
        THROW 50134, N'Describe what you need done (at most 255 characters).', 1;
    IF @BookingDate IS NULL OR @BookingTime IS NULL
        THROW 50135, N'Choose a date and a time.', 1;

    -- the chosen services (no duplicates); every one must be an ACTIVE service of THIS provider in an ACTIVE category
    DECLARE @Chosen TABLE (SERVICE_ID INT PRIMARY KEY, PRICE DECIMAL(10,2) NOT NULL, DURATION_MINUTES INT NOT NULL);
    DECLARE @Wanted TABLE (SERVICE_ID INT PRIMARY KEY);
    DECLARE @Xml XML = CAST(N'<i>' + REPLACE(@ServiceIds, N',', N'</i><i>') + N'</i>' AS XML);

    INSERT INTO @Wanted (SERVICE_ID)
    SELECT DISTINCT x.v.value('.', 'INT')
    FROM @Xml.nodes('/i') AS x(v);

    IF (SELECT COUNT(*) FROM @Wanted) > 10
        THROW 50133, N'You can book at most 10 services at once.', 1;

    INSERT INTO @Chosen (SERVICE_ID, PRICE, DURATION_MINUTES)
    SELECT s.SERVICE_ID, s.PRICE, s.DURATION_MINUTES
    FROM @Wanted w
    JOIN dbo.SERVICES s ON s.SERVICE_ID = w.SERVICE_ID
    JOIN dbo.SERVICE_CATEGORIES sc ON sc.CATEGORY_ID = s.CATEGORY_ID
    WHERE s.PROVIDER_ID = @ProviderId AND s.IS_ACTIVE = 1 AND sc.IS_ACTIVE = 1;

    IF (SELECT COUNT(*) FROM @Chosen) <> (SELECT COUNT(*) FROM @Wanted)
        THROW 50133, N'One of the selected services is no longer available. Please review your choices.', 1;

    DECLARE @Duration INT = (SELECT SUM(DURATION_MINUTES) FROM @Chosen);
    DECLARE @Total DECIMAL(10,2) = (SELECT SUM(PRICE) FROM @Chosen);
    DECLARE @Now DATETIME2(0) = dbo.fn_ManilaNow();
    DECLARE @Today DATE = CAST(@Now AS DATE);
    DECLARE @StartMinute INT = DATEDIFF(MINUTE, CAST('00:00' AS TIME), @BookingTime);

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        -- lock this provider: bookings for the same provider are checked one at a time
        DECLARE @ProviderUserId INT, @MinNotice INT, @MaxJobs INT;
        SELECT @ProviderUserId = p.USER_ID, @MinNotice = p.MIN_NOTICE_DAYS, @MaxJobs = p.MAX_JOBS_PER_DAY
        FROM dbo.SERVICE_PROVIDERS p WITH (UPDLOCK, HOLDLOCK)
        JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
        WHERE p.PROVIDER_ID = @ProviderId
          AND p.APPROVAL_STATUS = N'Approved'
          AND u.ACCOUNT_STATUS = N'Active';

        IF @ProviderUserId IS NULL
            THROW 50136, N'This provider is not taking bookings right now.', 1;

        IF @BookingDate < DATEADD(DAY, CASE WHEN @MinNotice > 0 THEN @MinNotice ELSE 0 END, @Today)
            THROW 50137, N'That date is too soon for this provider''s minimum notice (or it has passed). Please pick a later date.', 1;
        IF @BookingDate > DATEADD(DAY, 90, @Today)
            THROW 50137, N'Bookings can only be made up to 90 days ahead.', 1;
        IF EXISTS (SELECT 1 FROM dbo.PROVIDER_UNAVAILABLE_DATES WHERE PROVIDER_ID = @ProviderId AND UNAVAILABLE_DATE = @BookingDate)
            THROW 50137, N'The provider is not available on that date.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.PROVIDER_AVAILABILITY
                       WHERE PROVIDER_ID = @ProviderId AND DAY_OF_WEEK = dbo.fn_DayOfWeek(@BookingDate) AND IS_AVAILABLE = 1)
            THROW 50137, N'The provider does not work on that day of the week.', 1;
        IF dbo.fn_CountProviderJobsOnDate(@ProviderId, @BookingDate) >= @MaxJobs
            THROW 50137, N'The provider is fully booked on that date.', 1;
        IF @BookingDate = @Today AND @BookingTime <= CAST(@Now AS TIME(0))
            THROW 50138, N'That time has already passed. Please pick a later time.', 1;
        IF @StartMinute % 30 <> 0
            THROW 50138, N'Start times are every 30 minutes (for example 9:00 or 9:30).', 1;
        IF dbo.fn_IsProviderAvailable(@ProviderId, @BookingDate, @BookingTime, @Duration) = 0
            THROW 50138, N'That time does not fit the provider''s working hours and breaks for the services you chose.', 1;
        IF dbo.fn_HasBookingConflict(@ProviderId, @BookingDate, @BookingTime, @Duration, NULL, 1) = 1
            THROW 50138, N'That time is already taken. Please choose another.', 1;

        INSERT INTO dbo.BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES,
                                  SERVICE_LOCATION, SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT, UPDATED_AT)
        VALUES (@CustomerId, @ProviderId, @BookingDate, @BookingTime, @Duration,
                @ServiceLocation, @ServiceDescription, @Total, N'Pending', N'Unpaid', SYSUTCDATETIME(), SYSUTCDATETIME());
        SET @NewBookingId = CAST(SCOPE_IDENTITY() AS INT);

        INSERT INTO dbo.BOOKING_SERVICES (BOOKING_ID, SERVICE_ID, PRICE_SNAPSHOT, DURATION_MINUTES_SNAPSHOT)
        SELECT @NewBookingId, ch.SERVICE_ID, ch.PRICE, ch.DURATION_MINUTES
        FROM @Chosen ch;

        INSERT INTO dbo.NOTIFICATIONS (USER_ID, MESSAGE, IS_READ, CREATED_AT)
        VALUES (@ProviderUserId, CONCAT(N'You have a new booking request (Booking #', @NewBookingId, N').'), 0, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_CancelBooking
CREATE OR ALTER PROCEDURE dbo.usp_CancelBooking
    @CustomerUserId INT,
    @BookingId      INT
AS
BEGIN
    /* SERVISYO-DB-V2
       A customer cancels one of THEIR OWN bookings, only while it is Pending (project rule). The provider is told. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        DECLARE @Status NVARCHAR(50), @ProviderUserId INT;
        SELECT @Status = b.STATUS, @ProviderUserId = p.USER_ID
        FROM dbo.BOOKINGS b WITH (UPDLOCK)
        JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
        JOIN dbo.USERS u ON u.USER_ID = c.USER_ID
        JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = b.PROVIDER_ID
        WHERE b.BOOKING_ID = @BookingId
          AND c.USER_ID = @CustomerUserId
          AND u.ACCOUNT_STATUS = N'Active';

        IF @Status IS NULL
            THROW 50139, N'That booking could not be found.', 1;
        IF @Status <> N'Pending'
            THROW 50142, N'Only a Pending booking can be cancelled by the customer.', 1;

        UPDATE dbo.BOOKINGS
        SET STATUS = N'Cancelled', UPDATED_AT = SYSUTCDATETIME()
        WHERE BOOKING_ID = @BookingId;

        INSERT INTO dbo.NOTIFICATIONS (USER_ID, MESSAGE, IS_READ, CREATED_AT)
        VALUES (@ProviderUserId, CONCAT(N'Booking #', @BookingId, N' was cancelled by the customer.'), 0, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_UpdateBookingStatus
CREATE OR ALTER PROCEDURE dbo.usp_UpdateBookingStatus
    @ProviderUserId INT,
    @BookingId      INT,
    @NewStatus      NVARCHAR(50)
AS
BEGIN
    /* SERVISYO-DB-V2
       The ASSIGNED provider (found from their USER_ID) moves one of their bookings:
       Pending -> Accepted | Rejected, Accepted -> In Progress (only after the customer paid or chose cash on
       completion), In Progress -> Completed. Accepting must not overlap another Accepted / In Progress job.
       The customer gets the PDF's notification "Your booking #N is now <status>.". Customers cancel with usp_CancelBooking. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @NewStatus = LTRIM(RTRIM(@NewStatus));

    DECLARE @ProviderId INT = (SELECT p.PROVIDER_ID
                               FROM dbo.SERVICE_PROVIDERS p
                               JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
                               WHERE p.USER_ID = @ProviderUserId
                                 AND u.ROLE = N'Service Provider'
                                 AND u.ACCOUNT_STATUS = N'Active'
                                 AND p.APPROVAL_STATUS = N'Approved');
    IF @ProviderId IS NULL
        THROW 50121, N'Only an active, approved service provider can update bookings.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        -- accepting: one provider's accepts are checked one at a time
        IF @NewStatus = N'Accepted'
            SELECT @ProviderId = PROVIDER_ID FROM dbo.SERVICE_PROVIDERS WITH (UPDLOCK, HOLDLOCK) WHERE PROVIDER_ID = @ProviderId;

        DECLARE @Current NVARCHAR(50), @Payment NVARCHAR(50), @CustomerUserId INT,
                @Date DATE, @Time TIME(0), @Minutes INT;
        SELECT @Current = b.STATUS, @Payment = b.PAYMENT_STATUS, @CustomerUserId = c.USER_ID,
               @Date = b.BOOKING_DATE, @Time = b.BOOKING_TIME, @Minutes = b.TOTAL_DURATION_MINUTES
        FROM dbo.BOOKINGS b WITH (UPDLOCK)
        JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
        WHERE b.BOOKING_ID = @BookingId
          AND b.PROVIDER_ID = @ProviderId;

        IF @Current IS NULL
            THROW 50139, N'That booking could not be found.', 1;

        IF NOT (   (@Current = N'Pending'     AND @NewStatus IN (N'Accepted', N'Rejected'))
                OR (@Current = N'Accepted'    AND @NewStatus = N'In Progress')
                OR (@Current = N'In Progress' AND @NewStatus = N'Completed'))
            THROW 50143, N'Invalid status change for the provider.', 1;

        IF @NewStatus = N'In Progress'
           AND @Payment <> N'Paid'
           AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS WHERE BOOKING_ID = @BookingId AND PAYMENT_STATUS = N'PendingCollection')
            THROW 50144, N'The booking is waiting for the customer to approve and pay the quotation, so it can''t start yet.', 1;

        IF @NewStatus = N'Accepted'
           AND dbo.fn_HasBookingConflict(@ProviderId, @Date, @Time, @Minutes, @BookingId, 0) = 1
            THROW 50145, N'This booking overlaps another job that is already on your schedule.', 1;

        UPDATE dbo.BOOKINGS
        SET STATUS = @NewStatus, UPDATED_AT = SYSUTCDATETIME()
        WHERE BOOKING_ID = @BookingId;

        INSERT INTO dbo.NOTIFICATIONS (USER_ID, MESSAGE, IS_READ, CREATED_AT)
        VALUES (@CustomerUserId, CONCAT(N'Your booking #', @BookingId, N' is now ', @NewStatus, N'.'), 0, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_ApproveAndPay
CREATE OR ALTER PROCEDURE dbo.usp_ApproveAndPay
    @CustomerUserId       INT,
    @BookingId            INT,
    @PaymentMethod        NVARCHAR(50),             -- CashOnCompletion | MockEWallet
    @Wallet               NVARCHAR(50)  = NULL,     -- GCash | Maya (MockEWallet only)
    @MobileNumber         NVARCHAR(50)  = NULL,     -- test number, e.g. 0917-000-0000 (checked, never saved)
    @TransactionReference NVARCHAR(50)  = NULL OUTPUT
AS
BEGIN
    /* SERVISYO-DB-V2  (DEMO payment: no money moves)
       The customer approves an Accepted quotation of THEIR OWN booking that has no payment yet:
         CashOnCompletion -> PAYMENTS (PendingCollection, ref CASH-<year>-<booking>) + CASH_PAYMENT_DETAILS;
                             BOOKINGS.PAYMENT_STATUS stays Unpaid until the provider confirms the cash.
         MockEWallet      -> PAYMENTS (Paid, ref GCASH-######## / MAYA-########); BOOKINGS.PAYMENT_STATUS = Paid.
       The amount is always the booking's TOTAL_FEE. The provider gets a notification. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @PaymentMethod = LTRIM(RTRIM(@PaymentMethod));
    SET @Wallet = CASE LOWER(LTRIM(RTRIM(@Wallet))) WHEN N'gcash' THEN N'GCash' WHEN N'maya' THEN N'Maya' ELSE NULL END;
    DECLARE @Mobile NVARCHAR(50) = REPLACE(REPLACE(LTRIM(RTRIM(@MobileNumber)), N' ', N''), N'-', N'');

    IF NOT EXISTS (SELECT 1 FROM dbo.CUSTOMERS c JOIN dbo.USERS u ON u.USER_ID = c.USER_ID
                   WHERE c.USER_ID = @CustomerUserId AND u.ROLE = N'Customer' AND u.ACCOUNT_STATUS = N'Active')
        THROW 50132, N'Only an active customer account can pay for a booking.', 1;
    IF @PaymentMethod IS NULL OR @PaymentMethod NOT IN (N'CashOnCompletion', N'MockEWallet')
        THROW 50146, N'Please choose how you want to pay: CashOnCompletion or MockEWallet.', 1;
    IF @PaymentMethod = N'MockEWallet' AND @Wallet IS NULL
        THROW 50146, N'Please choose GCash or Maya for the test e-wallet payment.', 1;
    IF @PaymentMethod = N'MockEWallet'
       AND NOT (   (LEN(@Mobile) = 11 AND @Mobile LIKE N'09[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]')
                OR (LEN(@Mobile) = 13 AND @Mobile LIKE N'+639[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]')
                OR (LEN(@Mobile) = 12 AND @Mobile LIKE N'639[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'))
        THROW 50146, N'Please enter a valid mobile number for the test e-wallet, like 0917-000-0000.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        DECLARE @Status NVARCHAR(50), @PayStatus NVARCHAR(50), @Fee DECIMAL(10,2), @ProviderUserId INT;
        SELECT @Status = b.STATUS, @PayStatus = b.PAYMENT_STATUS, @Fee = b.TOTAL_FEE, @ProviderUserId = p.USER_ID
        FROM dbo.BOOKINGS b WITH (UPDLOCK)
        JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
        JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = b.PROVIDER_ID
        WHERE b.BOOKING_ID = @BookingId
          AND c.USER_ID = @CustomerUserId;

        IF @Status IS NULL
            THROW 50139, N'That booking could not be found.', 1;
        IF @PayStatus = N'Paid' OR EXISTS (SELECT 1 FROM dbo.PAYMENTS WITH (UPDLOCK, HOLDLOCK) WHERE BOOKING_ID = @BookingId)
            THROW 50147, N'This booking is already paid (or cash on completion was already chosen).', 1;
        IF @Status <> N'Accepted' OR @PayStatus <> N'Unpaid'
            THROW 50148, N'Only an Accepted quotation that is not paid yet can be approved and paid.', 1;

        DECLARE @IsCash BIT = CASE WHEN @PaymentMethod = N'CashOnCompletion' THEN 1 ELSE 0 END;
        DECLARE @Prefix NVARCHAR(10) = CASE WHEN @Wallet = N'Maya' THEN N'MAYA' ELSE N'GCASH' END;
        DECLARE @Tries INT = 0;

        IF @IsCash = 1
            SET @TransactionReference = CONCAT(N'CASH-', YEAR(dbo.fn_ManilaNow()), N'-',
                CASE WHEN @BookingId < 10000 THEN RIGHT(CONCAT(N'0000', @BookingId), 4) ELSE CAST(@BookingId AS NVARCHAR(12)) END);
        ELSE
        BEGIN
            -- a test reference: the wallet prefix and 8 random digits, never used before
            SET @TransactionReference = CONCAT(@Prefix, N'-', RIGHT(CONCAT(N'0000000', ABS(CAST(CHECKSUM(NEWID()) AS BIGINT)) % 100000000), 8));
            WHILE EXISTS (SELECT 1 FROM dbo.PAYMENTS WHERE TRANSACTION_REFERENCE = @TransactionReference) AND @Tries < 10
            BEGIN
                SET @Tries += 1;
                SET @TransactionReference = CONCAT(@Prefix, N'-', RIGHT(CONCAT(N'0000000', ABS(CAST(CHECKSUM(NEWID()) AS BIGINT)) % 100000000), 8));
            END
        END

        INSERT INTO dbo.PAYMENTS (BOOKING_ID, AMOUNT, PAYMENT_METHOD, PAYMENT_STATUS, TRANSACTION_REFERENCE, CREATED_AT, PAID_AT)
        VALUES (@BookingId, @Fee, @PaymentMethod,
                CASE WHEN @IsCash = 1 THEN N'PendingCollection' ELSE N'Paid' END,
                @TransactionReference, SYSUTCDATETIME(),
                CASE WHEN @IsCash = 1 THEN NULL ELSE SYSUTCDATETIME() END);

        IF @IsCash = 1
            INSERT INTO dbo.CASH_PAYMENT_DETAILS (PAYMENT_ID, IS_COLLECTED_BY_PROVIDER, COLLECTED_AT)
            VALUES (CAST(SCOPE_IDENTITY() AS INT), 0, NULL);

        UPDATE dbo.BOOKINGS
        SET PAYMENT_STATUS = CASE WHEN @IsCash = 1 THEN PAYMENT_STATUS ELSE N'Paid' END,
            UPDATED_AT = SYSUTCDATETIME()
        WHERE BOOKING_ID = @BookingId;

        DECLARE @Amount NVARCHAR(40) = N'PHP ' + CONVERT(NVARCHAR(30), CAST(@Fee AS MONEY), 1);
        INSERT INTO dbo.NOTIFICATIONS (USER_ID, MESSAGE, IS_READ, CREATED_AT)
        VALUES (@ProviderUserId,
                CASE WHEN @IsCash = 1
                     THEN CONCAT(N'Booking #', @BookingId, N' was approved. The customer will pay ', @Amount, N' in cash when the job is done. It is now scheduled.')
                     ELSE CONCAT(N'Booking #', @BookingId, N' was approved and paid by ', @Wallet, N' (test, ', @Amount, N', ref ', @TransactionReference, N'). It is now scheduled.')
                END,
                0, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_ConfirmCashReceived
CREATE OR ALTER PROCEDURE dbo.usp_ConfirmCashReceived
    @ProviderUserId INT,
    @BookingId      INT
AS
BEGIN
    /* SERVISYO-DB-V2
       The ASSIGNED provider confirms the cash of a COMPLETED cash-on-completion booking (only once):
       CASH_PAYMENT_DETAILS collected + time, PAYMENTS -> Paid + PAID_AT, BOOKINGS.PAYMENT_STATUS -> Paid,
       and a notification for the customer. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ProviderId INT, @BusinessName NVARCHAR(155);
    SELECT @ProviderId = p.PROVIDER_ID, @BusinessName = p.BUSINESS_NAME
    FROM dbo.SERVICE_PROVIDERS p
    JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
    WHERE p.USER_ID = @ProviderUserId
      AND u.ROLE = N'Service Provider'
      AND u.ACCOUNT_STATUS = N'Active'
      AND p.APPROVAL_STATUS = N'Approved';
    IF @ProviderId IS NULL
        THROW 50121, N'Only an active, approved service provider can confirm payments.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        DECLARE @Status NVARCHAR(50), @CustomerUserId INT, @PaymentId INT, @Method NVARCHAR(50),
                @PayStatus NVARCHAR(50), @Collected BIT, @Amount DECIMAL(10,2), @Ref NVARCHAR(50);
        SELECT @Status = b.STATUS, @CustomerUserId = c.USER_ID,
               @PaymentId = pay.PAYMENT_ID, @Method = pay.PAYMENT_METHOD, @PayStatus = pay.PAYMENT_STATUS,
               @Collected = cash.IS_COLLECTED_BY_PROVIDER, @Amount = pay.AMOUNT, @Ref = pay.TRANSACTION_REFERENCE
        FROM dbo.BOOKINGS b WITH (UPDLOCK)
        JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
        LEFT JOIN dbo.PAYMENTS pay WITH (UPDLOCK) ON pay.BOOKING_ID = b.BOOKING_ID
        LEFT JOIN dbo.CASH_PAYMENT_DETAILS cash WITH (UPDLOCK) ON cash.PAYMENT_ID = pay.PAYMENT_ID
        WHERE b.BOOKING_ID = @BookingId
          AND b.PROVIDER_ID = @ProviderId;

        IF @Status IS NULL
            THROW 50139, N'That booking could not be found.', 1;
        IF @PaymentId IS NULL OR @Method <> N'CashOnCompletion' OR @Collected IS NULL
            THROW 50149, N'This booking is not a cash-on-completion booking.', 1;
        IF @Collected = 1 OR @PayStatus <> N'PendingCollection'
            THROW 50150, N'The cash for this booking was already confirmed.', 1;
        IF @Status <> N'Completed'
            THROW 50151, N'Confirm the cash after you mark the job completed.', 1;

        UPDATE dbo.CASH_PAYMENT_DETAILS SET IS_COLLECTED_BY_PROVIDER = 1, COLLECTED_AT = SYSUTCDATETIME() WHERE PAYMENT_ID = @PaymentId;
        UPDATE dbo.PAYMENTS SET PAYMENT_STATUS = N'Paid', PAID_AT = SYSUTCDATETIME() WHERE PAYMENT_ID = @PaymentId;
        UPDATE dbo.BOOKINGS SET PAYMENT_STATUS = N'Paid', UPDATED_AT = SYSUTCDATETIME() WHERE BOOKING_ID = @BookingId;

        INSERT INTO dbo.NOTIFICATIONS (USER_ID, MESSAGE, IS_READ, CREATED_AT)
        VALUES (@CustomerUserId,
                LEFT(CONCAT(@BusinessName, N' confirmed receiving your PHP ', CONVERT(NVARCHAR(30), CAST(@Amount AS MONEY), 1),
                            N' cash payment for booking #', @BookingId, N' (ref ', @Ref, N').'), 255),
                0, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_AddReview
CREATE OR ALTER PROCEDURE dbo.usp_AddReview
    @CustomerUserId INT,
    @BookingId      INT,
    @Rating         INT,
    @Comment        NVARCHAR(400) = NULL,
    @NewReviewId    INT = NULL OUTPUT
AS
BEGIN
    /* SERVISYO-DB-V2
       The customer of a COMPLETED booking leaves ONE review (1 to 5 stars, comment up to 255 characters).
       The provider's AVG_RATING and REVIEW_COUNT are updated by trg_Reviews_RatingSync; the provider is notified. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Comment = NULLIF(LTRIM(RTRIM(@Comment)), N'');

    IF @Rating IS NULL OR @Rating < 1 OR @Rating > 5
        THROW 50152, N'Please choose from 1 to 5 stars.', 1;
    IF LEN(ISNULL(@Comment, N'')) > 255
        THROW 50152, N'Please keep your comment under 255 characters.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        DECLARE @Status NVARCHAR(50), @ProviderUserId INT, @First NVARCHAR(155), @Last NVARCHAR(155);
        SELECT @Status = b.STATUS, @ProviderUserId = p.USER_ID, @First = u.FIRST_NAME, @Last = u.LAST_NAME
        FROM dbo.BOOKINGS b WITH (UPDLOCK)
        JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
        JOIN dbo.USERS u ON u.USER_ID = c.USER_ID
        JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = b.PROVIDER_ID
        WHERE b.BOOKING_ID = @BookingId
          AND c.USER_ID = @CustomerUserId
          AND u.ACCOUNT_STATUS = N'Active';

        IF @Status IS NULL
            THROW 50139, N'That booking could not be found.', 1;
        IF @Status <> N'Completed'
            THROW 50153, N'Only a completed booking can be reviewed.', 1;
        IF EXISTS (SELECT 1 FROM dbo.REVIEWS WITH (UPDLOCK, HOLDLOCK) WHERE BOOKING_ID = @BookingId)
            THROW 50154, N'This booking already has a review.', 1;

        INSERT INTO dbo.REVIEWS (BOOKING_ID, RATING, COMMENT, CREATED_AT, STATUS)
        VALUES (@BookingId, @Rating, @Comment, SYSUTCDATETIME(), N'Visible');
        SET @NewReviewId = CAST(SCOPE_IDENTITY() AS INT);

        INSERT INTO dbo.NOTIFICATIONS (USER_ID, MESSAGE, IS_READ, CREATED_AT)
        VALUES (@ProviderUserId,
                LEFT(CONCAT(LTRIM(RTRIM(@First)), CASE WHEN LEN(LTRIM(RTRIM(@Last))) > 0 THEN N' ' + UPPER(LEFT(LTRIM(@Last), 1)) + N'.' ELSE N'' END,
                            N' left a ', @Rating, N'-star review for booking #', @BookingId, N'.'), 255),
                0, SYSUTCDATETIME());

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- usp_GetMonthlyReport
CREATE OR ALTER PROCEDURE dbo.usp_GetMonthlyReport
    @Year               INT,
    @Month              INT,
    @PlatformFeePercent DECIMAL(5,2) = 10
AS
BEGIN
    /* SERVISYO-DB-V2
       The admin's monthly numbers (same definitions as Admin > Export PDF Report), by BOOKING_DATE:
         1. summary: revenue = TOTAL_FEE of PAID bookings, platform fee, provider earnings, booking counts, completion rate
         2. bookings by status      3. top 5 categories      4. payments by method */
    SET NOCOUNT ON;

    IF @Year IS NULL OR @Month IS NULL OR @Month NOT BETWEEN 1 AND 12 OR @Year NOT BETWEEN 2000 AND 2100
        THROW 50155, N'Give a valid year and month (1-12).', 1;
    IF @PlatformFeePercent IS NULL OR @PlatformFeePercent < 0 OR @PlatformFeePercent > 100
        THROW 50155, N'The platform fee must be from 0 to 100 percent.', 1;

    DECLARE @First DATE = DATEFROMPARTS(@Year, @Month, 1);
    DECLARE @Next DATE = DATEADD(MONTH, 1, @First);

    DECLARE @Revenue DECIMAL(18,2), @PaidCount INT, @Total INT, @Completed INT, @Cancelled INT, @Rejected INT;
    SELECT @Revenue   = ISNULL(SUM(CASE WHEN b.PAYMENT_STATUS = N'Paid' THEN b.TOTAL_FEE END), 0),
           @PaidCount = SUM(CASE WHEN b.PAYMENT_STATUS = N'Paid' THEN 1 ELSE 0 END),
           @Total     = COUNT(*),
           @Completed = SUM(CASE WHEN b.STATUS = N'Completed' THEN 1 ELSE 0 END),
           @Cancelled = SUM(CASE WHEN b.STATUS = N'Cancelled' THEN 1 ELSE 0 END),
           @Rejected  = SUM(CASE WHEN b.STATUS = N'Rejected' THEN 1 ELSE 0 END)
    FROM dbo.BOOKINGS b
    WHERE b.BOOKING_DATE >= @First AND b.BOOKING_DATE < @Next;

    DECLARE @Fees DECIMAL(18,2) = ROUND(@Revenue * @PlatformFeePercent / 100, 2);

    SELECT CONCAT(DATENAME(MONTH, @First), N' ', @Year) AS REPORT_MONTH,
           @Revenue AS TOTAL_REVENUE,
           @PlatformFeePercent AS PLATFORM_FEE_PERCENT,
           @Fees AS PLATFORM_FEES,
           @Revenue - @Fees AS PROVIDER_EARNINGS,
           ISNULL(@PaidCount, 0) AS PAID_BOOKINGS,
           CASE WHEN ISNULL(@PaidCount, 0) = 0 THEN 0 ELSE ROUND(@Revenue / @PaidCount, 2) END AS AVERAGE_BOOKING_VALUE,
           @Total AS TOTAL_BOOKINGS,
           ISNULL(@Completed, 0) AS COMPLETED,
           ISNULL(@Cancelled, 0) AS CANCELLED,
           ISNULL(@Rejected, 0) AS REJECTED,
           CASE WHEN ISNULL(@Completed, 0) + ISNULL(@Cancelled, 0) + ISNULL(@Rejected, 0) = 0 THEN 0
                ELSE ROUND(@Completed * 100.0 / (@Completed + @Cancelled + @Rejected), 1) END AS COMPLETION_RATE_PERCENT;

    SELECT st.STATUS, COUNT(b.BOOKING_ID) AS BOOKINGS
    FROM (VALUES (1, N'Pending'), (2, N'Accepted'), (3, N'In Progress'), (4, N'Completed'), (5, N'Cancelled'), (6, N'Rejected')) st(SORT_ORDER, STATUS)
    LEFT JOIN dbo.BOOKINGS b ON b.STATUS = st.STATUS AND b.BOOKING_DATE >= @First AND b.BOOKING_DATE < @Next
    GROUP BY st.SORT_ORDER, st.STATUS
    ORDER BY st.SORT_ORDER;

    SELECT TOP (5) sc.CATEGORY_NAME, COUNT(*) AS SERVICES_BOOKED,
           CAST(ROUND(COUNT(*) * 100.0 / NULLIF(SUM(COUNT(*)) OVER (), 0), 1) AS DECIMAL(5,1)) AS SHARE_PERCENT
    FROM dbo.BOOKING_SERVICES bs
    JOIN dbo.BOOKINGS b ON b.BOOKING_ID = bs.BOOKING_ID
    JOIN dbo.SERVICES s ON s.SERVICE_ID = bs.SERVICE_ID
    JOIN dbo.SERVICE_CATEGORIES sc ON sc.CATEGORY_ID = s.CATEGORY_ID
    WHERE b.BOOKING_DATE >= @First AND b.BOOKING_DATE < @Next
    GROUP BY sc.CATEGORY_NAME
    ORDER BY COUNT(*) DESC, sc.CATEGORY_NAME;

    SELECT pay.PAYMENT_METHOD, pay.PAYMENT_STATUS, COUNT(*) AS PAYMENTS, SUM(pay.AMOUNT) AS AMOUNT
    FROM dbo.PAYMENTS pay
    JOIN dbo.BOOKINGS b ON b.BOOKING_ID = pay.BOOKING_ID
    WHERE b.BOOKING_DATE >= @First AND b.BOOKING_DATE < @Next
    GROUP BY pay.PAYMENT_METHOD, pay.PAYMENT_STATUS
    ORDER BY pay.PAYMENT_METHOD, pay.PAYMENT_STATUS;
END
GO

/* =====================================================================================================
   PART 5 - TRIGGERS
   ===================================================================================================== */
-- trg_Reviews_RatingSync
CREATE OR ALTER TRIGGER dbo.trg_Reviews_RatingSync ON dbo.REVIEWS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2
       Keeps SERVICE_PROVIDERS.AVG_RATING and REVIEW_COUNT right whenever a review is added, changes status
       (Visible / Flagged / Removed) or is deleted - from the website, a stored procedure or SSMS.
       Same rule as the website: VISIBLE reviews only, average rounded to 1 decimal, NULL when there is none.
       Set-based: one statement for all affected providers. */
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    -- an update that did not change the rating, the status or the booking: nothing to recalculate
    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND NOT EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.REVIEW_ID = i.REVIEW_ID
                       WHERE i.RATING <> d.RATING OR i.STATUS <> d.STATUS OR i.BOOKING_ID <> d.BOOKING_ID)
        RETURN;

    WITH affected AS (
        SELECT DISTINCT b.PROVIDER_ID
        FROM (SELECT BOOKING_ID FROM inserted UNION SELECT BOOKING_ID FROM deleted) x
        JOIN dbo.BOOKINGS b ON b.BOOKING_ID = x.BOOKING_ID
    ),
    stats AS (
        SELECT a.PROVIDER_ID,
               COUNT(r.REVIEW_ID) AS VISIBLE_COUNT,
               AVG(CAST(r.RATING AS DECIMAL(9,4))) AS AVG_RAW
        FROM affected a
        LEFT JOIN dbo.BOOKINGS b ON b.PROVIDER_ID = a.PROVIDER_ID
        LEFT JOIN dbo.REVIEWS r ON r.BOOKING_ID = b.BOOKING_ID AND r.STATUS = N'Visible'
        GROUP BY a.PROVIDER_ID
    )
    UPDATE p
    SET p.REVIEW_COUNT = s.VISIBLE_COUNT,
        p.AVG_RATING = CASE WHEN s.VISIBLE_COUNT = 0 THEN NULL ELSE CAST(ROUND(s.AVG_RAW, 1) AS DECIMAL(2,1)) END
    FROM dbo.SERVICE_PROVIDERS p
    JOIN stats s ON s.PROVIDER_ID = p.PROVIDER_ID;
END
GO

-- trg_Bookings_StatusFlow
CREATE OR ALTER TRIGGER dbo.trg_Bookings_StatusFlow ON dbo.BOOKINGS
AFTER UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2
       The booking life cycle from the project document, enforced by the database itself:
         Pending -> Accepted | Rejected | Cancelled,  Accepted -> In Progress,  In Progress -> Completed.
       A booking can only start (In Progress) once the customer has paid or chosen cash on completion.
       Any other status change is refused and the whole statement is rolled back.
       Development scripts that must jump a sample booking to any state can first run:
         EXEC sp_set_session_context @key = N'servisyo_dev_override', @value = 1;   (that connection only) */
    SET NOCOUNT ON;

    IF TRY_CAST(SESSION_CONTEXT(N'servisyo_dev_override') AS INT) = 1 RETURN;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
               WHERE i.STATUS <> d.STATUS
                 AND NOT (   (d.STATUS = N'Pending'     AND i.STATUS IN (N'Accepted', N'Rejected', N'Cancelled'))
                          OR (d.STATUS = N'Accepted'    AND i.STATUS = N'In Progress')
                          OR (d.STATUS = N'In Progress' AND i.STATUS = N'Completed')))
        THROW 50160, N'That booking status change is not allowed. Allowed: Pending -> Accepted / Rejected / Cancelled, Accepted -> In Progress, In Progress -> Completed.', 1;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
               WHERE d.STATUS = N'Accepted' AND i.STATUS = N'In Progress'
                 AND i.PAYMENT_STATUS <> N'Paid'
                 AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS p
                                 WHERE p.BOOKING_ID = i.BOOKING_ID AND p.PAYMENT_STATUS = N'PendingCollection'))
        THROW 50161, N'A booking can start only after the customer has paid or chosen cash on completion.', 1;
END
GO

-- trg_Payments_Audit
CREATE OR ALTER TRIGGER dbo.trg_Payments_Audit ON dbo.PAYMENTS
AFTER INSERT, UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2
       System Logs for the PAYMENTS table (same format as the existing audit triggers): who chose cash on completion
       or paid by test e-wallet, and when cash was confirmed. WHO comes from SESSION_CONTEXT (set by the app). */
    SET NOCOUNT ON;
    DECLARE @uid INT = TRY_CAST(SESSION_CONTEXT(N'servisyo_user_id') AS INT);
    DECLARE @role NVARCHAR(50) = COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System');
    DECLARE @now DATETIME2 = SYSUTCDATETIME();

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'INSERT', N'Payment', i.BOOKING_ID,
           LEFT(CONCAT(N'Booking #', i.BOOKING_ID, N' payment chosen: ',
                       CASE i.PAYMENT_METHOD WHEN N'CashOnCompletion' THEN N'cash on completion' ELSE N'test e-wallet' END,
                       N', ', i.PAYMENT_STATUS, N', PHP ', CONVERT(VARCHAR(30), CAST(i.AMOUNT AS MONEY), 1),
                       N', ref ', i.TRANSACTION_REFERENCE, N'.'), 500), @now
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted);

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Payment', i.BOOKING_ID,
           LEFT(CONCAT(N'Booking #', i.BOOKING_ID, N' payment record: ', d.PAYMENT_STATUS, N' -> ', i.PAYMENT_STATUS,
                       N' (ref ', i.TRANSACTION_REFERENCE, N').'), 500), @now
    FROM inserted i
    JOIN deleted d ON d.PAYMENT_ID = i.PAYMENT_ID
    WHERE i.PAYMENT_STATUS <> d.PAYMENT_STATUS;
END
GO

/* =====================================================================================================
   PART 6 - SELF-TEST
   Every test runs inside its own transaction and is ROLLED BACK: nothing is saved. (SQL Server does not reuse
   IDENTITY numbers, so the next real booking / user id may skip a few numbers - that is normal.)
   ===================================================================================================== */
IF OBJECT_ID(N'tempdb..#svSetup') IS NOT NULL DROP PROCEDURE #svSetup;
GO
CREATE PROCEDURE #svSetup
    @AdminUserId     INT OUTPUT,
    @CustomerUserId  INT OUTPUT,
    @Customer2UserId INT OUTPUT,
    @ProviderUserId  INT OUTPUT,
    @ProviderId      INT OUTPUT,
    @ServiceId       INT OUTPUT,
    @TestDate        DATE OUTPUT
AS
BEGIN
    -- Sample data for ONE test, made through the stored procedures themselves. Always called inside a transaction
    -- that the test rolls back, so none of it is kept.
    SET NOCOUNT ON;
    DECLARE @tag NVARCHAR(12) = LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 12);
    DECLARE @hash NVARCHAR(100) = N'SELFTEST-NOT-A-REAL-PASSWORD-HASH-000000';
    DECLARE @email NVARCHAR(155), @cid INT, @catId INT, @availId INT, @dow TINYINT;
    DECLARE @biz NVARCHAR(155) = N'Selftest Plumbing ' + @tag;
    DECLARE @catName NVARCHAR(100) = N'Selftest ' + @tag;
    DECLARE @svcName NVARCHAR(100) = N'Leak Repair ' + @tag;

    INSERT INTO dbo.USERS (FIRST_NAME, LAST_NAME, EMAIL, PASSWORD_HASH, ROLE, ACCOUNT_STATUS, CREATED_AT, EMAIL_VERIFICATION_PENDING)
    VALUES (N'Selftest', N'Admin', N'admin-' + @tag + N'@selftest.local', @hash, N'Administrator', N'Active', SYSUTCDATETIME(), 0);
    SET @AdminUserId = CAST(SCOPE_IDENTITY() AS INT);

    SET @email = N'ana-' + @tag + N'@selftest.local';
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Ana', @LastName = N'Selftest', @Email = @email, @PasswordHash = @hash,
         @PhoneNumber = N'0917-000-0000', @Address = N'Lahug', @City = N'Cebu City',
         @NewUserId = @CustomerUserId OUTPUT, @NewCustomerId = @cid OUTPUT;

    SET @email = N'ben-' + @tag + N'@selftest.local';
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Ben', @LastName = N'Selftest', @Email = @email, @PasswordHash = @hash,
         @PhoneNumber = N'09170000001', @Address = N'Mabolo', @City = N'Cebu City',
         @NewUserId = @Customer2UserId OUTPUT, @NewCustomerId = @cid OUTPUT;

    SET @email = N'mary-' + @tag + N'@selftest.local';
    EXEC dbo.usp_RegisterProvider @FirstName = N'Mary', @MiddleName = N'Cruz', @LastName = N'Selftest', @Email = @email,
         @PasswordHash = @hash, @PhoneNumber = N'09181234567', @BusinessName = @biz, @Description = N'Pipes and leaks',
         @Address = N'Banilad', @City = N'Cebu City', @BaseFee = 300, @MinNoticeDays = 1, @MaxJobsPerDay = 3,
         @NewUserId = @ProviderUserId OUTPUT, @NewProviderId = @ProviderId OUTPUT;

    EXEC dbo.usp_ApproveProvider @AdminUserId = @AdminUserId, @ProviderId = @ProviderId, @Decision = N'Approved', @Reason = N'Self-test';

    INSERT INTO dbo.SERVICE_CATEGORIES (CATEGORY_NAME, DESCRIPTION, IS_ACTIVE) VALUES (@catName, N'Self-test only', 1);
    SET @catId = CAST(SCOPE_IDENTITY() AS INT);

    EXEC dbo.usp_AddService @ProviderUserId = @ProviderUserId, @CategoryId = @catId, @ServiceName = @svcName,
         @Description = N'Fix leaking pipes', @Price = 650, @DurationMinutes = 60, @NewServiceId = @ServiceId OUTPUT;

    -- a working day one week from today: 8:00 AM - 5:00 PM with a lunch break 12:00 - 1:00 PM
    SET @TestDate = DATEADD(DAY, 7, CAST(dbo.fn_ManilaNow() AS DATE));
    SET @dow = dbo.fn_DayOfWeek(@TestDate);
    EXEC dbo.usp_AddAvailability @ProviderUserId = @ProviderUserId, @DayOfWeek = @dow, @StartTime = '08:00', @EndTime = '17:00',
         @NewAvailabilityId = @availId OUTPUT;
    INSERT INTO dbo.PROVIDER_BREAKS (AVAILABILITY_ID, BREAK_START, BREAK_END) VALUES (@availId, '12:00', '13:00');
END
GO

SET NOCOUNT ON;
DECLARE @R TABLE (N INT IDENTITY(1,1), TEST NVARCHAR(200), RESULT NVARCHAR(4), DETAIL NVARCHAR(1000));
DECLARE @A INT, @C INT, @C2 INT, @PU INT, @P INT, @S INT, @D DATE, @B INT, @B2 INT, @RevId INT, @n INT,
        @ref NVARCHAR(50), @ids NVARCHAR(20), @t NVARCHAR(200), @email NVARCHAR(155), @kw NVARCHAR(100);
DECLARE @Found TABLE (SERVICE_ID INT, SERVICE_NAME NVARCHAR(100), DESCRIPTION NVARCHAR(255), PRICE DECIMAL(10,2), DURATION_MINUTES INT,
                      CATEGORY_ID INT, CATEGORY_NAME NVARCHAR(100), PROVIDER_ID INT, BUSINESS_NAME NVARCHAR(155), CITY NVARCHAR(50),
                      AVG_RATING DECIMAL(2,1), REVIEW_COUNT INT);

------------------------------------------------------------------ 1
SET @t = N'01 Register customer + provider (procedures), approve, add service';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    IF EXISTS (SELECT 1 FROM dbo.USERS u JOIN dbo.CUSTOMERS c ON c.USER_ID = u.USER_ID
               WHERE u.USER_ID = @C AND u.ROLE = N'Customer' AND u.ACCOUNT_STATUS = N'Active' AND u.PHONE_NUMBER = N'09170000000')
       AND EXISTS (SELECT 1 FROM dbo.SERVICE_PROVIDERS WHERE PROVIDER_ID = @P AND USER_ID = @PU
                   AND APPROVAL_STATUS = N'Approved' AND DATE_APPROVED IS NOT NULL AND REVIEW_COUNT = 0)
       AND EXISTS (SELECT 1 FROM dbo.NOTIFICATIONS WHERE USER_ID = @A AND MESSAGE LIKE N'New provider application from%')
       AND EXISTS (SELECT 1 FROM dbo.NOTIFICATIONS WHERE USER_ID = @PU AND MESSAGE = N'Your provider application was Approved.')
       AND EXISTS (SELECT 1 FROM dbo.ADMIN_ACTIONS WHERE ADMIN_USER_ID = @A AND ACTION_TYPE = N'Approved' AND TARGET_ID = @P)
       AND EXISTS (SELECT 1 FROM dbo.SERVICES s JOIN dbo.PROVIDER_CATEGORIES pc ON pc.PROVIDER_ID = s.PROVIDER_ID AND pc.CATEGORY_ID = s.CATEGORY_ID
                   WHERE s.SERVICE_ID = @S AND s.PROVIDER_ID = @P)
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'An expected row is missing.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 2
SET @t = N'02 The same email cannot register twice (error 50101)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SELECT @email = UPPER(EMAIL) FROM dbo.USERS WHERE USER_ID = @C;
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Copy', @LastName = N'Cat', @Email = @email,
         @PasswordHash = N'SELFTEST-NOT-A-REAL-PASSWORD-HASH-000000', @PhoneNumber = N'09170000002', @Address = N'Talamban', @NewUserId = @n OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50101, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 3
SET @t = N'03 fn_DayOfWeek: 2026-10-25 = 0 (Sunday), 2026-10-31 = 6 (Saturday)';
IF dbo.fn_DayOfWeek('2026-10-25') = 0 AND dbo.fn_DayOfWeek('2026-10-31') = 6 AND dbo.fn_DayOfWeek('2026-10-28') = 3
    INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
ELSE
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(N'got ', dbo.fn_DayOfWeek('2026-10-25'), N' and ', dbo.fn_DayOfWeek('2026-10-31')));

------------------------------------------------------------------ 4
SET @t = N'04 usp_CreateBooking: Pending / Unpaid, fee and services copied, provider notified';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug, Cebu City', @ServiceDescription = N'Kitchen sink is leaking', @NewBookingId = @B OUTPUT;
    IF EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND STATUS = N'Pending' AND PAYMENT_STATUS = N'Unpaid'
               AND TOTAL_FEE = 650 AND TOTAL_DURATION_MINUTES = 60 AND END_TIME = '10:00')
       AND (SELECT COUNT(*) FROM dbo.BOOKING_SERVICES WHERE BOOKING_ID = @B AND SERVICE_ID = @S AND PRICE_SNAPSHOT = 650) = 1
       AND EXISTS (SELECT 1 FROM dbo.NOTIFICATIONS WHERE USER_ID = @PU AND MESSAGE = CONCAT(N'You have a new booking request (Booking #', @B, N').'))
       AND EXISTS (SELECT 1 FROM dbo.vw_BookingOverview WHERE BOOKING_ID = @B AND SERVICES LIKE N'Leak Repair%')
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'The booking rows are not as expected.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 5
SET @t = N'05 Overlapping request is refused (9:00 booked, 9:30 asked) (50138)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'First', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C2, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:30',
         @ServiceLocation = N'Mabolo', @ServiceDescription = N'Second', @NewBookingId = @B2 OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50138, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 6
SET @t = N'06 A job that runs into the 12:00 break is refused (11:30, 60 min) (50138)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '11:30',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Break', @NewBookingId = @B OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50138, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 7
SET @t = N'07 A job that ends after working hours is refused (4:30 PM, 60 min) (50138)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '16:30',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Late', @NewBookingId = @B OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50138, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 8
SET @t = N'08 fn_GetAvailableSlots: 8:00 and 1:00 PM free; 12:00 (break) and 4:30 PM (too late) not offered';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    IF EXISTS (SELECT 1 FROM dbo.fn_GetAvailableSlots(@P, @D, 60) WHERE SLOT_TIME = '08:00')
       AND EXISTS (SELECT 1 FROM dbo.fn_GetAvailableSlots(@P, @D, 60) WHERE SLOT_TIME = '13:00')
       AND NOT EXISTS (SELECT 1 FROM dbo.fn_GetAvailableSlots(@P, @D, 60) WHERE SLOT_TIME IN ('11:30', '12:00', '16:30', '07:30'))
       AND (SELECT COUNT(*) FROM dbo.fn_GetAvailableSlots(@P, @D, 60)) = 14     -- 8:00 ... 4:00 PM = 17 starts, minus 11:30, 12:00, 12:30
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(N'slots found: ', (SELECT COUNT(*) FROM dbo.fn_GetAvailableSlots(@P, @D, 60))));
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 9
SET @t = N'09 Provider cannot start before the customer pays (50144)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Unpaid start', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'In Progress';
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50144, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 10
SET @t = N'10 Cash on completion: pending collection -> start -> complete -> Confirm Cash Received';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Cash job', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'CashOnCompletion', @TransactionReference = @ref OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS p JOIN dbo.CASH_PAYMENT_DETAILS c ON c.PAYMENT_ID = p.PAYMENT_ID
                   WHERE p.BOOKING_ID = @B AND p.PAYMENT_METHOD = N'CashOnCompletion' AND p.PAYMENT_STATUS = N'PendingCollection'
                     AND p.PAID_AT IS NULL AND p.AMOUNT = 650 AND c.IS_COLLECTED_BY_PROVIDER = 0 AND p.TRANSACTION_REFERENCE = @ref)
       OR NOT EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND STATUS = N'Accepted' AND PAYMENT_STATUS = N'Unpaid')
        THROW 59001, N'After choosing cash: the payment rows are not as expected.', 1;

    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'In Progress';
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Completed';
    EXEC dbo.usp_ConfirmCashReceived @ProviderUserId = @PU, @BookingId = @B;

    IF EXISTS (SELECT 1 FROM dbo.PAYMENTS p JOIN dbo.CASH_PAYMENT_DETAILS c ON c.PAYMENT_ID = p.PAYMENT_ID
               WHERE p.BOOKING_ID = @B AND p.PAYMENT_STATUS = N'Paid' AND p.PAID_AT IS NOT NULL
                 AND c.IS_COLLECTED_BY_PROVIDER = 1 AND c.COLLECTED_AT IS NOT NULL)
       AND EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND STATUS = N'Completed' AND PAYMENT_STATUS = N'Paid')
       AND EXISTS (SELECT 1 FROM dbo.NOTIFICATIONS WHERE USER_ID = @C AND MESSAGE LIKE N'%cash payment for booking #%')
       AND EXISTS (SELECT 1 FROM dbo.SYSTEM_LOGS WHERE ENTITY_NAME = N'Payment' AND ENTITY_ID = @B AND DESCRIPTION LIKE N'%PendingCollection -> Paid%')
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'PASS', @ref);
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'After Confirm Cash Received: the rows are not as expected.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 11
SET @t = N'11 Confirm cash before the job is completed is refused (50151)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Too early', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'CashOnCompletion', @TransactionReference = @ref OUTPUT;
    EXEC dbo.usp_ConfirmCashReceived @ProviderUserId = @PU, @BookingId = @B;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50151, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 12
SET @t = N'12 Mock e-wallet (GCash): paid at once with a GCASH-######## reference';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Wallet job', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'MockEWallet', @Wallet = N'gcash',
         @MobileNumber = N'0917-000-0000', @TransactionReference = @ref OUTPUT;
    IF @ref LIKE N'GCASH-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]' AND LEN(@ref) = 14
       AND EXISTS (SELECT 1 FROM dbo.PAYMENTS WHERE BOOKING_ID = @B AND PAYMENT_METHOD = N'MockEWallet' AND PAYMENT_STATUS = N'Paid' AND PAID_AT IS NOT NULL)
       AND NOT EXISTS (SELECT 1 FROM dbo.CASH_PAYMENT_DETAILS c JOIN dbo.PAYMENTS p ON p.PAYMENT_ID = c.PAYMENT_ID WHERE p.BOOKING_ID = @B)
       AND EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND PAYMENT_STATUS = N'Paid')
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'PASS', @ref);
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(N'reference: ', @ref));
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 13
SET @t = N'13 Mock e-wallet with a wrong mobile number is refused (50146)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Bad number', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'MockEWallet', @Wallet = N'Maya',
         @MobileNumber = N'0817-000-0000', @TransactionReference = @ref OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50146, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 14
SET @t = N'14 Paying the same booking twice is refused (50147)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Twice', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'CashOnCompletion', @TransactionReference = @ref OUTPUT;
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'MockEWallet', @Wallet = N'GCash',
         @MobileNumber = N'09170000000', @TransactionReference = @ref OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50147, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 15
SET @t = N'15 Review trigger keeps AVG_RATING / REVIEW_COUNT right (add 4 stars, hide it, restore it)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Review me', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'MockEWallet', @Wallet = N'Maya',
         @MobileNumber = N'+639170000000', @TransactionReference = @ref OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'In Progress';
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Completed';
    EXEC dbo.usp_AddReview @CustomerUserId = @C, @BookingId = @B, @Rating = 4, @Comment = N'Quick and clean.', @NewReviewId = @RevId OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.SERVICE_PROVIDERS WHERE PROVIDER_ID = @P AND AVG_RATING = 4.0 AND REVIEW_COUNT = 1)
        THROW 59002, N'After adding a 4-star review the provider should have 4.0 from 1 review.', 1;
    EXEC dbo.usp_ModerateReview @AdminUserId = @A, @ReviewId = @RevId, @NewStatus = N'Removed', @Reason = N'Self-test';
    IF NOT EXISTS (SELECT 1 FROM dbo.SERVICE_PROVIDERS WHERE PROVIDER_ID = @P AND AVG_RATING IS NULL AND REVIEW_COUNT = 0)
        THROW 59003, N'After hiding the review the provider should have no rating (NULL, 0 reviews).', 1;
    EXEC dbo.usp_ModerateReview @AdminUserId = @A, @ReviewId = @RevId, @NewStatus = N'Visible';
    IF EXISTS (SELECT 1 FROM dbo.SERVICE_PROVIDERS WHERE PROVIDER_ID = @P AND AVG_RATING = 4.0 AND REVIEW_COUNT = 1)
       AND dbo.fn_GetProviderAvgRating(@P) = 4.0
       AND (SELECT COUNT(*) FROM dbo.ADMIN_ACTIONS WHERE ADMIN_USER_ID = @A AND TARGET_TYPE = N'REVIEW' AND TARGET_ID = @RevId) = 2
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'After restoring the review the rating is not 4.0 from 1 review.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 16
SET @t = N'16 A customer cannot cancel an Accepted booking (50142)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'No cancel', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_CancelBooking @CustomerUserId = @C, @BookingId = @B;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50142, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 17
SET @t = N'17 Another customer''s booking looks missing (cancel by Ben) (50139)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Mine', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_CancelBooking @CustomerUserId = @C2, @BookingId = @B;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50139, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 18
SET @t = N'18 Trigger trg_Bookings_StatusFlow refuses Pending -> Completed done directly in SQL (50160)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Jump', @NewBookingId = @B OUTPUT;
    UPDATE dbo.BOOKINGS SET STATUS = N'Completed' WHERE BOOKING_ID = @B;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50160, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 19
SET @t = N'19 The developer override lets a sample script jump a status (this connection only)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Dev jump', @NewBookingId = @B OUTPUT;
    EXEC sp_set_session_context @key = N'servisyo_dev_override', @value = 1;
    UPDATE dbo.BOOKINGS SET STATUS = N'Completed' WHERE BOOKING_ID = @B;
    EXEC sp_set_session_context @key = N'servisyo_dev_override', @value = NULL;
    IF EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND STATUS = N'Completed')
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'The status did not change.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sp_set_session_context @key = N'servisyo_dev_override', @value = NULL;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 20
SET @t = N'20 usp_SearchServices finds the new service by keyword';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SELECT @kw = SERVICE_NAME FROM dbo.SERVICES WHERE SERVICE_ID = @S;
    DELETE FROM @Found;
    INSERT INTO @Found EXEC dbo.usp_SearchServices @Keyword = @kw;
    IF EXISTS (SELECT 1 FROM @Found WHERE SERVICE_ID = @S AND PROVIDER_ID = @P)
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(N'rows found: ', (SELECT COUNT(*) FROM @Found)));
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 21
SET @t = N'21 A suspended customer cannot book (usp_UpdateAccountStatus, then 50132)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    EXEC dbo.usp_UpdateAccountStatus @AdminUserId = @A, @TargetUserId = @C, @NewStatus = N'Suspended', @Reason = N'Self-test';
    IF NOT EXISTS (SELECT 1 FROM dbo.ADMIN_ACTIONS WHERE ADMIN_USER_ID = @A AND ACTION_TYPE = N'Account Suspended' AND TARGET_ID = @C)
        THROW 59004, N'The ADMIN_ACTIONS row is missing.', 1;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Suspended', @NewBookingId = @B OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50132, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ results
SELECT N AS [#], TEST, RESULT, DETAIL FROM @R ORDER BY N;

DECLARE @Total INT = (SELECT COUNT(*) FROM @R);
DECLARE @Failed INT = (SELECT COUNT(*) FROM @R WHERE RESULT <> N'PASS');
IF @Failed = 0
    PRINT CONCAT(N'SELF-TEST: all ', @Total, N' tests passed. Nothing was saved (every test was rolled back).');
ELSE
    PRINT CONCAT(N'SELF-TEST: ', @Failed, N' of ', @Total, N' tests FAILED. Nothing was saved. Send the result grid to Claude.');
GO
DROP PROCEDURE #svSetup;
GO

/* =====================================================================================================
   PART 7 - EXAMPLES AND SUBQUERIES (read-only; run one at a time)
   =====================================================================================================
-- Search services:                        EXEC dbo.usp_SearchServices @Keyword = N'leak', @CategoryId = NULL;
-- A provider's week:                      EXEC dbo.usp_GetProviderAvailability @ProviderId = 1;
-- Free start times for a 2-hour job:      SELECT * FROM dbo.fn_GetAvailableSlots(1, DATEADD(DAY, 7, CAST(dbo.fn_ManilaNow() AS DATE)), 120);
-- This month's report:                    EXEC dbo.usp_GetMonthlyReport @Year = 2026, @Month = 10;
-- Every booking with names and payment:   SELECT * FROM dbo.vw_BookingOverview ORDER BY BOOKING_ID DESC;
-- Providers customers can see:            SELECT * FROM dbo.vw_ProviderDirectory ORDER BY AVG_RATING DESC;
-- Rating check (stored vs calculated):    SELECT PROVIDER_ID, AVG_RATING, dbo.fn_GetProviderAvgRating(PROVIDER_ID) AS CALCULATED FROM dbo.SERVICE_PROVIDERS;

-- A. Providers rated above the average of rated providers (subquery in WHERE)
SELECT BUSINESS_NAME, AVG_RATING
FROM dbo.SERVICE_PROVIDERS
WHERE AVG_RATING > (SELECT AVG(AVG_RATING) FROM dbo.SERVICE_PROVIDERS WHERE AVG_RATING IS NOT NULL);

-- B. Customers with at least one completed booking (IN subquery)
SELECT dbo.fn_GetFullName(c.USER_ID) AS CUSTOMER_NAME
FROM dbo.CUSTOMERS c
WHERE c.CUSTOMER_ID IN (SELECT b.CUSTOMER_ID FROM dbo.BOOKINGS b WHERE b.STATUS = N'Completed');

-- C. Approved providers with no bookings yet (NOT EXISTS)
SELECT p.BUSINESS_NAME
FROM dbo.SERVICE_PROVIDERS p
WHERE p.APPROVAL_STATUS = N'Approved'
  AND NOT EXISTS (SELECT 1 FROM dbo.BOOKINGS b WHERE b.PROVIDER_ID = p.PROVIDER_ID);

-- D. Bookings per provider (subquery in SELECT)
SELECT p.BUSINESS_NAME,
       (SELECT COUNT(*) FROM dbo.BOOKINGS b WHERE b.PROVIDER_ID = p.PROVIDER_ID) AS TOTAL_BOOKINGS
FROM dbo.SERVICE_PROVIDERS p;

-- E. Services priced above the average active service price (subquery in WHERE)
SELECT SERVICE_NAME, PRICE
FROM dbo.SERVICES
WHERE IS_ACTIVE = 1 AND PRICE > (SELECT AVG(PRICE) FROM dbo.SERVICES WHERE IS_ACTIVE = 1);

-- F. Cash still to collect, by provider (cash on completion, job completed, not confirmed)
SELECT v.BUSINESS_NAME, COUNT(*) AS JOBS, SUM(v.TOTAL_FEE) AS CASH_TO_COLLECT
FROM dbo.vw_BookingOverview v
WHERE v.PAYMENT_METHOD = N'CashOnCompletion' AND v.PAYMENT_RECORD_STATUS = N'PendingCollection' AND v.STATUS = N'Completed'
GROUP BY v.BUSINESS_NAME;
   ===================================================================================================== */

SET NOEXEC OFF;
GO
