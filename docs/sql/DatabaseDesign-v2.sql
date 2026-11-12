/* =====================================================================================================
   SERVISYO - Cebu-Based Service Provider Booking Platform            Group 3 | Microsoft SQL Server
   DATABASE DESIGN v2.7  (improved version of DatabaseDesign.sql)

   CONTENTS
     PART 0  Safety checks (run on the SERVISYO database, after "dotnet ef database update")
     PART 1  Tables, keys, relationships, CHECK rules and indexes  (REFERENCE - created by the EF migrations)
     PART 2  Functions (8)
     PART 3  Views (2)
     PART 4  Stored procedures (21)
     PART 5  Triggers (6 new; the 3 audit triggers trg_Users_Audit / trg_Bookings_Audit / trg_Services_Audit are unchanged)
     PART 6  Self-test: runs every procedure, function and trigger with sample data and ROLLS IT ALL BACK
     PART 7  Examples and subqueries for the defense

   HOW THIS FILE IS USED
     - The web app applies PARTS 2-5 itself with the EF migrations "DatabaseV2", "ProfileCleanup", "AdminTargets" and "DerivedData" (dotnet ef database update);
       "ExtraDayOff" (v2.4) only adds one column and its FOREIGN KEY, "OneDayOffPerDate" (v2.5) one UNIQUE index (PART 1),
       "MaxJobsPerDay20" (v2.6) the 20-job limit (CK_PROVIDERS_LIMITS and usp_RegisterProvider), "DayOffRequests"
       (v2.7) the table PROVIDER_DAY_OFF_REQUESTS, the column PROVIDER_UNAVAILABLE_DATES.REQUEST_ID (PART 1) and the
       new versions of fn_IsProviderAvailable, usp_CreateBooking and usp_GetNotifications:
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

   WHAT CHANGED IN v2.1 (EF migration "ProfileCleanup")
     - SERVISYO is Cebu City only, so the CITY columns of CUSTOMERS and SERVICE_PROVIDERS were removed.
     - Column sizes: EMAIL NVARCHAR(256); FIRST_NAME / MIDDLE_NAME / LAST_NAME NVARCHAR(100);
       PHONE_NUMBER NVARCHAR(11), always a Philippine mobile number 09XXXXXXXXX (+639... is turned into 09...).
     - The sign-up procedures refuse names with numbers, emails whose domain has numbers or one-letter parts
       (juan@gmail2.com, juan@a.c) and addresses made of numbers only.
     - Procedure parameters stay larger than the columns ON PURPOSE: SQL Server silently cuts a too-long value
       to the parameter size, so a bigger parameter lets the procedure see the real length and refuse it
       with a clear message instead of saving a cut-off value.
     - CUSTOMERS.ACCOUNT_STATUS and SERVICE_PROVIDERS.ACCOUNT_STATUS are copies of USERS.ACCOUNT_STATUS, kept equal
       by trg_Users_StatusSync / trg_Customers_StatusCopy / trg_Providers_StatusCopy. USERS stays the real status.
     - usp_UpdateAccountStatus: suspending or deactivating a provider cancels their Pending bookings (the customers
       are notified) and lists their Accepted / In Progress bookings for the administrator to follow up.

   WHAT CHANGED IN v2.2 (EF migration "AdminTargets")
     - ADMIN_ACTIONS no longer has one TARGET_ID that could mean four different things. Each kind of target has its
       own column with a FOREIGN KEY: TARGET_CUSTOMER_ID -> CUSTOMERS, TARGET_PROVIDER_ID -> SERVICE_PROVIDERS,
       TARGET_REVIEW_ID -> REVIEWS, TARGET_CATEGORY_ID -> SERVICE_CATEGORIES.
     - TARGET_TYPE is specific: CUSTOMER, SERVICE_PROVIDER, REVIEW or CATEGORY (no more "USER"). Suspending a customer
       logs the CUSTOMER; suspending a provider logs the SERVICE_PROVIDER (the PROVIDER_ID, like approve / reject).
     - CHECK CK_ADMIN_ACTIONS_TARGET: exactly one target column is filled, and it is the one TARGET_TYPE names.
     - The migration converted the existing rows (USER -> CUSTOMER or SERVICE_PROVIDER) before removing TARGET_ID.

   WHAT CHANGED IN v2.3 (EF migration "DerivedData") - the data-management rules
       1. One fact, one place: a value is never repeated in another table; it is referenced with a FOREIGN KEY.
       2. Nothing is stored that can be calculated from other rows.
       3. A table that only holds derivable rows becomes a query that shows the newest 25 (SELECT TOP 25), so the
          database does not grow with rows nobody needs.
       4. Every feature keeps working.
     - NOTIFICATIONS was removed. usp_GetNotifications works out each user's bell from BOOKINGS, PAYMENTS_DTL,
       REVIEWS, ADMIN_ACTIONS and the Pending applications (newest 25, each booking only its latest news).
       USERS.NOTIFICATIONS_SEEN_AT is the only thing stored: what is newer is unread.
     - BOOKING_REPORTS was removed (no screen ever filled it). usp_GetBookingsNeedingAttention lists, newest 25,
       the bookings an administrator should look at: a flagged or 1-2 star review, or a date that has passed while
       the booking is still Pending or Accepted.
     - EMAIL_OTPS was removed. The password reset code is calculated by the app (a keyed hash of the user, the
       current password hash and the time), so nothing is stored and the code stops working once it is used.
     - PAYMENTS is now the list of payment methods: 1 Cash on Completion, 2 GCash, 3 Maya. PAYMENTS_DTL is the
       bridge between BOOKINGS and PAYMENTS: each booking's payment (one per booking), with its status, reference
       and dates. The amount is not stored again: it is the booking's TOTAL_FEE.
     - CASH_PAYMENT_DETAILS was removed: "collected by the provider" is a cash payment whose status is Paid, at PAID_AT.
     - The procedures that used to write NOTIFICATIONS no longer do; trg_Payments_Audit became trg_PaymentsDtl_Audit.

   WHAT CHANGED IN v2.4 (EF migration "ExtraDayOff") - extra days off approved by an administrator
     - A provider may mark at most 5 days off per calendar month (checked by the app). For an emergency beyond that,
       an administrator approves an extra day: an ADMIN_ACTIONS row (ACTION_TYPE 'Extra Day Off Approved', who, when,
       why) and the PROVIDER_UNAVAILABLE_DATES row, which REFERENCES it through the new nullable column
       APPROVED_BY_ACTION_ID (FOREIGN KEY -> ADMIN_ACTIONS, no cascade). Rows with it filled do not count toward the 5.
       Nothing is copied: who/when/why stay in ADMIN_ACTIONS only.
     - A day with an active booking (Pending, Accepted, In Progress) cannot be marked off (checked by the app in a
       SERIALIZABLE transaction, like usp_CreateBooking checks unavailable dates the other way round).
     - Unavailable dates are only for the current year; last year's rows are deleted on January 1 (by the app).

   WHAT CHANGED IN v2.5 (EF migration "OneDayOffPerDate") - only one provider off per day
     - UNIQUE index IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE on PROVIDER_UNAVAILABLE_DATES (UNAVAILABLE_DATE):
       a date can be in the table only once, whichever provider it belongs to (first come, first served), so on every
       day the other providers stay bookable. The app checks it first (with a friendly message, also for
       administrators' extra days); the index is the guarantee, even when two providers save at the same moment.
     - Existing duplicates must be resolved before the index can be created (tools\add-one-day-off-per-date.ps1 lists
       them and, only if you type YES, keeps the first one saved for each date).

   WHAT CHANGED IN v2.6 (EF migration "MaxJobsPerDay20") - at most 20 jobs a day
     - CK_PROVIDERS_LIMITS: MAX_JOBS_PER_DAY BETWEEN 1 AND 20 (was: > 0; the app allowed up to 50).
     - usp_RegisterProvider: THROW 50110 for anything outside 1 to 20 (was 1 to 50).
     - Providers saved with more than 20 are set to 20 by the migration (tools\set-max-jobs-20.ps1 lists them and
       asks first).

   WHAT CHANGED IN v2.7 (EF migration "DayOffRequests") - providers REQUEST days off, an administrator answers
     - New table PROVIDER_DAY_OFF_REQUESTS: one row per request (PROVIDER_ID, REASON, STATUS, REQUESTED_AT,
       DECIDED_BY_ACTION_ID). CHECK CK_PROVIDER_DAY_OFF_REQUESTS_STATUS: Pending, Approved or Rejected.
       CHECK CK_PROVIDER_DAY_OFF_REQUESTS_DECISION: a Pending request has no decision yet; an Approved or Rejected one
       REFERENCES the ADMIN_ACTIONS row that decided it (who, when; FOREIGN KEY, no cascade, UNIQUE: one action
       decides one request). Nothing is copied: the dates are the request's PROVIDER_UNAVAILABLE_DATES rows.
     - PROVIDER_UNAVAILABLE_DATES.REQUEST_ID (nullable FOREIGN KEY -> PROVIDER_DAY_OFF_REQUESTS, no cascade): the days
       of a request, one row per date, so the v2.5 UNIQUE index still allows only one provider per date - a Pending
       request already holds its dates (first come, first served). CHECK CK_PROVIDER_UNAVAILABLE_DATES_SOURCE: a day
       comes from a request OR is an administrator's extra day (APPROVED_BY_ACTION_ID), never both.
     - A request covers 1 to 7 days in a row ("Maximum Day Off is 1 week or 7 days"), from tomorrow to December 31,
       never a day with an active booking, one waiting request per provider at a time (all checked by the app).
       The 5-days-a-month limit of v2.4 is gone: the administrator decides.
     - Which days stop bookings: a day without a request (older days off, extra days) or a day of an APPROVED
       request. fn_IsProviderAvailable and usp_CreateBooking now check exactly that (a Pending request does not
       block yet). Rejecting a request deletes its days, so the dates are free again; the request row stays as the
       record of the decision.
     - usp_GetNotifications: providers see the answer to their request (and an extra day an administrator gave);
       administrators see every request that is still Pending.
   ===================================================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;     -- needed for the filtered index on BOOKINGS (sqlcmd turns it off unless -I is used)
SET NOEXEC OFF;
GO

/* =====================================================================================================
   PART 0 - SAFETY CHECKS
   ===================================================================================================== */
IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL OR OBJECT_ID(N'dbo.BOOKINGS') IS NULL
   OR OBJECT_ID(N'dbo.PAYMENTS_DTL') IS NULL OR OBJECT_ID(N'dbo.SYSTEM_LOGS') IS NULL
BEGIN
    RAISERROR(N'Stopped: run this file on the SERVISYO database (choose it in the SSMS toolbar). Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO
-- the app must already know about the new triggers (HasTrigger in ServisyoDbContext.cs), and v2.7 reads the table and
-- column of the "DayOffRequests" migration, so the EF migrations come first
IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]DayOffRequests')
BEGIN
    RAISERROR(N'Stopped: run "dotnet ef database update --project src\Servisyo.Web" first (it installs these same objects). Then run this file again for the self-test. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

DECLARE @clash NVARCHAR(MAX) = STUFF((
    SELECT N', ' + o.name
    FROM sys.objects o
    WHERE o.schema_id = SCHEMA_ID(N'dbo')
      AND o.name IN (N'fn_DayOfWeek', N'fn_ManilaNow', N'fn_GetFullName', N'fn_GetProviderAvgRating', N'fn_CountProviderJobsOnDate', N'fn_IsProviderAvailable', N'fn_HasBookingConflict', N'fn_GetAvailableSlots', N'vw_BookingOverview', N'vw_ProviderDirectory', N'usp_RegisterCustomer', N'usp_RegisterProvider', N'usp_GetUserForLogin', N'usp_ApproveProvider', N'usp_UpdateAccountStatus', N'usp_ModerateReview', N'usp_AddService', N'usp_SearchServices', N'usp_GetProviderAvailability', N'usp_AddAvailability', N'usp_UpdateAvailability', N'usp_DeleteAvailability', N'usp_CreateBooking', N'usp_CancelBooking', N'usp_UpdateBookingStatus', N'usp_ApproveAndPay', N'usp_ConfirmCashReceived', N'usp_AddReview', N'usp_GetMonthlyReport', N'usp_GetNotifications', N'usp_GetBookingsNeedingAttention', N'trg_Reviews_RatingSync', N'trg_Bookings_StatusFlow', N'trg_PaymentsDtl_Audit', N'trg_Users_StatusSync', N'trg_Customers_StatusCopy', N'trg_Providers_StatusCopy')
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
    EMAIL                        NVARCHAR(256)   NOT NULL,
    EMAIL_VERIFICATION_PENDING   BIT             NOT NULL,
    FIRST_NAME                   NVARCHAR(100)   NOT NULL,
    LAST_NAME                    NVARCHAR(100)   NOT NULL,
    MIDDLE_NAME                  NVARCHAR(100)   NULL,
    NOTIFICATIONS_SEEN_AT        DATETIME2       NULL,      -- v2.3: the bell; newer notifications are unread
    PASSWORD_HASH                NVARCHAR(255)   NOT NULL,
    PHONE_NUMBER                 NVARCHAR(11)    NULL,      -- Philippine mobile, 09XXXXXXXXX
    ROLE                         NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_USERS PRIMARY KEY (USER_ID),
    CONSTRAINT CK_USERS_ROLE CHECK ([ROLE] IN ('Customer','Service Provider','Administrator')),
    CONSTRAINT CK_USERS_STATUS CHECK ([ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated'))
);
CREATE UNIQUE INDEX IX_USERS_EMAIL ON USERS (EMAIL);
-- triggers on USERS: trg_Users_Audit, trg_Users_StatusSync

CREATE TABLE CUSTOMERS (
    CUSTOMER_ID                  INT             IDENTITY(1,1) NOT NULL,
    ACCOUNT_STATUS               NVARCHAR(50)    NOT NULL DEFAULT N'Active',   -- copy of USERS.ACCOUNT_STATUS
    ADDRESS                      NVARCHAR(255)   NULL,
    PROFILE_PHOTO                NVARCHAR(255)   NULL,
    USER_ID                      INT             NOT NULL,
    CONSTRAINT PK_CUSTOMERS PRIMARY KEY (CUSTOMER_ID),
    CONSTRAINT FK_CUSTOMERS_USERS_USER_ID FOREIGN KEY (USER_ID) REFERENCES USERS(USER_ID),
    CONSTRAINT CK_CUSTOMERS_STATUS CHECK ([ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated'))
);
CREATE UNIQUE INDEX IX_CUSTOMERS_USER_ID ON CUSTOMERS (USER_ID);
-- triggers on CUSTOMERS: trg_Customers_StatusCopy

CREATE TABLE SERVICE_PROVIDERS (
    PROVIDER_ID                  INT             IDENTITY(1,1) NOT NULL,
    ACCOUNT_STATUS               NVARCHAR(50)    NOT NULL DEFAULT N'Active',   -- copy of USERS.ACCOUNT_STATUS
    ADDRESS                      NVARCHAR(255)   NULL,
    APPROVAL_STATUS              NVARCHAR(50)    NOT NULL,
    AVG_RATING                   DECIMAL(2,1)    NULL,
    BASE_FEE                     DECIMAL(10,2)   NULL,
    BUSINESS_NAME                NVARCHAR(155)   NOT NULL,
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
    CONSTRAINT CK_PROVIDERS_LIMITS CHECK ([MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] BETWEEN 1 AND 20),   -- v2.6: at most 20 jobs a day
    CONSTRAINT CK_PROVIDERS_RATING CHECK ([AVG_RATING] IS NULL OR [AVG_RATING] BETWEEN 0 AND 5),
    CONSTRAINT CK_PROVIDERS_STATUS CHECK ([ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated'))
);
CREATE UNIQUE INDEX IX_SERVICE_PROVIDERS_USER_ID ON SERVICE_PROVIDERS (USER_ID);
-- triggers on SERVICE_PROVIDERS: trg_Reviews_RatingSync (writes AVG_RATING / REVIEW_COUNT), trg_Providers_StatusCopy

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

-- v2.7: a provider's request for 1 to 7 days off; its dates are its PROVIDER_UNAVAILABLE_DATES rows (below)
CREATE TABLE PROVIDER_DAY_OFF_REQUESTS (
    REQUEST_ID                   INT             IDENTITY(1,1) NOT NULL,
    DECIDED_BY_ACTION_ID         INT             NULL,              -- the administrator's answer (NULL while Pending)
    PROVIDER_ID                  INT             NOT NULL,
    REASON                       NVARCHAR(255)   NULL,
    REQUESTED_AT                 DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    STATUS                       NVARCHAR(20)    NOT NULL DEFAULT N'Pending',
    CONSTRAINT PK_PROVIDER_DAY_OFF_REQUESTS PRIMARY KEY (REQUEST_ID),
    CONSTRAINT FK_PROVIDER_DAY_OFF_REQUESTS_SERVICE_PROVIDERS_PROVIDER_ID FOREIGN KEY (PROVIDER_ID) REFERENCES SERVICE_PROVIDERS(PROVIDER_ID) ON DELETE CASCADE,
    CONSTRAINT CK_PROVIDER_DAY_OFF_REQUESTS_STATUS CHECK ([STATUS] IN ('Pending','Approved','Rejected')),
    CONSTRAINT CK_PROVIDER_DAY_OFF_REQUESTS_DECISION CHECK (([STATUS] = 'Pending' AND [DECIDED_BY_ACTION_ID] IS NULL) OR ([STATUS] <> 'Pending' AND [DECIDED_BY_ACTION_ID] IS NOT NULL))
);
CREATE UNIQUE INDEX IX_PROVIDER_DAY_OFF_REQUESTS_DECIDED_BY_ACTION_ID ON PROVIDER_DAY_OFF_REQUESTS (DECIDED_BY_ACTION_ID) WHERE [DECIDED_BY_ACTION_ID] IS NOT NULL;
CREATE INDEX IX_PROVIDER_DAY_OFF_REQUESTS_PROVIDER_ID_STATUS ON PROVIDER_DAY_OFF_REQUESTS (PROVIDER_ID, STATUS);
-- (its FOREIGN KEY to ADMIN_ACTIONS is added after ADMIN_ACTIONS, below)

CREATE TABLE PROVIDER_UNAVAILABLE_DATES (
    UNAVAILABLE_ID               INT             IDENTITY(1,1) NOT NULL,
    APPROVED_BY_ACTION_ID        INT             NULL,              -- v2.4: an extra day approved by an administrator
    PROVIDER_ID                  INT             NOT NULL,
    REASON                       NVARCHAR(255)   NULL,
    REQUEST_ID                   INT             NULL,              -- v2.7: a day of a provider's day-off request
    UNAVAILABLE_DATE             DATE            NOT NULL,
    CONSTRAINT PK_PROVIDER_UNAVAILABLE_DATES PRIMARY KEY (UNAVAILABLE_ID),
    CONSTRAINT FK_PROVIDER_UNAVAILABLE_DATES_SERVICE_PROVIDERS_PROVIDER_ID FOREIGN KEY (PROVIDER_ID) REFERENCES SERVICE_PROVIDERS(PROVIDER_ID),
    CONSTRAINT FK_PROVIDER_UNAVAILABLE_DATES_PROVIDER_DAY_OFF_REQUESTS_REQUEST_ID FOREIGN KEY (REQUEST_ID) REFERENCES PROVIDER_DAY_OFF_REQUESTS(REQUEST_ID),
    -- v2.7: a day comes from a request OR is an administrator's extra day, never both
    CONSTRAINT CK_PROVIDER_UNAVAILABLE_DATES_SOURCE CHECK ([REQUEST_ID] IS NULL OR [APPROVED_BY_ACTION_ID] IS NULL)
);
CREATE UNIQUE INDEX IX_PROVIDER_UNAVAILABLE_DATES_PROVIDER_ID_UNAVAILABLE_DATE ON PROVIDER_UNAVAILABLE_DATES (PROVIDER_ID, UNAVAILABLE_DATE);
CREATE INDEX IX_PROVIDER_UNAVAILABLE_DATES_APPROVED_BY_ACTION_ID ON PROVIDER_UNAVAILABLE_DATES (APPROVED_BY_ACTION_ID);
CREATE INDEX IX_PROVIDER_UNAVAILABLE_DATES_REQUEST_ID ON PROVIDER_UNAVAILABLE_DATES (REQUEST_ID);
CREATE UNIQUE INDEX IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE ON PROVIDER_UNAVAILABLE_DATES (UNAVAILABLE_DATE);   -- v2.5: one provider off per day
-- (its FOREIGN KEY to ADMIN_ACTIONS is added after ADMIN_ACTIONS, below)

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

CREATE TABLE PAYMENTS (                                     -- v2.3: the list of payment methods
    PAYMENT_ID                   INT             IDENTITY(1,1) NOT NULL,
    METHOD_NAME                  NVARCHAR(50)    NOT NULL,  -- 1 Cash on Completion, 2 GCash, 3 Maya
    CONSTRAINT PK_PAYMENTS PRIMARY KEY (PAYMENT_ID)
);
CREATE UNIQUE INDEX IX_PAYMENTS_METHOD_NAME ON PAYMENTS (METHOD_NAME);

CREATE TABLE PAYMENTS_DTL (                                 -- v2.3: each booking's payment (BOOKINGS <-> PAYMENTS)
    PAYMENT_DTL_ID               INT             IDENTITY(1,1) NOT NULL,
    BOOKING_ID                   INT             NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    PAID_AT                      DATETIME2       NULL,      -- cash: when the provider confirmed it
    PAYMENT_ID                   INT             NOT NULL,
    PAYMENT_STATUS               NVARCHAR(50)    NOT NULL,
    TRANSACTION_REFERENCE        NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_PAYMENTS_DTL PRIMARY KEY (PAYMENT_DTL_ID),
    CONSTRAINT FK_PAYMENTS_DTL_BOOKINGS_BOOKING_ID FOREIGN KEY (BOOKING_ID) REFERENCES BOOKINGS(BOOKING_ID),
    CONSTRAINT FK_PAYMENTS_DTL_PAYMENTS_PAYMENT_ID FOREIGN KEY (PAYMENT_ID) REFERENCES PAYMENTS(PAYMENT_ID),
    CONSTRAINT CK_PAYMENTS_DTL_PAID_AT CHECK ([PAYMENT_STATUS] = 'PendingCollection' OR [PAID_AT] IS NOT NULL),
    -- only cash (PAYMENTS row 1, Cash on Completion) can wait to be collected
    CONSTRAINT CK_PAYMENTS_DTL_PENDING_CASH CHECK ([PAYMENT_STATUS] <> 'PendingCollection' OR [PAYMENT_ID] = 1),
    CONSTRAINT CK_PAYMENTS_DTL_STATUS CHECK ([PAYMENT_STATUS] IN ('PendingCollection','Paid','Refunded'))
);
CREATE UNIQUE INDEX IX_PAYMENTS_DTL_BOOKING_ID ON PAYMENTS_DTL (BOOKING_ID);
CREATE INDEX IX_PAYMENTS_DTL_PAYMENT_ID ON PAYMENTS_DTL (PAYMENT_ID);
CREATE UNIQUE INDEX IX_PAYMENTS_DTL_TRANSACTION_REFERENCE ON PAYMENTS_DTL (TRANSACTION_REFERENCE);
-- triggers on PAYMENTS_DTL: trg_PaymentsDtl_Audit

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

CREATE TABLE ADMIN_ACTIONS (
    ACTION_ID                    INT             IDENTITY(1,1) NOT NULL,
    ACTION_TYPE                  NVARCHAR(50)    NOT NULL,
    ADMIN_USER_ID                INT             NOT NULL,
    CREATED_AT                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    REASON                       NVARCHAR(255)   NULL,
    TARGET_CATEGORY_ID           INT             NULL,      -- v2.2: one FOREIGN KEY column per kind of target
    TARGET_CUSTOMER_ID           INT             NULL,
    TARGET_PROVIDER_ID           INT             NULL,
    TARGET_REVIEW_ID             INT             NULL,
    TARGET_TYPE                  NVARCHAR(50)    NOT NULL,
    CONSTRAINT PK_ADMIN_ACTIONS PRIMARY KEY (ACTION_ID),
    CONSTRAINT FK_ADMIN_ACTIONS_USERS_ADMIN_USER_ID FOREIGN KEY (ADMIN_USER_ID) REFERENCES USERS(USER_ID),
    CONSTRAINT FK_ADMIN_ACTIONS_CUSTOMERS_TARGET_CUSTOMER_ID FOREIGN KEY (TARGET_CUSTOMER_ID) REFERENCES CUSTOMERS(CUSTOMER_ID),
    CONSTRAINT FK_ADMIN_ACTIONS_SERVICE_PROVIDERS_TARGET_PROVIDER_ID FOREIGN KEY (TARGET_PROVIDER_ID) REFERENCES SERVICE_PROVIDERS(PROVIDER_ID),
    CONSTRAINT FK_ADMIN_ACTIONS_REVIEWS_TARGET_REVIEW_ID FOREIGN KEY (TARGET_REVIEW_ID) REFERENCES REVIEWS(REVIEW_ID),
    CONSTRAINT FK_ADMIN_ACTIONS_SERVICE_CATEGORIES_TARGET_CATEGORY_ID FOREIGN KEY (TARGET_CATEGORY_ID) REFERENCES SERVICE_CATEGORIES(CATEGORY_ID),
    CONSTRAINT CK_ADMIN_ACTIONS_TARGET_TYPE CHECK ([TARGET_TYPE] IN ('CUSTOMER','SERVICE_PROVIDER','REVIEW','CATEGORY')),
    -- exactly one target column is filled, and it is the one TARGET_TYPE names
    CONSTRAINT CK_ADMIN_ACTIONS_TARGET CHECK (([TARGET_TYPE] = 'CUSTOMER' AND [TARGET_CUSTOMER_ID] IS NOT NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'SERVICE_PROVIDER' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NOT NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'REVIEW' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NOT NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'CATEGORY' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NOT NULL))
);
CREATE INDEX IX_ADMIN_ACTIONS_ADMIN_USER_ID ON ADMIN_ACTIONS (ADMIN_USER_ID);
CREATE INDEX IX_ADMIN_ACTIONS_TARGET_CATEGORY_ID ON ADMIN_ACTIONS (TARGET_CATEGORY_ID);
CREATE INDEX IX_ADMIN_ACTIONS_TARGET_CUSTOMER_ID ON ADMIN_ACTIONS (TARGET_CUSTOMER_ID);
CREATE INDEX IX_ADMIN_ACTIONS_TARGET_PROVIDER_ID ON ADMIN_ACTIONS (TARGET_PROVIDER_ID);
CREATE INDEX IX_ADMIN_ACTIONS_TARGET_REVIEW_ID ON ADMIN_ACTIONS (TARGET_REVIEW_ID);
-- v2.4 (EF migration "ExtraDayOff")
ALTER TABLE PROVIDER_UNAVAILABLE_DATES ADD CONSTRAINT FK_PROVIDER_UNAVAILABLE_DATES_ADMIN_ACTIONS_APPROVED_BY_ACTION_ID
    FOREIGN KEY (APPROVED_BY_ACTION_ID) REFERENCES ADMIN_ACTIONS(ACTION_ID);
-- v2.7 (EF migration "DayOffRequests")
ALTER TABLE PROVIDER_DAY_OFF_REQUESTS ADD CONSTRAINT FK_PROVIDER_DAY_OFF_REQUESTS_ADMIN_ACTIONS_DECIDED_BY_ACTION_ID
    FOREIGN KEY (DECIDED_BY_ACTION_ID) REFERENCES ADMIN_ACTIONS(ACTION_ID);

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
               WHERE u.PROVIDER_ID = @ProviderId AND u.UNAVAILABLE_DATE = @BookingDate
                 AND (u.REQUEST_ID IS NULL OR EXISTS (SELECT 1 FROM dbo.PROVIDER_DAY_OFF_REQUESTS r WHERE r.REQUEST_ID = u.REQUEST_ID AND r.STATUS = N'Approved')))   -- v2.7: a Pending day-off request does not block yet
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
   Times are stored in UTC; CREATED_AT_CEBU shows them in Cebu time.
   v2.3: the payment comes from PAYMENTS_DTL, its method name from the PAYMENTS list. IS_COLLECTED_BY_PROVIDER is
   calculated (a cash payment that is Paid), no longer stored. */
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
       m.METHOD_NAME AS PAYMENT_METHOD,
       pay.PAYMENT_STATUS AS PAYMENT_RECORD_STATUS,
       pay.TRANSACTION_REFERENCE,
       CAST(CASE WHEN m.METHOD_NAME = N'Cash on Completion' AND pay.PAYMENT_STATUS = N'Paid' THEN 1
                 WHEN m.METHOD_NAME = N'Cash on Completion' THEN 0 END AS BIT) AS IS_COLLECTED_BY_PROVIDER,
       b.SERVICE_LOCATION,
       DATEADD(HOUR, 8, b.CREATED_AT) AS CREATED_AT_CEBU
FROM dbo.BOOKINGS b
JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
JOIN dbo.USERS cu ON cu.USER_ID = c.USER_ID
JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = b.PROVIDER_ID
LEFT JOIN dbo.PAYMENTS_DTL pay ON pay.BOOKING_ID = b.BOOKING_ID
LEFT JOIN dbo.PAYMENTS m ON m.PAYMENT_ID = pay.PAYMENT_ID;
GO

-- vw_ProviderDirectory
CREATE OR ALTER VIEW dbo.vw_ProviderDirectory
AS
/* SERVISYO-DB-V2
   Providers customers may see (Approved + Active account), with their rating and active services. */
SELECT p.PROVIDER_ID,
       p.BUSINESS_NAME,
       u.FIRST_NAME + N' ' + u.LAST_NAME AS OWNER_NAME,
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
    -- (the parameters are larger than the columns on purpose: too-long text is refused with a message below
    --  instead of being cut off silently)
    SET @FirstName    = NULLIF(LTRIM(RTRIM(@FirstName)), N'');
    SET @MiddleName   = NULLIF(LTRIM(RTRIM(@MiddleName)), N'');
    SET @LastName     = NULLIF(LTRIM(RTRIM(@LastName)), N'');
    SET @Email        = LOWER(NULLIF(LTRIM(RTRIM(@Email)), N''));
    SET @PasswordHash = NULLIF(LTRIM(RTRIM(@PasswordHash)), N'');
    SET @PhoneNumber  = NULLIF(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@PhoneNumber, N' ', N''), N'-', N''), N'.', N''), N'(', N''), N')', N''), CHAR(9), N''), N'');
    -- Philippine mobile number, saved as 09XXXXXXXXX: +639XXXXXXXXX and 639XXXXXXXXX are turned into 09XXXXXXXXX
    IF LEFT(@PhoneNumber, 3) = N'+63' SET @PhoneNumber = N'0' + SUBSTRING(@PhoneNumber, 4, 50);
    ELSE IF LEFT(@PhoneNumber, 2) = N'63' AND LEN(@PhoneNumber) = 12 SET @PhoneNumber = N'0' + SUBSTRING(@PhoneNumber, 3, 50);
    SET @Address      = NULLIF(LTRIM(RTRIM(@Address)), N'');

    IF @FirstName IS NULL OR @LastName IS NULL THROW 50102, N'Please enter your first and last name.', 1;
    IF LEN(@FirstName) > 100 OR LEN(@LastName) > 100 OR LEN(ISNULL(@MiddleName, N'')) > 100 THROW 50102, N'Names can be at most 100 characters.', 1;
    IF @FirstName LIKE N'%[0-9]%' OR @LastName LIKE N'%[0-9]%' OR ISNULL(@MiddleName, N'') LIKE N'%[0-9]%' THROW 50102, N'Please enter a valid name containing letters only.', 1;
    IF @Email IS NULL OR LEN(@Email) > 256 OR @Email NOT LIKE N'_%@_%._%' OR @Email LIKE N'%@%@%' THROW 50103, N'Please enter a valid email address.', 1;
    -- the part after @ must be letters, dots and hyphens only (no numbers), and every part at least 2 letters (not a.c)
    DECLARE @Domain NVARCHAR(400) = N'.' + SUBSTRING(@Email, CHARINDEX(N'@', @Email) + 1, 400) + N'.';
    IF @Domain LIKE N'%[^a-z.-]%' OR @Domain LIKE N'%..%' OR @Domain LIKE N'%.[a-z].%' OR @Domain LIKE N'%.-%' OR @Domain LIKE N'%-.%'
       OR LEFT(@Email, CHARINDEX(N'@', @Email) - 1) LIKE N'%[^a-z0-9._%+-]%'
        THROW 50103, N'Please enter a valid email address (for example juan@gmail.com).', 1;
    IF @PasswordHash IS NULL OR LEN(@PasswordHash) < 20 OR LEN(@PasswordHash) > 255 THROW 50104, N'The password could not be saved. Please try again.', 1;
    IF @PhoneNumber IS NULL THROW 50105, N'Please enter your phone number.', 1;
    IF LEN(@PhoneNumber) <> 11 OR @PhoneNumber NOT LIKE N'09[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]' THROW 50105, N'Please enter an 11-digit mobile number, for example 09171234567.', 1;
    IF @Address IS NULL THROW 50106, N'Please enter your street address and barangay.', 1;
    IF LEN(@Address) > 255 THROW 50106, N'The address can be at most 255 characters.', 1;
    IF @Address NOT LIKE N'%[a-z]%' THROW 50106, N'Please enter a real address (it cannot be numbers only).', 1;

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

        INSERT INTO dbo.CUSTOMERS (USER_ID, ADDRESS, ACCOUNT_STATUS)
        VALUES (@NewUserId, @Address, N'Active');
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
    @BaseFee       DECIMAL(12,2),
    @MinNoticeDays INT = 1,
    @MaxJobsPerDay INT = 5,
    @NewUserId     INT = NULL OUTPUT,
    @NewProviderId INT = NULL OUTPUT
AS
BEGIN
    /* SERVISYO-DB-V2
       Service-provider sign-up in ONE call: USERS + SERVICE_PROVIDERS (APPROVAL_STATUS = Pending until an
       administrator decides), all or nothing. The administrators' bell shows it (usp_GetNotifications).
       Used by the website (AuthService.RegisterProviderAsync). */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @FirstName    = NULLIF(LTRIM(RTRIM(@FirstName)), N'');
    SET @MiddleName   = NULLIF(LTRIM(RTRIM(@MiddleName)), N'');
    SET @LastName     = NULLIF(LTRIM(RTRIM(@LastName)), N'');
    SET @Email        = LOWER(NULLIF(LTRIM(RTRIM(@Email)), N''));
    SET @PasswordHash = NULLIF(LTRIM(RTRIM(@PasswordHash)), N'');
    SET @PhoneNumber  = NULLIF(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@PhoneNumber, N' ', N''), N'-', N''), N'.', N''), N'(', N''), N')', N''), CHAR(9), N''), N'');
    -- Philippine mobile number, saved as 09XXXXXXXXX: +639XXXXXXXXX and 639XXXXXXXXX are turned into 09XXXXXXXXX
    IF LEFT(@PhoneNumber, 3) = N'+63' SET @PhoneNumber = N'0' + SUBSTRING(@PhoneNumber, 4, 50);
    ELSE IF LEFT(@PhoneNumber, 2) = N'63' AND LEN(@PhoneNumber) = 12 SET @PhoneNumber = N'0' + SUBSTRING(@PhoneNumber, 3, 50);
    SET @BusinessName = NULLIF(LTRIM(RTRIM(@BusinessName)), N'');
    SET @Description  = NULLIF(LTRIM(RTRIM(@Description)), N'');
    SET @Address      = NULLIF(LTRIM(RTRIM(@Address)), N'');

    IF @FirstName IS NULL OR @LastName IS NULL THROW 50102, N'Please enter your first and last name.', 1;
    IF @MiddleName IS NULL THROW 50102, N'Please enter your middle name.', 1;
    IF LEN(@FirstName) > 100 OR LEN(@LastName) > 100 OR LEN(@MiddleName) > 100 THROW 50102, N'Names can be at most 100 characters.', 1;
    IF @FirstName LIKE N'%[0-9]%' OR @LastName LIKE N'%[0-9]%' OR @MiddleName LIKE N'%[0-9]%' THROW 50102, N'Please enter a valid name containing letters only.', 1;
    IF @Email IS NULL OR LEN(@Email) > 256 OR @Email NOT LIKE N'_%@_%._%' OR @Email LIKE N'%@%@%' THROW 50103, N'Please enter a valid email address.', 1;
    -- the part after @ must be letters, dots and hyphens only (no numbers), and every part at least 2 letters (not a.c)
    DECLARE @Domain NVARCHAR(400) = N'.' + SUBSTRING(@Email, CHARINDEX(N'@', @Email) + 1, 400) + N'.';
    IF @Domain LIKE N'%[^a-z.-]%' OR @Domain LIKE N'%..%' OR @Domain LIKE N'%.[a-z].%' OR @Domain LIKE N'%.-%' OR @Domain LIKE N'%-.%'
       OR LEFT(@Email, CHARINDEX(N'@', @Email) - 1) LIKE N'%[^a-z0-9._%+-]%'
        THROW 50103, N'Please enter a valid email address (for example juan@gmail.com).', 1;
    IF @PasswordHash IS NULL OR LEN(@PasswordHash) < 20 OR LEN(@PasswordHash) > 255 THROW 50104, N'The password could not be saved. Please try again.', 1;
    IF @PhoneNumber IS NULL THROW 50105, N'Please enter your phone number.', 1;
    IF LEN(@PhoneNumber) <> 11 OR @PhoneNumber NOT LIKE N'09[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]' THROW 50105, N'Please enter an 11-digit mobile number, for example 09171234567.', 1;
    IF @BusinessName IS NULL THROW 50107, N'Please enter your business name.', 1;
    IF LEN(@BusinessName) > 155 THROW 50107, N'The business name can be at most 155 characters.', 1;
    IF @Description IS NULL THROW 50108, N'Please describe your services.', 1;
    IF LEN(@Description) > 255 THROW 50108, N'The description can be at most 255 characters.', 1;
    IF @Address IS NULL THROW 50106, N'Please enter your business address.', 1;
    IF LEN(@Address) > 255 THROW 50106, N'The address can be at most 255 characters.', 1;
    IF @Address NOT LIKE N'%[a-z]%' THROW 50106, N'Please enter a real address (it cannot be numbers only).', 1;
    IF @BaseFee IS NULL THROW 50109, N'Please enter your base fee.', 1;
    IF @BaseFee < 0 OR @BaseFee > 1000000 THROW 50109, N'Enter a base fee from 0 to 1,000,000.', 1;
    IF @MinNoticeDays IS NULL OR @MinNoticeDays < 0 OR @MinNoticeDays > 30 THROW 50110, N'Enter a whole number of notice days from 0 to 30.', 1;
    IF @MaxJobsPerDay IS NULL OR @MaxJobsPerDay < 1 OR @MaxJobsPerDay > 20 THROW 50110, N'Enter a whole number of jobs per day from 1 to 20.', 1;

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

        INSERT INTO dbo.SERVICE_PROVIDERS (USER_ID, BUSINESS_NAME, DESCRIPTION, ADDRESS, BASE_FEE,
                                           AVG_RATING, REVIEW_COUNT, MIN_NOTICE_DAYS, MAX_JOBS_PER_DAY,
                                           APPROVAL_STATUS, DATE_APPLIED, DATE_APPROVED, ACCOUNT_STATUS)
        VALUES (@NewUserId, @BusinessName, @Description, @Address, @BaseFee,
                NULL, 0, @MinNoticeDays, @MaxJobsPerDay,
                N'Pending', SYSUTCDATETIME(), NULL, N'Active');
        SET @NewProviderId = CAST(SCOPE_IDENTITY() AS INT);

        -- v2.3: nothing is stored for the administrators' bell. usp_GetNotifications shows every Pending
        -- application straight from SERVICE_PROVIDERS.

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
       status (+ DATE_APPROVED) and the ADMIN_ACTIONS log, all or nothing. The provider's bell shows the decision.
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

        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_PROVIDER_ID, REASON, CREATED_AT)
        VALUES (@AdminUserId, @Decision, N'SERVICE_PROVIDER', @ProviderId, @Reason, SYSUTCDATETIME());

        -- v2.3: the provider's bell shows this decision from the ADMIN_ACTIONS row above (usp_GetNotifications)

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
       Deactivated -> Active. Administrators cannot be changed here (and nobody can change their own account).
       The copies CUSTOMERS.ACCOUNT_STATUS / SERVICE_PROVIDERS.ACCOUNT_STATUS follow automatically (trg_Users_StatusSync).
       v2.2: the ADMIN_ACTIONS row points to the CUSTOMER (TARGET_CUSTOMER_ID) or the SERVICE_PROVIDER (TARGET_PROVIDER_ID).
       When a SERVICE PROVIDER is suspended or deactivated, their Pending bookings are cancelled and those customers are
       told; Accepted / In Progress bookings are NOT changed (they may already be paid) but are returned as the result
       so the administrator can follow them up. */
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

        -- the customer / provider row the ADMIN_ACTIONS log points to (v2.2)
        DECLARE @CustomerId INT = (SELECT CUSTOMER_ID FROM dbo.CUSTOMERS WHERE USER_ID = @TargetUserId);
        DECLARE @ProviderId INT = (SELECT PROVIDER_ID FROM dbo.SERVICE_PROVIDERS WHERE USER_ID = @TargetUserId);
        IF (@Role = N'Customer' AND @CustomerId IS NULL) OR (@Role = N'Service Provider' AND @ProviderId IS NULL)
            THROW 50116, N'That customer or provider account was not found.', 1;

        IF NOT (   (@Current = N'Active'      AND @NewStatus IN (N'Suspended', N'Deactivated'))
                OR (@Current = N'Suspended'   AND @NewStatus IN (N'Active', N'Deactivated'))
                OR (@Current = N'Deactivated' AND @NewStatus = N'Active'))
            THROW 50117, N'That status change is not allowed from the account''s current status.', 1;

        UPDATE dbo.USERS SET ACCOUNT_STATUS = @NewStatus WHERE USER_ID = @TargetUserId;

        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_CUSTOMER_ID, TARGET_PROVIDER_ID, REASON, CREATED_AT)
        VALUES (@AdminUserId, N'Account ' + @NewStatus,
                CASE WHEN @Role = N'Customer' THEN N'CUSTOMER' ELSE N'SERVICE_PROVIDER' END,
                CASE WHEN @Role = N'Customer' THEN @CustomerId END,
                CASE WHEN @Role = N'Service Provider' THEN @ProviderId END,
                @Reason, SYSUTCDATETIME());

        -- a provider who can no longer work: cancel their Pending bookings. v2.3: those customers' bells show
        -- the cancellation from BOOKINGS + this ADMIN_ACTIONS row (usp_GetNotifications); nothing else is stored.
        DECLARE @Cancelled TABLE (BOOKING_ID INT PRIMARY KEY, CUSTOMER_USER_ID INT, BOOKING_DATE DATE);
        IF @Role = N'Service Provider' AND @NewStatus IN (N'Suspended', N'Deactivated') AND @ProviderId IS NOT NULL
        BEGIN
            UPDATE b
            SET b.STATUS = N'Cancelled', b.UPDATED_AT = SYSUTCDATETIME()
            OUTPUT inserted.BOOKING_ID, c.USER_ID, inserted.BOOKING_DATE INTO @Cancelled (BOOKING_ID, CUSTOMER_USER_ID, BOOKING_DATE)
            FROM dbo.BOOKINGS b
            JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
            WHERE b.PROVIDER_ID = @ProviderId AND b.STATUS = N'Pending';
        END

        IF @tc = 0 COMMIT TRANSACTION;

        -- result 1: what happened; result 2: bookings the administrator still has to follow up (empty for customers)
        SELECT @TargetUserId AS USER_ID, @NewStatus AS ACCOUNT_STATUS, (SELECT COUNT(*) FROM @Cancelled) AS CANCELLED_PENDING_BOOKINGS;
        SELECT b.BOOKING_ID, b.STATUS, b.BOOKING_DATE, b.BOOKING_TIME, b.PAYMENT_STATUS
        FROM dbo.BOOKINGS b
        WHERE @Role = N'Service Provider' AND @NewStatus IN (N'Suspended', N'Deactivated')
          AND b.PROVIDER_ID = @ProviderId AND b.STATUS IN (N'Accepted', N'In Progress')
        ORDER BY b.BOOKING_DATE, b.BOOKING_TIME;
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

        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_REVIEW_ID, REASON, CREATED_AT)
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
       the other (no double booking). New bookings start as Pending / Unpaid; the provider's bell shows them. */
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
        IF EXISTS (SELECT 1 FROM dbo.PROVIDER_UNAVAILABLE_DATES u WHERE u.PROVIDER_ID = @ProviderId AND u.UNAVAILABLE_DATE = @BookingDate
                   AND (u.REQUEST_ID IS NULL OR EXISTS (SELECT 1 FROM dbo.PROVIDER_DAY_OFF_REQUESTS r WHERE r.REQUEST_ID = u.REQUEST_ID AND r.STATUS = N'Approved')))   -- v2.7: a Pending day-off request does not block yet
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

        -- v2.3: the provider's bell shows the new Pending booking itself (usp_GetNotifications); nothing else is stored

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

        -- v2.3: the provider's bell shows the cancellation from BOOKINGS (usp_GetNotifications)

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
       The customer's bell shows the PDF's "Your booking #N is now <status>." (usp_GetNotifications). Customers cancel with usp_CancelBooking. */
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
           AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL WHERE BOOKING_ID = @BookingId AND PAYMENT_STATUS = N'PendingCollection')
            THROW 50144, N'The booking is waiting for the customer to approve and pay the quotation, so it can''t start yet.', 1;

        IF @NewStatus = N'Accepted'
           AND dbo.fn_HasBookingConflict(@ProviderId, @Date, @Time, @Minutes, @BookingId, 0) = 1
            THROW 50145, N'This booking overlaps another job that is already on your schedule.', 1;

        UPDATE dbo.BOOKINGS
        SET STATUS = @NewStatus, UPDATED_AT = SYSUTCDATETIME()
        WHERE BOOKING_ID = @BookingId;

        -- v2.3: the customer's bell shows the new status from BOOKINGS (usp_GetNotifications)

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
         CashOnCompletion -> PAYMENTS_DTL row with PAYMENTS 'Cash on Completion' (PendingCollection,
                             ref CASH-<year>-<booking>); BOOKINGS.PAYMENT_STATUS stays Unpaid until the provider
                             confirms the cash.
         MockEWallet      -> PAYMENTS_DTL row with PAYMENTS 'GCash' or 'Maya' (Paid, ref GCASH-######## /
                             MAYA-########); BOOKINGS.PAYMENT_STATUS = Paid.
       v2.3: the amount is not stored again: it is always the booking's TOTAL_FEE. The provider's bell shows the
       approval from PAYMENTS_DTL (usp_GetNotifications). */
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
        IF @PayStatus = N'Paid' OR EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL WITH (UPDLOCK, HOLDLOCK) WHERE BOOKING_ID = @BookingId)
            THROW 50147, N'This booking is already paid (or cash on completion was already chosen).', 1;
        IF @Status <> N'Accepted' OR @PayStatus <> N'Unpaid'
            THROW 50148, N'Only an Accepted quotation that is not paid yet can be approved and paid.', 1;

        DECLARE @IsCash BIT = CASE WHEN @PaymentMethod = N'CashOnCompletion' THEN 1 ELSE 0 END;

        -- the payment method row in the PAYMENTS list (referenced, never copied)
        DECLARE @PaymentId INT = (SELECT PAYMENT_ID FROM dbo.PAYMENTS
                                  WHERE METHOD_NAME = CASE WHEN @IsCash = 1 THEN N'Cash on Completion' ELSE @Wallet END);
        IF @PaymentId IS NULL
            THROW 50146, N'That payment method is not in the PAYMENTS list.', 1;
        DECLARE @Prefix NVARCHAR(10) = CASE WHEN @Wallet = N'Maya' THEN N'MAYA' ELSE N'GCASH' END;
        DECLARE @Tries INT = 0;

        IF @IsCash = 1
            SET @TransactionReference = CONCAT(N'CASH-', YEAR(dbo.fn_ManilaNow()), N'-',
                CASE WHEN @BookingId < 10000 THEN RIGHT(CONCAT(N'0000', @BookingId), 4) ELSE CAST(@BookingId AS NVARCHAR(12)) END);
        ELSE
        BEGIN
            -- a test reference: the wallet prefix and 8 random digits, never used before
            SET @TransactionReference = CONCAT(@Prefix, N'-', RIGHT(CONCAT(N'0000000', ABS(CAST(CHECKSUM(NEWID()) AS BIGINT)) % 100000000), 8));
            WHILE EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL WHERE TRANSACTION_REFERENCE = @TransactionReference) AND @Tries < 10
            BEGIN
                SET @Tries += 1;
                SET @TransactionReference = CONCAT(@Prefix, N'-', RIGHT(CONCAT(N'0000000', ABS(CAST(CHECKSUM(NEWID()) AS BIGINT)) % 100000000), 8));
            END
        END

        INSERT INTO dbo.PAYMENTS_DTL (BOOKING_ID, PAYMENT_ID, PAYMENT_STATUS, TRANSACTION_REFERENCE, CREATED_AT, PAID_AT)
        VALUES (@BookingId, @PaymentId,
                CASE WHEN @IsCash = 1 THEN N'PendingCollection' ELSE N'Paid' END,
                @TransactionReference, SYSUTCDATETIME(),
                CASE WHEN @IsCash = 1 THEN NULL ELSE SYSUTCDATETIME() END);

        UPDATE dbo.BOOKINGS
        SET PAYMENT_STATUS = CASE WHEN @IsCash = 1 THEN PAYMENT_STATUS ELSE N'Paid' END,
            UPDATED_AT = SYSUTCDATETIME()
        WHERE BOOKING_ID = @BookingId;

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
       PAYMENTS_DTL -> Paid + PAID_AT, BOOKINGS.PAYMENT_STATUS -> Paid. v2.3: "cash collected" is not stored again:
       it is a cash payment whose status is Paid, collected at PAID_AT. The customer's bell shows it from
       PAYMENTS_DTL (usp_GetNotifications). */
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

        DECLARE @Status NVARCHAR(50), @PaymentDtlId INT, @Method NVARCHAR(50), @PayStatus NVARCHAR(50);
        SELECT @Status = b.STATUS,
               @PaymentDtlId = d.PAYMENT_DTL_ID, @Method = m.METHOD_NAME, @PayStatus = d.PAYMENT_STATUS
        FROM dbo.BOOKINGS b WITH (UPDLOCK)
        LEFT JOIN dbo.PAYMENTS_DTL d WITH (UPDLOCK) ON d.BOOKING_ID = b.BOOKING_ID
        LEFT JOIN dbo.PAYMENTS m ON m.PAYMENT_ID = d.PAYMENT_ID
        WHERE b.BOOKING_ID = @BookingId
          AND b.PROVIDER_ID = @ProviderId;

        IF @Status IS NULL
            THROW 50139, N'That booking could not be found.', 1;
        IF @PaymentDtlId IS NULL OR @Method <> N'Cash on Completion'
            THROW 50149, N'This booking is not a cash-on-completion booking.', 1;
        IF @PayStatus <> N'PendingCollection'
            THROW 50150, N'The cash for this booking was already confirmed.', 1;
        IF @Status <> N'Completed'
            THROW 50151, N'Confirm the cash after you mark the job completed.', 1;

        -- one moment for both rows, so the customer's bell shows the cash confirmation as this booking's latest news
        DECLARE @Now DATETIME2 = SYSUTCDATETIME();
        UPDATE dbo.PAYMENTS_DTL SET PAYMENT_STATUS = N'Paid', PAID_AT = @Now WHERE PAYMENT_DTL_ID = @PaymentDtlId;
        UPDATE dbo.BOOKINGS SET PAYMENT_STATUS = N'Paid', UPDATED_AT = @Now WHERE BOOKING_ID = @BookingId;

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
       The provider's AVG_RATING and REVIEW_COUNT are updated by trg_Reviews_RatingSync; the provider's bell shows the review. */
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

        -- v2.3: the provider's bell shows the review from REVIEWS (usp_GetNotifications)

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

    -- v2.3: the method comes from the PAYMENTS list, the amount from the booking (never stored twice)
    SELECT m.METHOD_NAME AS PAYMENT_METHOD, d.PAYMENT_STATUS, COUNT(*) AS PAYMENTS, SUM(b.TOTAL_FEE) AS AMOUNT
    FROM dbo.PAYMENTS_DTL d
    JOIN dbo.PAYMENTS m ON m.PAYMENT_ID = d.PAYMENT_ID
    JOIN dbo.BOOKINGS b ON b.BOOKING_ID = d.BOOKING_ID
    WHERE b.BOOKING_DATE >= @First AND b.BOOKING_DATE < @Next
    GROUP BY m.PAYMENT_ID, m.METHOD_NAME, d.PAYMENT_STATUS
    ORDER BY m.PAYMENT_ID, d.PAYMENT_STATUS;
END
GO

-- usp_GetNotifications
CREATE OR ALTER PROCEDURE dbo.usp_GetNotifications
    @UserId INT,
    @Top    INT = 25
AS
BEGIN
    /* SERVISYO-DB-V2  (v2.3: replaces the NOTIFICATIONS table - nothing is stored, so nothing piles up)
       The bell of ONE user, worked out from the data itself, newest first, at most 25 rows:
         customer      - the provider's answer and the job's progress ("Your booking #N is now ..."), a booking
                         cancelled because its provider was suspended or deactivated, and cash the provider
                         confirmed receiving. Each booking shows only its latest news.
         provider      - new booking requests still waiting for an answer, quotations the customer approved
                         (and paid), bookings the customer cancelled, reviews, and the decision on the application.
         administrator - every provider application that is still Pending.
       IsUnread = newer than USERS.NOTIFICATIONS_SEEN_AT (set when the user opens the bell or marks all as read).
       An unknown or inactive account gets no rows. Times are UTC. The column names match the app's C# class. */
    SET NOCOUNT ON;

    IF @Top IS NULL OR @Top < 1 OR @Top > 25 SET @Top = 25;

    DECLARE @Role NVARCHAR(50), @SeenAt DATETIME2, @CustomerId INT, @ProviderId INT;
    SELECT @Role = u.ROLE, @SeenAt = u.NOTIFICATIONS_SEEN_AT, @CustomerId = c.CUSTOMER_ID, @ProviderId = p.PROVIDER_ID
    FROM dbo.USERS u
    LEFT JOIN dbo.CUSTOMERS c ON c.USER_ID = u.USER_ID
    LEFT JOIN dbo.SERVICE_PROVIDERS p ON p.USER_ID = u.USER_ID
    WHERE u.USER_ID = @UserId
      AND u.ACCOUNT_STATUS = N'Active';
    -- @Role stays NULL for an unknown or inactive account, so every part below returns nothing

    DECLARE @Peso NCHAR(1) = NCHAR(8369);   -- the peso sign, written as a number so the file's encoding never matters

    WITH Pay AS (
        -- each booking's payment with the name of its method (PAYMENTS list)
        SELECT d.BOOKING_ID, d.PAYMENT_STATUS, d.TRANSACTION_REFERENCE, d.CREATED_AT, d.PAID_AT, m.METHOD_NAME,
               CASE WHEN m.METHOD_NAME = N'Cash on Completion' THEN 1 ELSE 0 END AS IS_CASH
        FROM dbo.PAYMENTS_DTL d
        JOIN dbo.PAYMENTS m ON m.PAYMENT_ID = d.PAYMENT_ID
    ),
    SuspensionCancel AS (
        -- bookings cancelled automatically when an administrator suspended or deactivated their provider
        -- (usp_UpdateAccountStatus and the app cancel them within the same second as the ADMIN_ACTIONS row)
        SELECT b.BOOKING_ID
        FROM dbo.BOOKINGS b
        WHERE b.STATUS = N'Cancelled'
          AND EXISTS (SELECT 1 FROM dbo.ADMIN_ACTIONS a
                      WHERE a.TARGET_PROVIDER_ID = b.PROVIDER_ID
                        AND a.ACTION_TYPE IN (N'Account Suspended', N'Account Deactivated')
                        AND b.UPDATED_AT BETWEEN DATEADD(SECOND, -5, a.CREATED_AT) AND DATEADD(SECOND, 5, a.CREATED_AT))
    ),
    Events AS (
        -- ===== customer: the provider's answer and the job's progress (the booking's status now)
        SELECT CONCAT(N'B', b.BOOKING_ID) AS GRP, 2 AS PRIORITY, N'booking-update' AS KIND,
               CONCAT(N'Your booking #', b.BOOKING_ID, N' is now ', b.STATUS, N'.') AS MESSAGE,
               b.UPDATED_AT AS CREATED_AT, b.BOOKING_ID
        FROM dbo.BOOKINGS b
        WHERE @Role = N'Customer' AND b.CUSTOMER_ID = @CustomerId
          AND (   b.STATUS IN (N'Rejected', N'In Progress', N'Completed')
               OR (b.STATUS = N'Accepted' AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL d WHERE d.BOOKING_ID = b.BOOKING_ID)))
        UNION ALL
        -- customer: cancelled because the service provider can no longer work
        SELECT CONCAT(N'B', b.BOOKING_ID), 1, N'booking-cancelled',
               CONCAT(N'Your booking #', b.BOOKING_ID, N' on ', FORMAT(b.BOOKING_DATE, 'MMM d, yyyy', 'en-US'),
                      N' was cancelled because the service provider is no longer available. Please book another provider.'),
               b.UPDATED_AT, b.BOOKING_ID
        FROM dbo.BOOKINGS b
        JOIN SuspensionCancel sc ON sc.BOOKING_ID = b.BOOKING_ID
        WHERE @Role = N'Customer' AND b.CUSTOMER_ID = @CustomerId
        UNION ALL
        -- customer: the provider confirmed receiving the cash
        SELECT CONCAT(N'B', b.BOOKING_ID), 1, N'booking-update',
               CONCAT(sp.BUSINESS_NAME, N' confirmed receiving your ', @Peso, FORMAT(b.TOTAL_FEE, '#,##0.##', 'en-US'),
                      N' cash payment for booking #', b.BOOKING_ID, N' (ref ', pay.TRANSACTION_REFERENCE, N').'),
               pay.PAID_AT, b.BOOKING_ID
        FROM dbo.BOOKINGS b
        JOIN Pay pay ON pay.BOOKING_ID = b.BOOKING_ID
        JOIN dbo.SERVICE_PROVIDERS sp ON sp.PROVIDER_ID = b.PROVIDER_ID
        WHERE @Role = N'Customer' AND b.CUSTOMER_ID = @CustomerId
          AND pay.IS_CASH = 1 AND pay.PAYMENT_STATUS = N'Paid' AND pay.PAID_AT IS NOT NULL
        UNION ALL
        -- ===== provider: a request that is still waiting for an answer
        SELECT CONCAT(N'B', b.BOOKING_ID), 3, N'booking-new',
               CONCAT(N'You have a new booking request (Booking #', b.BOOKING_ID, N').'),
               b.CREATED_AT, b.BOOKING_ID
        FROM dbo.BOOKINGS b
        WHERE @Role = N'Service Provider' AND b.PROVIDER_ID = @ProviderId AND b.STATUS = N'Pending'
        UNION ALL
        -- provider: the customer approved the quotation (cash on completion, or paid by test e-wallet)
        SELECT CONCAT(N'B', b.BOOKING_ID), 1, N'booking-update',
               CASE WHEN pay.IS_CASH = 1
                    THEN CONCAT(N'Booking #', b.BOOKING_ID, N' was approved. The customer will pay ', @Peso,
                                FORMAT(b.TOTAL_FEE, '#,##0.##', 'en-US'), N' in cash when the job is done. It is now scheduled.')
                    ELSE CONCAT(N'Booking #', b.BOOKING_ID, N' was approved and paid by ', pay.METHOD_NAME, N' (test, ', @Peso,
                                FORMAT(b.TOTAL_FEE, '#,##0.##', 'en-US'), N', ref ', pay.TRANSACTION_REFERENCE, N'). It is now scheduled.')
               END,
               pay.CREATED_AT, b.BOOKING_ID
        FROM dbo.BOOKINGS b
        JOIN Pay pay ON pay.BOOKING_ID = b.BOOKING_ID
        WHERE @Role = N'Service Provider' AND b.PROVIDER_ID = @ProviderId
        UNION ALL
        -- provider: the customer cancelled (not the automatic cancellations of a suspension)
        SELECT CONCAT(N'B', b.BOOKING_ID), 1, N'booking-cancelled',
               CONCAT(N'Booking #', b.BOOKING_ID, N' was cancelled by the customer.'),
               b.UPDATED_AT, b.BOOKING_ID
        FROM dbo.BOOKINGS b
        WHERE @Role = N'Service Provider' AND b.PROVIDER_ID = @ProviderId AND b.STATUS = N'Cancelled'
          AND NOT EXISTS (SELECT 1 FROM SuspensionCancel sc WHERE sc.BOOKING_ID = b.BOOKING_ID)
        UNION ALL
        -- provider: reviews (not the ones an administrator removed)
        SELECT CONCAT(N'R', r.REVIEW_ID), 1, N'general',
               CONCAT(LTRIM(RTRIM(cu.FIRST_NAME)),
                      CASE WHEN LEN(LTRIM(RTRIM(cu.LAST_NAME))) > 0 THEN N' ' + UPPER(LEFT(LTRIM(cu.LAST_NAME), 1)) + N'.' ELSE N'' END,
                      N' left a ', r.RATING, N'-star review for booking #', b.BOOKING_ID, N'.'),
               r.CREATED_AT, b.BOOKING_ID
        FROM dbo.REVIEWS r
        JOIN dbo.BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
        JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
        JOIN dbo.USERS cu ON cu.USER_ID = c.USER_ID
        WHERE @Role = N'Service Provider' AND b.PROVIDER_ID = @ProviderId AND r.STATUS <> N'Removed'
        UNION ALL
        -- provider: the answer to a day-off request, or a day off an administrator gave (v2.7)
        SELECT CONCAT(N'D', a.ACTION_ID), 1, N'day-off',
               CASE a.ACTION_TYPE
                    WHEN N'Day Off Approved' THEN CONCAT(N'Your day off request for ', a.REASON, N' was approved.')
                    WHEN N'Day Off Rejected' THEN CONCAT(N'Your day off request for ', a.REASON, N' was not approved, so you stay available on those days.')
                    ELSE CONCAT(N'An administrator gave you a day off: ', a.REASON, N'.')
               END,
               a.CREATED_AT, NULL
        FROM dbo.ADMIN_ACTIONS a
        WHERE @Role = N'Service Provider' AND a.TARGET_PROVIDER_ID = @ProviderId
          AND a.ACTION_TYPE IN (N'Day Off Approved', N'Day Off Rejected', N'Extra Day Off Approved')
        UNION ALL
        -- provider: the administrator's decision on the application (only the latest one is shown)
        SELECT N'A', 1, N'application',
               CONCAT(N'Your provider application was ', a.ACTION_TYPE, N'.'),
               a.CREATED_AT, NULL
        FROM dbo.ADMIN_ACTIONS a
        WHERE @Role = N'Service Provider' AND a.TARGET_PROVIDER_ID = @ProviderId
          AND a.ACTION_TYPE IN (N'Approved', N'Rejected')
        UNION ALL
        -- ===== administrator: every application that is still Pending
        SELECT CONCAT(N'P', sp.PROVIDER_ID), 1, N'application-new',
               CONCAT(N'New provider application from ', sp.BUSINESS_NAME, N' (', pu.FIRST_NAME, N' ', pu.LAST_NAME, N').'),
               sp.DATE_APPLIED, NULL
        FROM dbo.SERVICE_PROVIDERS sp
        JOIN dbo.USERS pu ON pu.USER_ID = sp.USER_ID
        WHERE @Role = N'Administrator' AND sp.APPROVAL_STATUS = N'Pending'
        UNION ALL
        -- administrator: every day-off request that is still Pending (v2.7)
        SELECT CONCAT(N'Q', r.REQUEST_ID), 1, N'day-off-new',
               CONCAT(pu.FIRST_NAME, N' ', pu.LAST_NAME, N' (', sp.BUSINESS_NAME, N') asks for a day off: ',
                      FORMAT(d.FIRST_DAY, 'MMM d, yyyy', 'en-US'),
                      CASE WHEN d.DAYS > 1 THEN CONCAT(N' to ', FORMAT(d.LAST_DAY, 'MMM d, yyyy', 'en-US'), N' (', d.DAYS, N' days)') ELSE N'' END,
                      N'. Open Providers to approve or reject it.'),
               r.REQUESTED_AT, NULL
        FROM dbo.PROVIDER_DAY_OFF_REQUESTS r
        JOIN dbo.SERVICE_PROVIDERS sp ON sp.PROVIDER_ID = r.PROVIDER_ID
        JOIN dbo.USERS pu ON pu.USER_ID = sp.USER_ID
        CROSS APPLY (SELECT MIN(u.UNAVAILABLE_DATE) AS FIRST_DAY, MAX(u.UNAVAILABLE_DATE) AS LAST_DAY, COUNT(*) AS DAYS
                     FROM dbo.PROVIDER_UNAVAILABLE_DATES u WHERE u.REQUEST_ID = r.REQUEST_ID) d
        WHERE @Role = N'Administrator' AND r.STATUS = N'Pending'
    ),
    Latest AS (
        -- one row per group: the newest news of each booking (ties: the payment / cancellation wins)
        SELECT e.GRP, e.PRIORITY, e.KIND, e.MESSAGE, e.CREATED_AT, e.BOOKING_ID,
               ROW_NUMBER() OVER (PARTITION BY e.GRP ORDER BY e.CREATED_AT DESC, e.PRIORITY) AS RN
        FROM Events e
    )
    SELECT TOP (@Top)
           CAST(l.KIND AS NVARCHAR(30)) AS Kind,
           CAST(LEFT(l.MESSAGE, 400) AS NVARCHAR(400)) AS Message,
           l.CREATED_AT AS CreatedAt,
           l.BOOKING_ID AS BookingId,
           CAST(CASE WHEN @SeenAt IS NULL OR l.CREATED_AT > @SeenAt THEN 1 ELSE 0 END AS BIT) AS IsUnread
    FROM Latest l
    WHERE l.RN = 1
    ORDER BY l.CREATED_AT DESC, l.PRIORITY, l.GRP;
