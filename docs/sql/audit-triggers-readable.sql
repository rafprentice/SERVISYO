CREATE OR ALTER TRIGGER dbo.trg_Bookings_Audit ON dbo.BOOKINGS AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @uid INT = TRY_CAST(SESSION_CONTEXT(N'servisyo_user_id') AS INT);
    DECLARE @role NVARCHAR(50) = COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System');
    DECLARE @now DATETIME2 = SYSUTCDATETIME();

    -- a new booking request
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'INSERT', N'Booking', i.BOOKING_ID,
           LEFT(CONCAT(N'Booking #', i.BOOKING_ID, N' requested (', i.STATUS, N', ', i.PAYMENT_STATUS, N', PHP ', CONVERT(VARCHAR(30), CAST(i.TOTAL_FEE AS MONEY), 1), N').'), 500), @now
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted);

    -- status transitions (Pending -> Accepted -> In Progress -> Completed, or Rejected / Cancelled)
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Booking', i.BOOKING_ID,
           LEFT(CONCAT(N'Booking #', i.BOOKING_ID, N' status changed: ', d.STATUS, N' -> ', i.STATUS, N'.'), 500), @now
    FROM inserted i JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
    WHERE i.STATUS <> d.STATUS;

    -- payments live in BOOKINGS.PAYMENT_STATUS (there is no separate payments table)
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Payment', i.BOOKING_ID,
           LEFT(CONCAT(N'Booking #', i.BOOKING_ID, N' payment: ', d.PAYMENT_STATUS, N' -> ', i.PAYMENT_STATUS, N' (PHP ', CONVERT(VARCHAR(30), CAST(i.TOTAL_FEE AS MONEY), 1), N').'), 500), @now
    FROM inserted i JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
    WHERE i.PAYMENT_STATUS <> d.PAYMENT_STATUS;
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Users_Audit ON dbo.USERS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @uid INT = TRY_CAST(SESSION_CONTEXT(N'servisyo_user_id') AS INT);
    DECLARE @role NVARCHAR(50) = COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System');
    DECLARE @now DATETIME2 = SYSUTCDATETIME();

    -- new accounts
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'INSERT', N'User', i.USER_ID,
           LEFT(CONCAT(N'New ', i.ROLE, N' account created: ', i.EMAIL, N'.'), 500), @now
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted);

    -- role changes
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'User', i.USER_ID,
           LEFT(CONCAT(N'Role changed for ', i.EMAIL, N': ', d.ROLE, N' -> ', i.ROLE, N'.'), 500), @now
    FROM inserted i JOIN deleted d ON d.USER_ID = i.USER_ID
    WHERE i.ROLE <> d.ROLE;

    -- suspended / deactivated / re-activated
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'User', i.USER_ID,
           LEFT(CONCAT(N'Account status for ', i.EMAIL, N': ', d.ACCOUNT_STATUS, N' -> ', i.ACCOUNT_STATUS, N'.'), 500), @now
    FROM inserted i JOIN deleted d ON d.USER_ID = i.USER_ID
    WHERE i.ACCOUNT_STATUS <> d.ACCOUNT_STATUS;

    -- profile edits (which parts changed; never the values of passwords)
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'User', i.USER_ID,
           LEFT(CONCAT(N'Profile updated for ', i.EMAIL, N' (',
                CONCAT_WS(N', ',
                    IIF(ISNULL(i.FIRST_NAME, N'') <> ISNULL(d.FIRST_NAME, N'') OR ISNULL(i.MIDDLE_NAME, N'') <> ISNULL(d.MIDDLE_NAME, N'')
                        OR ISNULL(i.LAST_NAME, N'') <> ISNULL(d.LAST_NAME, N''), N'name', NULL),
                    IIF(ISNULL(i.EMAIL, N'') <> ISNULL(d.EMAIL, N''), CONCAT(N'email, was ', d.EMAIL), NULL),
                    IIF(ISNULL(i.PHONE_NUMBER, N'') <> ISNULL(d.PHONE_NUMBER, N''), N'phone', NULL)),
                N').'), 500), @now
    FROM inserted i JOIN deleted d ON d.USER_ID = i.USER_ID
    WHERE ISNULL(i.FIRST_NAME, N'') <> ISNULL(d.FIRST_NAME, N'') OR ISNULL(i.MIDDLE_NAME, N'') <> ISNULL(d.MIDDLE_NAME, N'')
       OR ISNULL(i.LAST_NAME, N'') <> ISNULL(d.LAST_NAME, N'') OR ISNULL(i.EMAIL, N'') <> ISNULL(d.EMAIL, N'')
       OR ISNULL(i.PHONE_NUMBER, N'') <> ISNULL(d.PHONE_NUMBER, N'');

    -- password changed (reset or profile page)
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'User', i.USER_ID,
           LEFT(CONCAT(N'Password changed for ', i.EMAIL, N'.'), 500), @now
    FROM inserted i JOIN deleted d ON d.USER_ID = i.USER_ID
    WHERE i.PASSWORD_HASH <> d.PASSWORD_HASH;

    -- removed accounts
    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'DELETE', N'User', d.USER_ID,
           LEFT(CONCAT(N'Account deleted: ', d.EMAIL, N' (', d.ROLE, N').'), 500), @now
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Services_Audit ON dbo.SERVICES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @uid INT = TRY_CAST(SESSION_CONTEXT(N'servisyo_user_id') AS INT);
    DECLARE @role NVARCHAR(50) = COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System');
    DECLARE @now DATETIME2 = SYSUTCDATETIME();

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'INSERT', N'Service', i.SERVICE_ID,
           LEFT(CONCAT(N'Service added: ', i.SERVICE_NAME, N' (PHP ', CONVERT(VARCHAR(30), CAST(i.PRICE AS MONEY), 1), N').'), 500), @now
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted);

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Service', i.SERVICE_ID,
           LEFT(CONCAT(N'Price of ', i.SERVICE_NAME, N' changed: PHP ', CONVERT(VARCHAR(30), CAST(d.PRICE AS MONEY), 1), N' -> PHP ', CONVERT(VARCHAR(30), CAST(i.PRICE AS MONEY), 1), N'.'), 500), @now
    FROM inserted i JOIN deleted d ON d.SERVICE_ID = i.SERVICE_ID
    WHERE i.PRICE <> d.PRICE;

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Service', i.SERVICE_ID,
           LEFT(CONCAT(IIF(i.IS_ACTIVE = 1, N'Service activated: ', N'Service deactivated: '), i.SERVICE_NAME, N'.'), 500), @now
    FROM inserted i JOIN deleted d ON d.SERVICE_ID = i.SERVICE_ID
    WHERE i.IS_ACTIVE <> d.IS_ACTIVE;

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Service', i.SERVICE_ID,
           LEFT(CONCAT(N'Service details updated: ', i.SERVICE_NAME,
                IIF(i.SERVICE_NAME <> d.SERVICE_NAME, CONCAT(N' (was ', d.SERVICE_NAME, N')'), N''), N'.'), 500), @now
    FROM inserted i JOIN deleted d ON d.SERVICE_ID = i.SERVICE_ID
    WHERE i.SERVICE_NAME <> d.SERVICE_NAME OR ISNULL(i.DESCRIPTION, N'') <> ISNULL(d.DESCRIPTION, N'');

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'DELETE', N'Service', d.SERVICE_ID,
           LEFT(CONCAT(N'Service deleted: ', d.SERVICE_NAME, N'.'), 500), @now
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO
