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
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ProviderCategory> ProviderCategories => Set<ProviderCategory>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingService> BookingServices => Set<BookingService>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<AdminAction> AdminActions => Set<AdminAction>();
    public DbSet<BookingReport> BookingReports => Set<BookingReport>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        cb.Properties<string>().HaveMaxLength(255);   // PDF used NVARCHAR(255) for most text
        cb.Properties<decimal>().HavePrecision(10, 2);  // money
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Primary keys whose names EF can't guess automatically
        mb.Entity<AdminAction>().HasKey(x => x.ActionId);
        mb.Entity<BookingReport>().HasKey(x => x.ReportId);
        mb.Entity<ProviderAvailability>().HasKey(x => x.AvailabilityId);
        mb.Entity<ProviderBreak>().HasKey(x => x.BreakId);
        mb.Entity<ProviderUnavailableDate>().HasKey(x => x.UnavailableId);
        mb.Entity<ServiceCategory>().HasKey(x => x.CategoryId);

        mb.Entity<User>(e =>
        {   
            e.ToTable("USERS", t =>
            {
                t.HasCheckConstraint("CK_USERS_ROLE", "[ROLE] IN ('Customer','Service Provider','Administrator')");
                t.HasCheckConstraint("CK_USERS_STATUS", "[ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated')");
            });
            e.HasKey(x => x.UserId);
            e.Property(x => x.FirstName).HasMaxLength(155);
            e.Property(x => x.MiddleName).HasMaxLength(155);
            e.Property(x => x.LastName).HasMaxLength(155);
            e.Property(x => x.Email).HasMaxLength(155);
            e.Property(x => x.PhoneNumber).HasMaxLength(15);
            e.Property(x => x.Role).HasMaxLength(50);
            e.Property(x => x.AccountStatus).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.Email).IsUnique();
        });

        mb.Entity<Customer>(e =>
        {
            e.ToTable("CUSTOMERS");
            e.HasOne(x => x.User).WithOne(u => u.Customer).HasForeignKey<Customer>(x => x.UserId);
            e.HasIndex(x => x.UserId).IsUnique();
        });

        mb.Entity<Provider>(e =>
        {
            e.ToTable("SERVICE_PROVIDERS", t =>
            {
                t.HasCheckConstraint("CK_PROVIDERS_RATING", "[AVG_RATING] IS NULL OR [AVG_RATING] BETWEEN 0 AND 5");
                t.HasCheckConstraint("CK_PROVIDERS_APPROVAL", "[APPROVAL_STATUS] IN ('Pending','Approved','Rejected')");
                t.HasCheckConstraint("CK_PROVIDERS_LIMITS", "[MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] > 0");
            });
            e.Property(x => x.BusinessName).HasMaxLength(155);
            e.Property(x => x.City).HasMaxLength(50);
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
            e.ToTable("PROVIDER_UNAVAILABLE_DATES");
            e.HasOne(x => x.Provider).WithMany(p => p.UnavailableDates).HasForeignKey(x => x.ProviderId);
            e.HasIndex(x => new { x.ProviderId, x.UnavailableDate }).IsUnique();
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
                t.HasCheckConstraint("CK_BOOKINGS_STATUS",
                    "[STATUS] IN ('Pending','Accepted','Rejected','In Progress','Completed','Cancelled')");
                t.HasCheckConstraint("CK_BOOKINGS_PAYMENT", "[PAYMENT_STATUS] IN ('Unpaid','Paid','Refunded')");
                t.HasCheckConstraint("CK_BOOKINGS_DURATION", "[TOTAL_DURATION_MINUTES] > 0");
            });
            e.Property(x => x.Status).HasMaxLength(50);
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
        });

        mb.Entity<EmailOtp>(e =>
        {
            e.ToTable("EMAIL_OTPS");
            e.Property(x => x.Purpose).HasMaxLength(30);
            e.Property(x => x.CodeHash).HasMaxLength(255);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.Purpose });
        });
        mb.Entity<Notification>(e =>
        {
            e.ToTable("NOTIFICATIONS");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        mb.Entity<AdminAction>(e =>
        {
            e.ToTable("ADMIN_ACTIONS");
            e.Property(x => x.ActionType).HasMaxLength(50);
            e.Property(x => x.TargetType).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminUserId);
        });

        mb.Entity<BookingReport>(e =>
        {
            e.ToTable("BOOKING_REPORTS", t =>
                t.HasCheckConstraint("CK_REPORTS_STATUS", "[STATUS] IN ('Open','Under Review','Resolved')"));
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Booking).WithMany(b => b.Reports).HasForeignKey(x => x.BookingId);
            e.HasOne(x => x.ReportedBy).WithMany().HasForeignKey(x => x.ReportedByUserId);
            e.HasOne(x => x.ResolvedBy).WithMany().HasForeignKey(x => x.ResolvedByUserId);
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