END
GO

-- usp_GetBookingsNeedingAttention
CREATE OR ALTER PROCEDURE dbo.usp_GetBookingsNeedingAttention
    @Top INT = 25
AS
BEGIN
    /* SERVISYO-DB-V2  (v2.3: replaces the BOOKING_REPORTS table, which no screen ever filled)
       The administrator's list of bookings that need a look, worked out from the data, newest first, at most 25:
         - its review was flagged, or gave 1 or 2 stars (an unhappy customer);
         - its date has passed (Cebu time) but it is still Pending or Accepted (nobody moved it on).
       TotalCount = how many bookings need attention in all (it can be more than the 25 rows shown).
       The column names match the app's C# class. */
    SET NOCOUNT ON;

    IF @Top IS NULL OR @Top < 1 OR @Top > 25 SET @Top = 25;
    DECLARE @Today DATE = CAST(dbo.fn_ManilaNow() AS DATE);

    SELECT TOP (@Top)
           b.BOOKING_ID AS BookingId,
           CAST(b.STATUS AS NVARCHAR(50)) AS Status,
           b.BOOKING_DATE AS BookingDate,
           CAST(CASE WHEN r.STATUS = N'Flagged' THEN CONCAT(N'Review flagged (', r.RATING, N' of 5 stars)')
                     WHEN r.RATING <= 2 THEN CONCAT(N'Low rating (', r.RATING, N' of 5 stars)')
                     ELSE CONCAT(N'Date passed, still ', b.STATUS) END AS NVARCHAR(100)) AS Reason,
           CAST(cu.FIRST_NAME + N' ' + cu.LAST_NAME AS NVARCHAR(255)) AS CustomerName,
           CAST(sp.BUSINESS_NAME AS NVARCHAR(255)) AS ProviderName,
           COUNT(*) OVER () AS TotalCount
    FROM dbo.BOOKINGS b
    JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
    JOIN dbo.USERS cu ON cu.USER_ID = c.USER_ID
    JOIN dbo.SERVICE_PROVIDERS sp ON sp.PROVIDER_ID = b.PROVIDER_ID
    LEFT JOIN dbo.REVIEWS r ON r.BOOKING_ID = b.BOOKING_ID AND r.STATUS <> N'Removed'
    WHERE (r.REVIEW_ID IS NOT NULL AND (r.STATUS = N'Flagged' OR r.RATING <= 2))
       OR (b.STATUS IN (N'Pending', N'Accepted') AND b.BOOKING_DATE < @Today)
    ORDER BY b.BOOKING_DATE DESC, b.BOOKING_ID DESC;
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
                 AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL p
                                 WHERE p.BOOKING_ID = i.BOOKING_ID AND p.PAYMENT_STATUS = N'PendingCollection'))
        THROW 50161, N'A booking can start only after the customer has paid or chosen cash on completion.', 1;
END
GO

-- trg_PaymentsDtl_Audit
CREATE OR ALTER TRIGGER dbo.trg_PaymentsDtl_Audit ON dbo.PAYMENTS_DTL
AFTER INSERT, UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2  (v2.3: was trg_Payments_Audit on the old PAYMENTS table)
       System Logs for each booking's payment (same format as the existing audit triggers): who chose cash on
       completion or paid by test e-wallet, and when cash was confirmed. The method name comes from PAYMENTS and
       the amount from BOOKINGS. WHO comes from SESSION_CONTEXT (set by the app). */
    SET NOCOUNT ON;
    DECLARE @uid INT = TRY_CAST(SESSION_CONTEXT(N'servisyo_user_id') AS INT);
    DECLARE @role NVARCHAR(50) = COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System');
    DECLARE @now DATETIME2 = SYSUTCDATETIME();

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'INSERT', N'Payment', i.BOOKING_ID,
           LEFT(CONCAT(N'Booking #', i.BOOKING_ID, N' payment chosen: ',
                       CASE m.METHOD_NAME WHEN N'Cash on Completion' THEN N'cash on completion'
                                          ELSE CONCAT(N'test e-wallet (', m.METHOD_NAME, N')') END,
                       N', ', i.PAYMENT_STATUS, N', PHP ', CONVERT(VARCHAR(30), CAST(b.TOTAL_FEE AS MONEY), 1),
                       N', ref ', i.TRANSACTION_REFERENCE, N'.'), 500), @now
    FROM inserted i
    JOIN dbo.PAYMENTS m ON m.PAYMENT_ID = i.PAYMENT_ID
    JOIN dbo.BOOKINGS b ON b.BOOKING_ID = i.BOOKING_ID
    WHERE NOT EXISTS (SELECT 1 FROM deleted);

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Payment', i.BOOKING_ID,
           LEFT(CONCAT(N'Booking #', i.BOOKING_ID, N' payment record: ', d.PAYMENT_STATUS, N' -> ', i.PAYMENT_STATUS,
                       N' (ref ', i.TRANSACTION_REFERENCE, N').'), 500), @now
    FROM inserted i
    JOIN deleted d ON d.PAYMENT_DTL_ID = i.PAYMENT_DTL_ID
    WHERE i.PAYMENT_STATUS <> d.PAYMENT_STATUS;
