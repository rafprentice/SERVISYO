namespace Servisyo.Web.Models;

// NOTE: SERVICE_PROVIDERS is named "Provider" in C# to avoid a clash with
// Microsoft.Extensions.DependencyInjection.ServiceProvider.

public class User
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = Roles.Customer;
    public string AccountStatus { get; set; } = AccountStatuses.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Customer? Customer { get; set; }
    public Provider? Provider { get; set; }
}

public class Customer
{
    public int CustomerId { get; set; }
    public int UserId { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ProfilePhoto { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public class Provider
{
    public int ProviderId { get; set; }
    public int UserId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ProfilePhoto { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public decimal? BaseFee { get; set; }
    public decimal? AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public int MinNoticeDays { get; set; } = 1;
    public int MaxJobsPerDay { get; set; } = 5;
    public string ApprovalStatus { get; set; } = ApprovalStatuses.Pending;
    public DateTime DateApplied { get; set; } = DateTime.UtcNow;
    public DateTime? DateApproved { get; set; }

    public User User { get; set; } = null!;
    public ICollection<ProviderAvailability> Availabilities { get; set; } = new List<ProviderAvailability>();
    public ICollection<ProviderUnavailableDate> UnavailableDates { get; set; } = new List<ProviderUnavailableDate>();
    public ICollection<ProviderCategory> ProviderCategories { get; set; } = new List<ProviderCategory>();
    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public class ProviderAvailability
{
    public int AvailabilityId { get; set; }
    public int ProviderId { get; set; }
    public byte DayOfWeek { get; set; }          // 0 = Sunday ... 6 = Saturday
    public bool IsAvailable { get; set; } = true;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public Provider Provider { get; set; } = null!;
    public ICollection<ProviderBreak> Breaks { get; set; } = new List<ProviderBreak>();
}

public class ProviderBreak
{
    public int BreakId { get; set; }
    public int AvailabilityId { get; set; }
    public TimeOnly BreakStart { get; set; }
    public TimeOnly BreakEnd { get; set; }

    public ProviderAvailability Availability { get; set; } = null!;
}

public class ProviderUnavailableDate
{
    public int UnavailableId { get; set; }
    public int ProviderId { get; set; }
    public DateOnly UnavailableDate { get; set; }
    public string? Reason { get; set; }

    public Provider Provider { get; set; } = null!;
}

public class ServiceCategory
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProviderCategory
{
    public int ProviderCategoryId { get; set; }
    public int ProviderId { get; set; }
    public int CategoryId { get; set; }

    public Provider Provider { get; set; } = null!;
    public ServiceCategory Category { get; set; } = null!;
}

public class Service
{
    public int ServiceId { get; set; }
    public int ProviderId { get; set; }
    public int CategoryId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; } = 60;   // NEW: needed for overlap detection
    public bool IsActive { get; set; } = true;

    public Provider Provider { get; set; } = null!;
    public ServiceCategory Category { get; set; } = null!;
}

public class Booking
{
    public int BookingId { get; set; }
    public int CustomerId { get; set; }
    public int ProviderId { get; set; }
    public DateOnly BookingDate { get; set; }
    public TimeOnly BookingTime { get; set; }
    public int TotalDurationMinutes { get; set; }
    public TimeOnly EndTime { get; private set; }      // computed column, read-only
    public string? ServiceLocation { get; set; }
    public string? ServiceDescription { get; set; }
    public decimal TotalFee { get; set; }
    public string Status { get; set; } = BookingStatuses.Pending;
    public string PaymentStatus { get; set; } = PaymentStatuses.Unpaid;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = null!;     // concurrency token

    public Customer Customer { get; set; } = null!;
    public Provider Provider { get; set; } = null!;
    public ICollection<BookingService> Services { get; set; } = new List<BookingService>();
    public Review? Review { get; set; }
    public ICollection<BookingReport> Reports { get; set; } = new List<BookingReport>();
}

// NEW: lets one booking contain one or more services.
public class BookingService
{
    public int BookingServiceId { get; set; }
    public int BookingId { get; set; }
    public int ServiceId { get; set; }
    public decimal PriceSnapshot { get; set; }
    public int DurationMinutesSnapshot { get; set; }

    public Booking Booking { get; set; } = null!;
    public Service Service { get; set; } = null!;
}

public class Review
{
    public int ReviewId { get; set; }
    public int BookingId { get; set; }
    public byte Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = ReviewStatuses.Visible;

    public Booking Booking { get; set; } = null!;
}

public class Message
{
    public int MessageId { get; set; }
    public int SenderId { get; set; }
    public int ReceiverId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public User Sender { get; set; } = null!;
    public User Receiver { get; set; } = null!;
}

public class Notification
{
    public int NotificationId { get; set; }
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

public class AdminAction
{
    public int ActionId { get; set; }
    public int AdminUserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public int TargetId { get; set; }                    // polymorphic: no FK by design
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Admin { get; set; } = null!;
}

public class BookingReport
{
    public int ReportId { get; set; }
    public int BookingId { get; set; }
    public int ReportedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = ReportStatuses.Open;
    public int? ResolvedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    public Booking Booking { get; set; } = null!;
    public User ReportedBy { get; set; } = null!;
    public User? ResolvedBy { get; set; }
}
