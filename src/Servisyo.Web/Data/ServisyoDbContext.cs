using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Models;

namespace Servisyo.Web.Data;

public class ServisyoDbContext : DbContext
{
    public ServisyoDbContext(DbContextOptions<ServisyoDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<ProviderAvailability> ProviderAvailabilities => Set<ProviderAvailability>();
    public DbSet<ProviderBreak> ProviderBreaks => Set<ProviderBreak>();
    public DbSet<ProviderUnavailableDate> ProviderUnavailableDates => Set<ProviderUnavailableDate>();
    public DbSet<ProviderDayOffRequest> DayOffRequests => Set<ProviderDayOffRequest>();   // v2.7
    public DbSet<ProviderNameChangeRequest> NameChangeRequests => Set<ProviderNameChangeRequest>();   // v2.8
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ProviderCategory> ProviderCategories => Set<ProviderCategory>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingService> BookingServices => Set<BookingService>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Payment> Payments => Set<Payment>();                    // v2.3: the list of payment methods
    public DbSet<PaymentDetail> PaymentDetails => Set<PaymentDetail>();  // v2.3: each booking's payment (PAYMENTS_DTL)
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
    public DbSet<AdminAction> AdminActions => Set<AdminAction>();

    /// CK_PROVIDERS_LIMITS (database v2.6): MAX_JOBS_PER_DAY from 1 to 20 (ProviderLimits.MaxJobsPerDay).
    public const string ProviderLimitsCheck = "[MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] BETWEEN 1 AND 20";

    /// Database v2.7: the day-off requests.
    public const string DayOffStatusCheck = "[STATUS] IN ('Pending','Approved','Rejected')";
    /// A Pending request has no decision yet; an Approved or Rejected one points to the ADMIN_ACTIONS row that decided it.
    public const string DayOffDecisionCheck = "([STATUS] = 'Pending' AND [DECIDED_BY_ACTION_ID] IS NULL) OR ([STATUS] <> 'Pending' AND [DECIDED_BY_ACTION_ID] IS NOT NULL)";
    /// A day off comes from a request OR from an administrator's extra day, never both.
    public const string DayOffSourceCheck = "[REQUEST_ID] IS NULL OR [APPROVED_BY_ACTION_ID] IS NULL";

    /// Database v2.8: the name change requests (the same two rules as the day-off requests).
    public const string NameChangeStatusCheck = DayOffStatusCheck;
    public const string NameChangeDecisionCheck = DayOffDecisionCheck;
    /// Only ONE request per provider can wait for an answer (a filtered UNIQUE index).
    public const string NameChangeOnePendingIndex = "UX_PROVIDER_NAME_CHANGE_REQUESTS_ONE_PENDING";
    public const string NameChangeOnePendingFilter = "[STATUS] = 'Pending'";

    /// CK_BOOKINGS_COMPLETION (database v2.11): the proof of a finished job is all or nothing, and only on a Completed
    /// booking (with a message that is not blank). A job completed before v2.11 has no proof (all three NULL).
    public const string BookingCompletionCheck =
        "([COMPLETION_MESSAGE] IS NULL AND [COMPLETION_PHOTO_URL] IS NULL AND [COMPLETED_AT] IS NULL) OR ([STATUS] = 'Completed' AND [COMPLETION_MESSAGE] IS NOT NULL AND LEN([COMPLETION_MESSAGE]) > 0 AND [COMPLETION_PHOTO_URL] IS NOT NULL AND [COMPLETED_AT] IS NOT NULL)";

    /// Database v2.12: the scheduled start (Cebu time) and the whole minutes from it to STARTED_AT (UTC + 8 hours),
    /// counted like DATEDIFF(MINUTE, ...) counts. The app counts the same way (StartRules.MinutesBetween).
    public const string BookingScheduledSql = "CAST([BOOKING_DATE] AS DATETIME) + CAST([BOOKING_TIME] AS DATETIME)";
    public const string BookingStartMinutesSql = "DATEDIFF(MINUTE, " + BookingScheduledSql + ", DATEADD(HOUR, 8, [STARTED_AT]))";

    /// MINUTES_LATE / IS_LATE (database v2.12): calculated, not stored. Only a start more than 15 minutes after the
    /// scheduled time is late; MINUTES_LATE is NULL for a start on time and for a booking that has not started.
    public const string BookingMinutesLateSql = "CASE WHEN " + BookingStartMinutesSql + " > 15 THEN " + BookingStartMinutesSql + " END";
    public const string BookingIsLateSql = "ISNULL(CAST(CASE WHEN " + BookingStartMinutesSql + " > 15 THEN 1 ELSE 0 END AS BIT), CAST(0 AS BIT))";

    /// CK_BOOKINGS_START (database v2.12): no start yet (both NULL), or a start on an In Progress / Completed booking
    /// from the scheduled time up to 2 hours after it, with a reason exactly when it was more than 15 minutes late.
    /// (A CHECK cannot read the calculated columns, so the minutes are written out here.)
    public const string BookingStartCheck =
        "([STARTED_AT] IS NULL AND [LATE_REASON] IS NULL) OR ([STARTED_AT] IS NOT NULL AND [STATUS] IN ('In Progress','Completed') AND "
        + BookingStartMinutesSql + " BETWEEN 0 AND 120 AND ((" + BookingStartMinutesSql + " > 15 AND [LATE_REASON] IS NOT NULL AND LEN([LATE_REASON]) > 0) OR ("
        + BookingStartMinutesSql + " <= 15 AND [LATE_REASON] IS NULL)))";

    /// CK_ADMIN_ACTIONS_TARGET (database v2.2): exactly one target column is filled, and it is the one TARGET_TYPE names.
    public const string AdminActionTargetCheck =
        "([TARGET_TYPE] = 'CUSTOMER' AND [TARGET_CUSTOMER_ID] IS NOT NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'SERVICE_PROVIDER' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NOT NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'REVIEW' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NOT NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'CATEGORY' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NOT NULL)";

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        cb.Properties<string>().HaveMaxLength(255);   // PDF used NVARCHAR(255) for most text
        cb.Properties<decimal>().HavePrecision(10, 2);  // money
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Primary keys whose names EF can't guess automatically
        mb.Entity<AdminAction>().HasKey(x => x.ActionId);
        mb.Entity<ProviderAvailability>().HasKey(x => x.AvailabilityId);
        mb.Entity<ProviderBreak>().HasKey(x => x.BreakId);
        mb.Entity<ProviderUnavailableDate>().HasKey(x => x.UnavailableId);
        mb.Entity<ServiceCategory>().HasKey(x => x.CategoryId);

        mb.Entity<User>(e =>
        {   
            e.ToTable("USERS", t =>
            {
                t.HasTrigger("trg_Users_Audit");
                t.HasTrigger("trg_Users_StatusSync");      // database v2.1: copies ACCOUNT_STATUS to CUSTOMERS / SERVICE_PROVIDERS
                t.HasCheckConstraint("CK_USERS_ROLE", "[ROLE] IN ('Customer','Service Provider','Administrator')");
                t.HasCheckConstraint("CK_USERS_STATUS", "[ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated')");
            });
            e.HasKey(x => x.UserId);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.MiddleName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.PhoneNumber).HasMaxLength(11);    // Philippine mobile number, always 09XXXXXXXXX
            e.Property(x => x.Role).HasMaxLength(50);
            e.Property(x => x.AccountStatus).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            // database v2.9: a row added by SQL (e.g. usp_RegisterProvider) starts "active now" too
            e.Property(x => x.LastActivityAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.LastActivityAt);          // database v2.9: the daily search for inactive accounts
        });

        mb.Entity<Customer>(e =>
        {
            e.ToTable("CUSTOMERS", t =>
            {
                t.HasTrigger("trg_Customers_StatusCopy");  // database v2.1
                t.HasTrigger("trg_Customers_Audit");       // database v2.10
                t.HasCheckConstraint("CK_CUSTOMERS_STATUS", "[ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated')");
            });
            e.Property(x => x.AccountStatus).HasMaxLength(50).HasDefaultValue(AccountStatuses.Active);
            e.HasOne(x => x.User).WithOne(u => u.Customer).HasForeignKey<Customer>(x => x.UserId);
            e.HasIndex(x => x.UserId).IsUnique();
        });

        mb.Entity<Provider>(e =>
        {
            e.ToTable("SERVICE_PROVIDERS", t =>
            {
                t.HasCheckConstraint("CK_PROVIDERS_RATING", "[AVG_RATING] IS NULL OR [AVG_RATING] BETWEEN 0 AND 5");
                t.HasCheckConstraint("CK_PROVIDERS_APPROVAL", "[APPROVAL_STATUS] IN ('Pending','Approved','Rejected')");
                t.HasCheckConstraint("CK_PROVIDERS_LIMITS", ProviderLimitsCheck);   // database v2.6: at most 20 jobs a day
                t.HasCheckConstraint("CK_PROVIDERS_COUNTS", "[REVIEW_COUNT] >= 0 AND ([BASE_FEE] IS NULL OR [BASE_FEE] >= 0)");   // database v2
                t.HasCheckConstraint("CK_PROVIDERS_STATUS", "[ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated')");          // database v2.1
                t.HasTrigger("trg_Providers_StatusCopy");  // database v2.1
                t.HasTrigger("trg_Providers_Audit");       // database v2.10
            });
            e.Property(x => x.BusinessName).HasMaxLength(155);
            e.Property(x => x.AccountStatus).HasMaxLength(50).HasDefaultValue(AccountStatuses.Active);
            e.Property(x => x.AvgRating).HasPrecision(2, 1);
            e.Property(x => x.ApprovalStatus).HasMaxLength(50);
            e.Property(x => x.DateApplied).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.User).WithOne(u => u.Provider).HasForeignKey<Provider>(x => x.UserId);
            e.HasIndex(x => x.UserId).IsUnique();
        });

        mb.Entity<ProviderAvailability>(e =>
        {
            e.ToTable("PROVIDER_AVAILABILITY", t =>
            {
                t.HasCheckConstraint("CK_AVAIL_DAY", "[DAY_OF_WEEK] BETWEEN 0 AND 6");
                t.HasCheckConstraint("CK_AVAIL_TIMES", "[END_TIME] > [START_TIME]");
            });
            e.HasOne(x => x.Provider).WithMany(p => p.Availabilities).HasForeignKey(x => x.ProviderId);
            e.HasIndex(x => new { x.ProviderId, x.DayOfWeek }).IsUnique();   // one schedule per day
        });

        mb.Entity<ProviderBreak>(e =>
        {
            e.ToTable("PROVIDER_BREAKS", t =>
                t.HasCheckConstraint("CK_BREAK_TIMES", "[BREAK_END] > [BREAK_START]"));
            e.HasOne(x => x.Availability).WithMany(a => a.Breaks).HasForeignKey(x => x.AvailabilityId);
        });

        mb.Entity<ProviderUnavailableDate>(e =>
        {
            e.ToTable("PROVIDER_UNAVAILABLE_DATES", t =>
            {
                t.HasCheckConstraint("CK_PROVIDER_UNAVAILABLE_DATES_SOURCE", DayOffSourceCheck);   // v2.7
                t.HasTrigger("trg_UnavailableDates_Audit");                                        // database v2.10
            });
            e.HasOne(x => x.Provider).WithMany(p => p.UnavailableDates).HasForeignKey(x => x.ProviderId);
            e.HasIndex(x => new { x.ProviderId, x.UnavailableDate }).IsUnique();
            // database v2.5: only ONE provider can be off on any date (first come, first served) - the database refuses a second
            e.HasIndex(x => x.UnavailableDate).IsUnique();
            // database v2.4: an extra day off approved by an administrator points to the ADMIN_ACTIONS row that approved it
            e.HasOne(x => x.ApprovedBy).WithMany().HasForeignKey(x => x.ApprovedByActionId).OnDelete(DeleteBehavior.Restrict);
            // database v2.7: a day of a provider's day-off request (no cascade: the app removes a request's days itself)
            e.HasOne(x => x.Request).WithMany(r => r.Days).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        });

        // database v2.7: a provider's request for 1 to 7 days off (its days are PROVIDER_UNAVAILABLE_DATES rows)
        mb.Entity<ProviderDayOffRequest>(e =>
        {
            e.ToTable("PROVIDER_DAY_OFF_REQUESTS", t =>
            {
                t.HasCheckConstraint("CK_PROVIDER_DAY_OFF_REQUESTS_STATUS", DayOffStatusCheck);
                t.HasCheckConstraint("CK_PROVIDER_DAY_OFF_REQUESTS_DECISION", DayOffDecisionCheck);
                t.HasTrigger("trg_DayOffRequests_Audit");      // database v2.10
            });
            e.HasKey(x => x.RequestId);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(DayOffStatuses.Pending);
            e.Property(x => x.RequestedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Provider).WithMany(p => p.DayOffRequests).HasForeignKey(x => x.ProviderId);
            // the decision is in the admin history, which is never deleted with it; one admin action decides one request
            e.HasOne(x => x.DecidedBy).WithMany().HasForeignKey(x => x.DecidedByActionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DecidedByActionId).IsUnique();
            e.HasIndex(x => new { x.ProviderId, x.Status });
        });

        // database v2.8: an approved provider's request to change the name on their account
        mb.Entity<ProviderNameChangeRequest>(e =>
        {
            e.ToTable("PROVIDER_NAME_CHANGE_REQUESTS", t =>
            {
                t.HasCheckConstraint("CK_PROVIDER_NAME_CHANGE_REQUESTS_STATUS", NameChangeStatusCheck);
                t.HasCheckConstraint("CK_PROVIDER_NAME_CHANGE_REQUESTS_DECISION", NameChangeDecisionCheck);
                t.HasTrigger("trg_NameChangeRequests_Audit");  // database v2.10
            });
            e.HasKey(x => x.RequestId);
            e.Property(x => x.FirstName).HasMaxLength(100);      // the same sizes as USERS
            e.Property(x => x.MiddleName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(NameChangeStatuses.Pending);
            e.Property(x => x.RequestedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Provider).WithMany(p => p.NameChangeRequests).HasForeignKey(x => x.ProviderId);
            // the decision is in the admin history, which is never deleted with it; one admin action decides one request
            e.HasOne(x => x.DecidedBy).WithMany().HasForeignKey(x => x.DecidedByActionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DecidedByActionId).IsUnique();
            e.HasIndex(x => new { x.ProviderId, x.Status });
            // one waiting request per provider: the database refuses a second one, even from two clicks at the same moment
            e.HasIndex(x => x.ProviderId)
                .IsUnique()
                .HasDatabaseName(NameChangeOnePendingIndex)
                .HasFilter(NameChangeOnePendingFilter);
        });

        mb.Entity<ServiceCategory>(e =>
        {
            e.ToTable("SERVICE_CATEGORIES");
            e.Property(x => x.CategoryName).HasMaxLength(100);
            e.HasIndex(x => x.CategoryName).IsUnique();
        });

        mb.Entity<ProviderCategory>(e =>
        {
            e.ToTable("PROVIDER_CATEGORIES");
            e.HasOne(x => x.Provider).WithMany(p => p.ProviderCategories).HasForeignKey(x => x.ProviderId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
            e.HasIndex(x => new { x.ProviderId, x.CategoryId }).IsUnique();   // no duplicates
        });

        mb.Entity<Service>(e =>
        {
            e.ToTable("SERVICES", t =>
            {
                t.HasTrigger("trg_Services_Audit");
                t.HasCheckConstraint("CK_SERVICES_PRICE", "[PRICE] >= 0");
                t.HasCheckConstraint("CK_SERVICES_DURATION", "[DURATION_MINUTES] > 0");
            });
            e.Property(x => x.ServiceName).HasMaxLength(100);
            e.HasOne(x => x.Provider).WithMany(p => p.Services).HasForeignKey(x => x.ProviderId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        });

        mb.Entity<Booking>(e =>
        {
            e.ToTable("BOOKINGS", t =>
            {
                t.HasTrigger("trg_Bookings_Audit");
                t.HasTrigger("trg_Bookings_StatusFlow");      // database v2: refuses status changes outside the booking life cycle
                t.HasCheckConstraint("CK_BOOKINGS_STATUS",
                    "[STATUS] IN ('Pending','Accepted','Rejected','In Progress','Completed','Cancelled')");
                t.HasCheckConstraint("CK_BOOKINGS_PAYMENT", "[PAYMENT_STATUS] IN ('Unpaid','Paid','Refunded')");
                t.HasCheckConstraint("CK_BOOKINGS_DURATION", "[TOTAL_DURATION_MINUTES] > 0");
                t.HasCheckConstraint("CK_BOOKINGS_FEE", "[TOTAL_FEE] >= 0");   // database v2
                t.HasCheckConstraint("CK_BOOKINGS_COMPLETION", BookingCompletionCheck);   // database v2.11
                t.HasCheckConstraint("CK_BOOKINGS_START", BookingStartCheck);             // database v2.12
            });
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.CompletionMessage).HasMaxLength(1000);       // database v2.11
            e.Property(x => x.CompletionPhotoUrl).HasMaxLength(500);       // database v2.11
            e.Property(x => x.LateReason).HasMaxLength(StartRules.MaxLateReasonLength);           // database v2.12
            e.Property(x => x.MinutesLate).HasComputedColumnSql(BookingMinutesLateSql, stored: false);
            e.Property(x => x.IsLate).HasComputedColumnSql(BookingIsLateSql, stored: false);
            e.Property(x => x.PaymentStatus).HasMaxLength(50);
            e.Property(x => x.EndTime)
                .HasComputedColumnSql("DATEADD(MINUTE, [TOTAL_DURATION_MINUTES], [BOOKING_TIME])");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Customer).WithMany(c => c.Bookings).HasForeignKey(x => x.CustomerId);
            e.HasOne(x => x.Provider).WithMany(p => p.Bookings).HasForeignKey(x => x.ProviderId);
            // Backstop against double-booking the exact same start time (real overlap check is in Stage 5).
            e.HasIndex(x => new { x.ProviderId, x.BookingDate, x.BookingTime })
                .IsUnique()
                .HasDatabaseName("UX_BOOKINGS_ACTIVE_SLOT")
                .HasFilter("[STATUS] IN ('Pending','Accepted','In Progress')");
            // database v2: faster "my bookings" lists (provider + status) and monthly reports (date + payment)
            e.HasIndex(x => new { x.ProviderId, x.Status }).HasDatabaseName("IX_BOOKINGS_PROVIDER_STATUS");
            e.HasIndex(x => new { x.BookingDate, x.PaymentStatus }).HasDatabaseName("IX_BOOKINGS_DATE_PAYMENT");
        });

        mb.Entity<BookingService>(e =>
        {
            e.ToTable("BOOKING_SERVICES");
            e.HasOne(x => x.Booking).WithMany(b => b.Services).HasForeignKey(x => x.BookingId);
            e.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId);
            e.HasIndex(x => new { x.BookingId, x.ServiceId }).IsUnique();
        });

        mb.Entity<Review>(e =>
        {
            e.ToTable("REVIEWS", t =>
            {
                t.HasTrigger("trg_Reviews_RatingSync");       // database v2: keeps AVG_RATING / REVIEW_COUNT right
                t.HasTrigger("trg_Reviews_Audit");            // database v2.10
                t.HasCheckConstraint("CK_REVIEWS_RATING", "[RATING] BETWEEN 1 AND 5");
                t.HasCheckConstraint("CK_REVIEWS_STATUS", "[STATUS] IN ('Visible','Flagged','Removed')");
            });
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Booking).WithOne(b => b.Review).HasForeignKey<Review>(x => x.BookingId);
            e.HasIndex(x => x.BookingId).IsUnique();   // one review per booking
        });

        mb.Entity<Message>(e =>
        {
            e.ToTable("MESSAGES");
            e.Property(x => x.MessageText).HasMaxLength(1000);
            e.Property(x => x.SentAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId);
            e.HasOne(x => x.Receiver).WithMany().HasForeignKey(x => x.ReceiverId);
            e.HasIndex(x => new { x.ReceiverId, x.IsRead }).HasDatabaseName("IX_MESSAGES_RECEIVER_UNREAD");   // database v2: unread badge
        });

        mb.Entity<SystemLog>(e =>
        {
            // database v2.10: only INSERT / UPDATE / DELETE / LOGIN
            e.ToTable("SYSTEM_LOGS", t => t.HasCheckConstraint("CK_SYSTEM_LOGS_ACTION_TYPE", AuditActionType.Check));
            e.Property(x => x.UserRole).HasMaxLength(50);
            e.Property(x => x.ActionType).HasMaxLength(20);
            e.Property(x => x.EntityName).HasMaxLength(50);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.CreatedAt);                                  // the "All" tab, newest first
            e.HasIndex(x => new { x.ActionType, x.CreatedAt });            // database v2.10: one action tab, newest first
            e.HasIndex(x => new { x.UserId, x.CreatedAt });                // database v2.10: one person's history
        });
        // database v2.3: PAYMENTS is the list of payment methods; PAYMENTS_DTL is each booking's payment
        mb.Entity<Payment>(e =>
        {
            e.ToTable("PAYMENTS");
            e.Property(x => x.MethodName).HasMaxLength(50);
            e.HasIndex(x => x.MethodName).IsUnique();
            e.HasData(
                new Payment { PaymentId = PaymentIds.Cash, MethodName = PaymentIds.CashName },
                new Payment { PaymentId = PaymentIds.GCash, MethodName = PaymentIds.GCashName },
                new Payment { PaymentId = PaymentIds.Maya, MethodName = PaymentIds.MayaName });
        });

        mb.Entity<PaymentDetail>(e =>
        {
            e.ToTable("PAYMENTS_DTL", t =>
            {
                t.HasTrigger("trg_PaymentsDtl_Audit");        // System Logs for payments
                t.HasCheckConstraint("CK_PAYMENTS_DTL_STATUS", "[PAYMENT_STATUS] IN ('PendingCollection','Paid','Refunded')");
                // only cash (PAYMENTS row 1) can wait to be collected, and anything paid (or refunded) has a paid date
                t.HasCheckConstraint("CK_PAYMENTS_DTL_PENDING_CASH", "[PAYMENT_STATUS] <> 'PendingCollection' OR [PAYMENT_ID] = 1");
                t.HasCheckConstraint("CK_PAYMENTS_DTL_PAID_AT", "[PAYMENT_STATUS] = 'PendingCollection' OR [PAID_AT] IS NOT NULL");
            });
            e.HasKey(x => x.PaymentDtlId);
            e.Property(x => x.PaymentStatus).HasMaxLength(50);
            e.Property(x => x.TransactionReference).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Booking).WithOne(b => b.PaymentDetail).HasForeignKey<PaymentDetail>(x => x.BookingId);
            e.HasOne(x => x.Payment).WithMany(p => p.Details).HasForeignKey(x => x.PaymentId);
            e.HasIndex(x => x.BookingId).IsUnique();                // one payment per booking: a second one is refused
            e.HasIndex(x => x.TransactionReference).IsUnique();
        });

        mb.Entity<AdminAction>(e =>
        {
            e.ToTable("ADMIN_ACTIONS", t =>
            {
                // database v2.2: a specific target type, and exactly one FOREIGN KEY column filled - the one it names
                t.HasCheckConstraint("CK_ADMIN_ACTIONS_TARGET_TYPE", "[TARGET_TYPE] IN ('CUSTOMER','SERVICE_PROVIDER','REVIEW','CATEGORY')");
                t.HasCheckConstraint("CK_ADMIN_ACTIONS_TARGET", AdminActionTargetCheck);
            });
            e.Property(x => x.ActionType).HasMaxLength(50);
            e.Property(x => x.TargetType).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminUserId);
            // the history is never deleted with its target (the app never deletes customers, providers, reviews or categories)
            e.HasOne(x => x.TargetCustomer).WithMany().HasForeignKey(x => x.TargetCustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetProvider).WithMany().HasForeignKey(x => x.TargetProviderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetReview).WithMany().HasForeignKey(x => x.TargetReviewId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetCategory).WithMany().HasForeignKey(x => x.TargetCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        // Column names: UPPER_SNAKE_CASE (e.g. CreatedAt -> CREATED_AT), like the PDF.
        foreach (var entity in mb.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(Regex.Replace(prop.Name, "([a-z0-9])([A-Z])", "$1_$2").ToUpperInvariant());

        // Safe default: never cascade-delete. Deleting a parent that has children fails.
        foreach (var fk in mb.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        // One deliberate exception (matches usp_DeleteAvailability in the PDF):
        // deleting a weekly schedule also deletes its breaks.
        mb.Entity<ProviderBreak>().HasOne(b => b.Availability).WithMany(a => a.Breaks)
            .HasForeignKey(b => b.AvailabilityId).OnDelete(DeleteBehavior.Cascade);
    }
}