END
GO

-- trg_Users_StatusSync
CREATE OR ALTER TRIGGER dbo.trg_Users_StatusSync ON dbo.USERS
AFTER UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.1)
       USERS.ACCOUNT_STATUS is the real account status. CUSTOMERS and SERVICE_PROVIDERS keep a copy of it
       (ACCOUNT_STATUS) so a suspended customer / provider can be seen in those tables too.
       When an account status changes, this copies the new value to the customer / provider row. */
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.USER_ID = i.USER_ID WHERE i.ACCOUNT_STATUS <> d.ACCOUNT_STATUS)
        RETURN;

    UPDATE c SET c.ACCOUNT_STATUS = i.ACCOUNT_STATUS
    FROM dbo.CUSTOMERS c
    JOIN inserted i ON i.USER_ID = c.USER_ID
    WHERE c.ACCOUNT_STATUS <> i.ACCOUNT_STATUS;

    UPDATE p SET p.ACCOUNT_STATUS = i.ACCOUNT_STATUS
    FROM dbo.SERVICE_PROVIDERS p
    JOIN inserted i ON i.USER_ID = p.USER_ID
    WHERE p.ACCOUNT_STATUS <> i.ACCOUNT_STATUS;
END
GO

-- trg_Customers_StatusCopy
CREATE OR ALTER TRIGGER dbo.trg_Customers_StatusCopy ON dbo.CUSTOMERS
AFTER INSERT, UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.1)
       CUSTOMERS.ACCOUNT_STATUS is only a copy of USERS.ACCOUNT_STATUS: whoever inserts or changes a customer row,
       the copy is set back to the user's real status, so the two can never differ. */
    SET NOCOUNT ON;
    UPDATE c SET c.ACCOUNT_STATUS = u.ACCOUNT_STATUS
    FROM dbo.CUSTOMERS c
    JOIN inserted i ON i.CUSTOMER_ID = c.CUSTOMER_ID
    JOIN dbo.USERS u ON u.USER_ID = c.USER_ID
    WHERE c.ACCOUNT_STATUS <> u.ACCOUNT_STATUS;
END
GO

-- trg_Providers_StatusCopy
CREATE OR ALTER TRIGGER dbo.trg_Providers_StatusCopy ON dbo.SERVICE_PROVIDERS
AFTER INSERT, UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.1)
       SERVICE_PROVIDERS.ACCOUNT_STATUS is only a copy of USERS.ACCOUNT_STATUS (see trg_Customers_StatusCopy).
       It is not the same as APPROVAL_STATUS (Pending / Approved / Rejected), which this trigger never touches. */
    SET NOCOUNT ON;
    UPDATE p SET p.ACCOUNT_STATUS = u.ACCOUNT_STATUS
    FROM dbo.SERVICE_PROVIDERS p
    JOIN inserted i ON i.PROVIDER_ID = p.PROVIDER_ID
    JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
    WHERE p.ACCOUNT_STATUS <> u.ACCOUNT_STATUS;
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
         @PhoneNumber = N'0917-000-0000', @Address = N'Lahug',
         @NewUserId = @CustomerUserId OUTPUT, @NewCustomerId = @cid OUTPUT;

    SET @email = N'ben-' + @tag + N'@selftest.local';
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Ben', @LastName = N'Selftest', @Email = @email, @PasswordHash = @hash,
         @PhoneNumber = N'09170000001', @Address = N'Mabolo',
         @NewUserId = @Customer2UserId OUTPUT, @NewCustomerId = @cid OUTPUT;

    SET @email = N'mary-' + @tag + N'@selftest.local';
    EXEC dbo.usp_RegisterProvider @FirstName = N'Mary', @MiddleName = N'Cruz', @LastName = N'Selftest', @Email = @email,
         @PasswordHash = @hash, @PhoneNumber = N'09181234567', @BusinessName = @biz, @Description = N'Pipes and leaks',
         @Address = N'Banilad', @BaseFee = 300, @MinNoticeDays = 1, @MaxJobsPerDay = 3,
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
DECLARE @Note TABLE (Kind NVARCHAR(30), Message NVARCHAR(400), CreatedAt DATETIME2, BookingId INT, IsUnread BIT);
DECLARE @Attn TABLE (BookingId INT, Status NVARCHAR(50), BookingDate DATE, Reason NVARCHAR(100), CustomerName NVARCHAR(255),
                     ProviderName NVARCHAR(255), TotalCount INT);
DECLARE @seen DATETIME2, @biz NVARCHAR(155), @P2 INT, @PU2 INT, @k INT;
DECLARE @Found TABLE (SERVICE_ID INT, SERVICE_NAME NVARCHAR(100), DESCRIPTION NVARCHAR(255), PRICE DECIMAL(10,2), DURATION_MINUTES INT,
                      CATEGORY_ID INT, CATEGORY_NAME NVARCHAR(100), PROVIDER_ID INT, BUSINESS_NAME NVARCHAR(155),
                      AVG_RATING DECIMAL(2,1), REVIEW_COUNT INT);

------------------------------------------------------------------ 1
SET @t = N'01 Register customer + provider (procedures), approve, add service';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU;
    IF EXISTS (SELECT 1 FROM dbo.USERS u JOIN dbo.CUSTOMERS c ON c.USER_ID = u.USER_ID
               WHERE u.USER_ID = @C AND u.ROLE = N'Customer' AND u.ACCOUNT_STATUS = N'Active' AND u.PHONE_NUMBER = N'09170000000')
       AND EXISTS (SELECT 1 FROM dbo.SERVICE_PROVIDERS WHERE PROVIDER_ID = @P AND USER_ID = @PU
                   AND APPROVAL_STATUS = N'Approved' AND DATE_APPROVED IS NOT NULL AND REVIEW_COUNT = 0)
       AND EXISTS (SELECT 1 FROM @Note WHERE Kind = N'application' AND Message = N'Your provider application was Approved.')
       AND EXISTS (SELECT 1 FROM dbo.ADMIN_ACTIONS WHERE ADMIN_USER_ID = @A AND ACTION_TYPE = N'Approved' AND TARGET_TYPE = N'SERVICE_PROVIDER' AND TARGET_PROVIDER_ID = @P)
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
SET @t = N'04 usp_CreateBooking: Pending / Unpaid, fee and services copied, on the provider''s bell';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug, Cebu City', @ServiceDescription = N'Kitchen sink is leaking', @NewBookingId = @B OUTPUT;
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU;
    IF EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND STATUS = N'Pending' AND PAYMENT_STATUS = N'Unpaid'
               AND TOTAL_FEE = 650 AND TOTAL_DURATION_MINUTES = 60 AND END_TIME = '10:00')
       AND (SELECT COUNT(*) FROM dbo.BOOKING_SERVICES WHERE BOOKING_ID = @B AND SERVICE_ID = @S AND PRICE_SNAPSHOT = 650) = 1
       AND EXISTS (SELECT 1 FROM @Note WHERE Kind = N'booking-new' AND BookingId = @B AND IsUnread = 1
                   AND Message = CONCAT(N'You have a new booking request (Booking #', @B, N').'))
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

    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU;
    IF NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL d JOIN dbo.PAYMENTS m ON m.PAYMENT_ID = d.PAYMENT_ID
                   WHERE d.BOOKING_ID = @B AND d.PAYMENT_ID = 1 AND m.METHOD_NAME = N'Cash on Completion'
                     AND d.PAYMENT_STATUS = N'PendingCollection' AND d.PAID_AT IS NULL AND d.TRANSACTION_REFERENCE = @ref)
       OR NOT EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND STATUS = N'Accepted' AND PAYMENT_STATUS = N'Unpaid')
       OR NOT EXISTS (SELECT 1 FROM @Note WHERE BookingId = @B AND Kind = N'booking-update'
                      AND Message = CONCAT(N'Booking #', @B, N' was approved. The customer will pay ', NCHAR(8369), N'650 in cash when the job is done. It is now scheduled.'))
        THROW 59001, N'After choosing cash: the payment row or the provider''s bell is not as expected.', 1;

    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'In Progress';
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Completed';
    EXEC dbo.usp_ConfirmCashReceived @ProviderUserId = @PU, @BookingId = @B;

    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @C;
    IF EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL d WHERE d.BOOKING_ID = @B AND d.PAYMENT_STATUS = N'Paid' AND d.PAID_AT IS NOT NULL)
       AND EXISTS (SELECT 1 FROM dbo.vw_BookingOverview WHERE BOOKING_ID = @B AND IS_COLLECTED_BY_PROVIDER = 1)
       AND EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND STATUS = N'Completed' AND PAYMENT_STATUS = N'Paid')
       AND EXISTS (SELECT 1 FROM @Note WHERE BookingId = @B AND Kind = N'booking-update'
                   AND Message LIKE CONCAT(N'% confirmed receiving your ', NCHAR(8369), N'650 cash payment for booking #', @B, N' (ref ', @ref, N').'))
       AND (SELECT COUNT(*) FROM @Note WHERE BookingId = @B) = 1
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
       AND EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL d JOIN dbo.PAYMENTS m ON m.PAYMENT_ID = d.PAYMENT_ID
                   WHERE d.BOOKING_ID = @B AND m.METHOD_NAME = N'GCash' AND d.PAYMENT_STATUS = N'Paid' AND d.PAID_AT IS NOT NULL)
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
       AND (SELECT COUNT(*) FROM dbo.ADMIN_ACTIONS WHERE ADMIN_USER_ID = @A AND TARGET_TYPE = N'REVIEW' AND TARGET_REVIEW_ID = @RevId) = 2
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
    IF NOT EXISTS (SELECT 1 FROM dbo.ADMIN_ACTIONS WHERE ADMIN_USER_ID = @A AND ACTION_TYPE = N'Account Suspended' AND TARGET_TYPE = N'CUSTOMER'
                   AND TARGET_CUSTOMER_ID = (SELECT CUSTOMER_ID FROM dbo.CUSTOMERS WHERE USER_ID = @C))
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

------------------------------------------------------------------ 22
SET @t = N'22 Phone +63 917 000 0003 is saved as 09170000003';
BEGIN TRY
    BEGIN TRANSACTION;
    SET @email = N'plus-' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 12) + N'@selftest.local';
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Phone', @LastName = N'Test', @Email = @email,
         @PasswordHash = N'SELFTEST-NOT-A-REAL-PASSWORD-HASH-000000', @PhoneNumber = N'+63 917 000 0003', @Address = N'Talamban', @NewUserId = @n OUTPUT;
    IF EXISTS (SELECT 1 FROM dbo.USERS WHERE USER_ID = @n AND PHONE_NUMBER = N'09170000003')
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'The phone number was not changed to 09XXXXXXXXX.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 23
SET @t = N'23 A phone number that is not 11 digits is refused (50105)';
BEGIN TRY
    BEGIN TRANSACTION;
    SET @email = N'short-' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 12) + N'@selftest.local';
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Phone', @LastName = N'Test', @Email = @email,
         @PasswordHash = N'SELFTEST-NOT-A-REAL-PASSWORD-HASH-000000', @PhoneNumber = N'12345', @Address = N'Talamban', @NewUserId = @n OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50105, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 24
SET @t = N'24 An email with a number in the domain is refused (50103)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Mail', @LastName = N'Test', @Email = N'juan@gmail2.com',
         @PasswordHash = N'SELFTEST-NOT-A-REAL-PASSWORD-HASH-000000', @PhoneNumber = N'09170000004', @Address = N'Talamban', @NewUserId = @n OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50103, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 25
SET @t = N'25 A one-letter domain like a.c is refused (50103)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Mail', @LastName = N'Test', @Email = N'juan@a.c',
         @PasswordHash = N'SELFTEST-NOT-A-REAL-PASSWORD-HASH-000000', @PhoneNumber = N'09170000005', @Address = N'Talamban', @NewUserId = @n OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50103, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 26
SET @t = N'26 An address of numbers only is refused (50106)';
BEGIN TRY
    BEGIN TRANSACTION;
    SET @email = N'addr-' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 12) + N'@selftest.local';
    EXEC dbo.usp_RegisterCustomer @FirstName = N'Phone', @LastName = N'Test', @Email = @email,
         @PasswordHash = N'SELFTEST-NOT-A-REAL-PASSWORD-HASH-000000', @PhoneNumber = N'09170000006', @Address = N'12345', @NewUserId = @n OUTPUT;
    ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'No error was raised.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, IIF(ERROR_NUMBER() = 50106, N'PASS', N'FAIL'), CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 27
SET @t = N'27 The status copies follow USERS (trg_Users_StatusSync, trg_Customers_StatusCopy)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    EXEC dbo.usp_UpdateAccountStatus @AdminUserId = @A, @TargetUserId = @C, @NewStatus = N'Suspended', @Reason = N'Self-test';
    SET @n = IIF(EXISTS (SELECT 1 FROM dbo.CUSTOMERS WHERE USER_ID = @C AND ACCOUNT_STATUS = N'Suspended'), 1, 0);
    UPDATE dbo.CUSTOMERS SET ACCOUNT_STATUS = N'Deactivated' WHERE USER_ID = @C;   -- a wrong direct change is put back
    IF @n = 1 AND EXISTS (SELECT 1 FROM dbo.CUSTOMERS WHERE USER_ID = @C AND ACCOUNT_STATUS = N'Suspended')
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'CUSTOMERS.ACCOUNT_STATUS does not match USERS.ACCOUNT_STATUS.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 28
SET @t = N'28 Suspending a provider cancels Pending bookings, keeps Accepted ones, the customer''s bell says why';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Stays pending', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C2, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '14:00',
         @ServiceLocation = N'Mabolo', @ServiceDescription = N'Gets accepted', @NewBookingId = @B2 OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B2, @NewStatus = N'Accepted';
    EXEC dbo.usp_UpdateAccountStatus @AdminUserId = @A, @TargetUserId = @PU, @NewStatus = N'Suspended', @Reason = N'Self-test';
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @C;
    IF EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B AND STATUS = N'Cancelled')
       AND EXISTS (SELECT 1 FROM dbo.BOOKINGS WHERE BOOKING_ID = @B2 AND STATUS = N'Accepted')
       AND EXISTS (SELECT 1 FROM @Note WHERE Kind = N'booking-cancelled' AND BookingId = @B
                   AND Message LIKE CONCAT(N'Your booking #', @B, N' on % was cancelled because the service provider is no longer available.%'))
       AND EXISTS (SELECT 1 FROM dbo.SERVICE_PROVIDERS WHERE PROVIDER_ID = @P AND ACCOUNT_STATUS = N'Suspended' AND APPROVAL_STATUS = N'Approved')
       AND EXISTS (SELECT 1 FROM dbo.ADMIN_ACTIONS WHERE ADMIN_USER_ID = @A AND ACTION_TYPE = N'Account Suspended'
                   AND TARGET_TYPE = N'SERVICE_PROVIDER' AND TARGET_PROVIDER_ID = @P)
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'The bookings, the notification, the provider copy or the ADMIN_ACTIONS row are not as expected.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 29
SET @t = N'29 ADMIN_ACTIONS points to exactly one real target of its TARGET_TYPE (CHECK + FOREIGN KEYS, v2.2)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @n = 0;     -- how many wrong rows were refused (547 = CHECK or FOREIGN KEY)
    BEGIN TRY   -- the old type "USER" is gone
        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_PROVIDER_ID) VALUES (@A, N'Self-test', N'USER', @P);
    END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @n += 1; END CATCH;
    BEGIN TRY   -- no target at all
        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE) VALUES (@A, N'Self-test', N'SERVICE_PROVIDER');
    END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @n += 1; END CATCH;
    BEGIN TRY   -- the type does not match the column that is filled
        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_PROVIDER_ID) VALUES (@A, N'Self-test', N'CUSTOMER', @P);
    END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @n += 1; END CATCH;
    BEGIN TRY   -- two targets in one row
        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_PROVIDER_ID, TARGET_CUSTOMER_ID)
        VALUES (@A, N'Self-test', N'SERVICE_PROVIDER', @P, (SELECT CUSTOMER_ID FROM dbo.CUSTOMERS WHERE USER_ID = @C));
    END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @n += 1; END CATCH;
    BEGIN TRY   -- a provider that does not exist (FOREIGN KEY)
        INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_PROVIDER_ID) VALUES (@A, N'Self-test', N'SERVICE_PROVIDER', -1);
    END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @n += 1; END CATCH;
    -- a correct row is accepted
    INSERT INTO dbo.ADMIN_ACTIONS (ADMIN_USER_ID, ACTION_TYPE, TARGET_TYPE, TARGET_PROVIDER_ID) VALUES (@A, N'Self-test OK', N'SERVICE_PROVIDER', @P);
    IF @n = 5 AND EXISTS (SELECT 1 FROM dbo.ADMIN_ACTIONS WHERE ADMIN_USER_ID = @A AND ACTION_TYPE = N'Self-test OK' AND TARGET_PROVIDER_ID = @P)
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(N'wrong rows refused: ', @n, N' of 5'));
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 30
SET @t = N'30 usp_GetNotifications: each role sees its own news, each booking only its latest (v2.3)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    -- a second provider who is still waiting: on the administrator's bell until it is decided
    SET @email = N'nora-' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 12) + N'@selftest.local';
    SET @biz = N'Selftest Waiting ' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 8);
    EXEC dbo.usp_RegisterProvider @FirstName = N'Nora', @MiddleName = N'Reyes', @LastName = N'Selftest', @Email = @email,
         @PasswordHash = N'SELFTEST-NOT-A-REAL-PASSWORD-HASH-000000', @PhoneNumber = N'09181234568', @BusinessName = @biz,
         @Description = N'Waiting for a decision', @Address = N'Talamban', @BaseFee = 300, @MinNoticeDays = 1, @MaxJobsPerDay = 3,
         @NewUserId = @PU2 OUTPUT, @NewProviderId = @P2 OUTPUT;
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @A;
    IF NOT EXISTS (SELECT 1 FROM @Note WHERE Kind = N'application-new'
                   AND Message = CONCAT(N'New provider application from ', @biz, N' (Nora Selftest).'))
        THROW 59030, N'The administrator does not see the Pending application.', 1;
    EXEC dbo.usp_ApproveProvider @AdminUserId = @A, @ProviderId = @P2, @Decision = N'Rejected', @Reason = N'Self-test';
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @A;
    IF EXISTS (SELECT 1 FROM @Note WHERE Message LIKE CONCAT(N'%', @biz, N'%'))
        THROW 59030, N'A decided application is still on the administrator''s bell.', 1;
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU2;
    IF NOT EXISTS (SELECT 1 FROM @Note WHERE Kind = N'application' AND Message = N'Your provider application was Rejected.')
        THROW 59030, N'The rejected provider does not see the decision.', 1;

    -- booking 1 is accepted: the customer sees "now Accepted", the provider's "new request" row is gone
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Accepted job', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    -- booking 2 is cancelled by its customer: only the provider is told
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C2, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '14:00',
         @ServiceLocation = N'Mabolo', @ServiceDescription = N'Cancelled job', @NewBookingId = @B2 OUTPUT;
    EXEC dbo.usp_CancelBooking @CustomerUserId = @C2, @BookingId = @B2;

    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @C;
    IF NOT EXISTS (SELECT 1 FROM @Note WHERE BookingId = @B AND Kind = N'booking-update' AND Message = CONCAT(N'Your booking #', @B, N' is now Accepted.'))
        THROW 59030, N'The customer does not see the accepted booking.', 1;
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @C2;
    IF EXISTS (SELECT 1 FROM @Note WHERE BookingId = @B2)
        THROW 59030, N'A customer is told about the booking they cancelled themselves.', 1;
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU;
    IF EXISTS (SELECT 1 FROM @Note WHERE BookingId = @B)
       OR NOT EXISTS (SELECT 1 FROM @Note WHERE BookingId = @B2 AND Kind = N'booking-cancelled'
                      AND Message = CONCAT(N'Booking #', @B2, N' was cancelled by the customer.'))
       OR EXISTS (SELECT BookingId FROM @Note WHERE BookingId IS NOT NULL GROUP BY BookingId HAVING COUNT(*) > 1)
        THROW 59030, N'The provider''s bell is not as expected.', 1;
    INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 31
SET @t = N'31 usp_GetNotifications: at most 25 rows, unread = newer than NOTIFICATIONS_SEEN_AT, none when suspended';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    -- 30 waiting requests, made directly (one per day, so the active-slot index is happy)
    SELECT @n = CUSTOMER_ID FROM dbo.CUSTOMERS WHERE USER_ID = @C;
    SET @k = 1;
    WHILE @k <= 30
    BEGIN
        INSERT INTO dbo.BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES, SERVICE_LOCATION,
                                  SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT, UPDATED_AT)
        VALUES (@n, @P, DATEADD(DAY, 30 + @k, @D), '09:00', 60, N'Lahug', N'Many requests', 650, N'Pending', N'Unpaid',
                DATEADD(MINUTE, -@k, SYSUTCDATETIME()), DATEADD(MINUTE, -@k, SYSUTCDATETIME()));
        SET @k += 1;
    END
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU, @Top = 500;
    IF (SELECT COUNT(*) FROM @Note) <> 25 OR EXISTS (SELECT 1 FROM @Note WHERE IsUnread = 0)
        THROW 59031, N'Not exactly 25 rows, or some are not unread before the bell was opened.', 1;
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU, @Top = 5;
    IF (SELECT COUNT(*) FROM @Note) <> 5
        THROW 59031, N'@Top = 5 did not return 5 rows.', 1;

    -- the bell is opened: everything up to now is seen; one newer request is unread
    SET @seen = SYSUTCDATETIME();
    UPDATE dbo.USERS SET NOTIFICATIONS_SEEN_AT = @seen WHERE USER_ID = @PU;
    INSERT INTO dbo.BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES, SERVICE_LOCATION,
                              SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT, UPDATED_AT)
    VALUES (@n, @P, DATEADD(DAY, 70, @D), '09:00', 60, N'Lahug', N'After the bell', 650, N'Pending', N'Unpaid',
            DATEADD(SECOND, 1, @seen), DATEADD(SECOND, 1, @seen));
    SET @B = CAST(SCOPE_IDENTITY() AS INT);
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU;
    IF (SELECT COUNT(*) FROM @Note WHERE IsUnread = 1) <> 1 OR NOT EXISTS (SELECT 1 FROM @Note WHERE IsUnread = 1 AND BookingId = @B)
        THROW 59031, N'Only the request made after the bell was opened should be unread.', 1;

    -- a suspended account gets nothing
    UPDATE dbo.USERS SET ACCOUNT_STATUS = N'Suspended' WHERE USER_ID = @PU;
    DELETE @Note; INSERT @Note EXEC dbo.usp_GetNotifications @UserId = @PU;
    IF EXISTS (SELECT 1 FROM @Note)
        THROW 59031, N'A suspended account still gets notifications.', 1;
    INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 32
SET @t = N'32 PAYMENTS is the list of methods; PAYMENTS_DTL: one payment per booking, only cash can wait (v2.3)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    IF (SELECT COUNT(*) FROM dbo.PAYMENTS
        WHERE (PAYMENT_ID = 1 AND METHOD_NAME = N'Cash on Completion') OR (PAYMENT_ID = 2 AND METHOD_NAME = N'GCash')
           OR (PAYMENT_ID = 3 AND METHOD_NAME = N'Maya')) <> 3
        THROW 59032, N'PAYMENTS does not have the rows 1 Cash on Completion, 2 GCash, 3 Maya.', 1;
    SET @ids = CAST(@S AS NVARCHAR(20));
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Maya job', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'MockEWallet', @Wallet = N'MAYA',
         @MobileNumber = N'09170000000', @TransactionReference = @ref OUTPUT;
    IF @ref NOT LIKE N'MAYA-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
       OR NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL WHERE BOOKING_ID = @B AND PAYMENT_ID = 3 AND PAYMENT_STATUS = N'Paid')
        THROW 59032, N'The Maya payment is not as expected.', 1;
    SET @n = 0;     -- wrong rows refused
    BEGIN TRY   -- a second payment for the same booking (unique BOOKING_ID)
        INSERT INTO dbo.PAYMENTS_DTL (BOOKING_ID, PAYMENT_ID, PAYMENT_STATUS, TRANSACTION_REFERENCE, CREATED_AT, PAID_AT)
        VALUES (@B, 2, N'Paid', N'GCASH-00000001-SELFTEST', SYSUTCDATETIME(), SYSUTCDATETIME());
    END TRY BEGIN CATCH IF ERROR_NUMBER() IN (2601, 2627) SET @n += 1; END CATCH;
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C2, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '14:00',
         @ServiceLocation = N'Mabolo', @ServiceDescription = N'Wrong payments', @NewBookingId = @B2 OUTPUT;
    BEGIN TRY   -- GCash cannot wait to be collected (CHECK)
        INSERT INTO dbo.PAYMENTS_DTL (BOOKING_ID, PAYMENT_ID, PAYMENT_STATUS, TRANSACTION_REFERENCE, CREATED_AT, PAID_AT)
        VALUES (@B2, 2, N'PendingCollection', N'GCASH-00000002-SELFTEST', SYSUTCDATETIME(), NULL);
    END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @n += 1; END CATCH;
    BEGIN TRY   -- a method that is not in the PAYMENTS list (FOREIGN KEY)
        INSERT INTO dbo.PAYMENTS_DTL (BOOKING_ID, PAYMENT_ID, PAYMENT_STATUS, TRANSACTION_REFERENCE, CREATED_AT, PAID_AT)
        VALUES (@B2, 99, N'Paid', N'X-00000003-SELFTEST', SYSUTCDATETIME(), SYSUTCDATETIME());
    END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @n += 1; END CATCH;
    BEGIN TRY   -- a method still in use cannot be deleted (FOREIGN KEY)
        DELETE FROM dbo.PAYMENTS WHERE PAYMENT_ID = 3;
    END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @n += 1; END CATCH;
    IF @n = 4
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'PASS', @ref);
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(N'wrong rows refused: ', @n, N' of 4'));
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
END CATCH;

------------------------------------------------------------------ 33
SET @t = N'33 usp_GetBookingsNeedingAttention: low ratings and passed dates still Pending (v2.3)';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC #svSetup @A OUTPUT, @C OUTPUT, @C2 OUTPUT, @PU OUTPUT, @P OUTPUT, @S OUTPUT, @D OUTPUT;
    SET @ids = CAST(@S AS NVARCHAR(20));
    -- a finished job with a 2-star review
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '09:00',
         @ServiceLocation = N'Lahug', @ServiceDescription = N'Unhappy customer', @NewBookingId = @B OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Accepted';
    EXEC dbo.usp_ApproveAndPay @CustomerUserId = @C, @BookingId = @B, @PaymentMethod = N'CashOnCompletion', @TransactionReference = @ref OUTPUT;
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'In Progress';
    EXEC dbo.usp_UpdateBookingStatus @ProviderUserId = @PU, @BookingId = @B, @NewStatus = N'Completed';
    EXEC dbo.usp_AddReview @CustomerUserId = @C, @BookingId = @B, @Rating = 2, @Comment = N'Late and messy', @NewReviewId = @RevId OUTPUT;
    -- a request whose date has passed while it is still Pending
    EXEC dbo.usp_CreateBooking @CustomerUserId = @C2, @ProviderId = @P, @ServiceIds = @ids, @BookingDate = @D, @BookingTime = '14:00',
         @ServiceLocation = N'Mabolo', @ServiceDescription = N'Forgotten request', @NewBookingId = @B2 OUTPUT;
    UPDATE dbo.BOOKINGS SET BOOKING_DATE = DATEADD(DAY, -3, CAST(dbo.fn_ManilaNow() AS DATE)) WHERE BOOKING_ID = @B2;

    DELETE @Attn; INSERT @Attn EXEC dbo.usp_GetBookingsNeedingAttention;
    IF EXISTS (SELECT 1 FROM @Attn WHERE BookingId = @B AND Reason = N'Low rating (2 of 5 stars)')
       AND EXISTS (SELECT 1 FROM @Attn WHERE BookingId = @B2 AND Reason = N'Date passed, still Pending')
       AND (SELECT COUNT(*) FROM @Attn) <= 25
       AND NOT EXISTS (SELECT 1 FROM @Attn WHERE TotalCount < 2)
        INSERT @R (TEST, RESULT) VALUES (@t, N'PASS');
    ELSE
        INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', N'The two bookings are not both on the list.');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT @R (TEST, RESULT, DETAIL) VALUES (@t, N'FAIL', CONCAT(ERROR_NUMBER(), N': ', ERROR_MESSAGE()));
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
-- One user's bell (newest 25):            EXEC dbo.usp_GetNotifications @UserId = 2;
-- Bookings that need an administrator:    EXEC dbo.usp_GetBookingsNeedingAttention;
-- The payment methods:                    SELECT * FROM dbo.PAYMENTS;
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
WHERE v.PAYMENT_METHOD = N'Cash on Completion' AND v.PAYMENT_RECORD_STATUS = N'PendingCollection' AND v.STATUS = N'Completed'
GROUP BY v.BUSINESS_NAME;
   ===================================================================================================== */

SET NOEXEC OFF;
GO
