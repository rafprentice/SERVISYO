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

    /// Database v2.3: the bell. Notifications are worked out by dbo.usp_GetNotifications (nothing is stored);
    /// the ones newer than this moment are unread. Null = the bell was never opened.
    public DateTime? NotificationsSeenAt { get; set; }

    public Customer? Customer { get; set; }
    public bool EmailVerificationPending { get; set; }
    public Provider? Provider { get; set; }
}

public class Customer
{
    public int CustomerId { get; set; }
    public int UserId { get; set; }
    public string? Address { get; set; }
    public string? ProfilePhoto { get; set; }
    /// Copy of User.AccountStatus (the real status). The database keeps it equal with triggers
    /// (trg_Users_StatusSync / trg_Customers_StatusCopy); the app also sets both together.
    public string AccountStatus { get; set; } = AccountStatuses.Active;

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
    public decimal? BaseFee { get; set; }
    public decimal? AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public int MinNoticeDays { get; set; } = 1;
    public int MaxJobsPerDay { get; set; } = 5;
    public string ApprovalStatus { get; set; } = ApprovalStatuses.Pending;
    /// Copy of User.AccountStatus (Active / Suspended / Deactivated), kept equal by trg_Users_StatusSync /
    /// trg_Providers_StatusCopy. Not the same as ApprovalStatus (Pending / Approved / Rejected).
    public string AccountStatus { get; set; } = AccountStatuses.Active;
    public DateTime DateApplied { get; set; } = DateTime.UtcNow;
    public DateTime? DateApproved { get; set; }

    public User User { get; set; } = null!;
    public ICollection<ProviderAvailability> Availabilities { get; set; } = new List<ProviderAvailability>();
    public ICollection<ProviderUnavailableDate> UnavailableDates { get; set; } = new List<ProviderUnavailableDate>();
    public ICollection<ProviderDayOffRequest> DayOffRequests { get; set; } = new List<ProviderDayOffRequest>();
    public ICollection<ProviderNameChangeRequest> NameChangeRequests { get; set; } = new List<ProviderNameChangeRequest>();   // v2.8
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

    /// Database v2.4: set when an ADMINISTRATOR gave this day as an extra day off (an emergency, e.g. sick today).
    /// It points to the ADMIN_ACTIONS row that says who gave it, when and why.
    public int? ApprovedByActionId { get; set; }

    /// Database v2.7: set when this day belongs to a provider's day-off REQUEST (1 to 7 days). The request says
    /// whether it is Pending or Approved; only an Approved request (or a day without a request) stops bookings.
    /// A day has a request OR an administrator's approval, never both (CK_PROVIDER_UNAVAILABLE_DATES_SOURCE).
    public int? RequestId { get; set; }

    public Provider Provider { get; set; } = null!;
    public AdminAction? ApprovedBy { get; set; }
    public ProviderDayOffRequest? Request { get; set; }
}

/// Database v2.7: a provider's request for 1 to 7 days off in a row. Its days are PROVIDER_UNAVAILABLE_DATES rows
/// (one per date, so the database still allows only one provider per date); the request holds what is said once:
/// who asked, why, when, and the administrator's answer.
public class ProviderDayOffRequest
{
    public int RequestId { get; set; }
    public int ProviderId { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = DayOffStatuses.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// The ADMIN_ACTIONS row of the decision (who approved or rejected it, and when). Null while Pending.
    public int? DecidedByActionId { get; set; }

    public Provider Provider { get; set; } = null!;
    public AdminAction? DecidedBy { get; set; }
    public ICollection<ProviderUnavailableDate> Days { get; set; } = new List<ProviderUnavailableDate>();
}

/// Database v2.8: an approved provider asks an administrator to change the name on their account (their name is
/// locked once they are approved). Only the NEW name is kept here: the current name is in USERS, and when the
/// request is approved the new name is copied there (the USERS audit trigger logs the old one in SYSTEM_LOGS).
public class ProviderNameChangeRequest
{
    public int RequestId { get; set; }
    public int ProviderId { get; set; }

    /// The name the provider asks for (the same rules as at registration).
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;

    /// Why (for example "Married, new last name" or "Spelling mistake"). Required.
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = NameChangeStatuses.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// The ADMIN_ACTIONS row of the decision (who approved or rejected it, and when). Null while Pending.
    public int? DecidedByActionId { get; set; }

    public Provider Provider { get; set; } = null!;
    public AdminAction? DecidedBy { get; set; }
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
    public PaymentDetail? PaymentDetail { get; set; }   // PAYMENTS_DTL (one per booking, made by Approve & Pay)
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

/// ADMIN_ACTIONS: one row per important administrator action. Database v2.2: the target is stored in the ONE column
/// that matches TARGET_TYPE, and each of those columns is a real FOREIGN KEY (CK_ADMIN_ACTIONS_TARGET keeps exactly one filled).
public class AdminAction
{
    public int ActionId { get; set; }
    public int AdminUserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;   // AdminTargetTypes: CUSTOMER | SERVICE_PROVIDER | REVIEW | CATEGORY
    public int? TargetCustomerId { get; set; }              // -> CUSTOMERS          (TARGET_TYPE = CUSTOMER)
    public int? TargetProviderId { get; set; }              // -> SERVICE_PROVIDERS  (TARGET_TYPE = SERVICE_PROVIDER)
    public int? TargetReviewId { get; set; }                // -> REVIEWS            (TARGET_TYPE = REVIEW)
    public int? TargetCategoryId { get; set; }              // -> SERVICE_CATEGORIES (TARGET_TYPE = CATEGORY)
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Admin { get; set; } = null!;
    public Customer? TargetCustomer { get; set; }
    public Provider? TargetProvider { get; set; }
    public Review? TargetReview { get; set; }
    public ServiceCategory? TargetCategory { get; set; }

    /// The four kinds of target, each filling only its own column.
    public static AdminAction ForCustomer(int adminUserId, string actionType, int customerId, string? reason) =>
        new() { AdminUserId = adminUserId, ActionType = actionType, TargetType = AdminTargetTypes.Customer, TargetCustomerId = customerId, Reason = reason };

    public static AdminAction ForProvider(int adminUserId, string actionType, int providerId, string? reason) =>
        new() { AdminUserId = adminUserId, ActionType = actionType, TargetType = AdminTargetTypes.ServiceProvider, TargetProviderId = providerId, Reason = reason };

    public static AdminAction ForReview(int adminUserId, string actionType, int reviewId, string? reason) =>
        new() { AdminUserId = adminUserId, ActionType = actionType, TargetType = AdminTargetTypes.Review, TargetReviewId = reviewId, Reason = reason };

    public static AdminAction ForCategory(int adminUserId, string actionType, int categoryId, string? reason) =>
        new() { AdminUserId = adminUserId, ActionType = actionType, TargetType = AdminTargetTypes.Category, TargetCategoryId = categoryId, Reason = reason };
}

/// ADMIN_ACTIONS.TARGET_TYPE (database v2.2: there is no "USER" any more).
public static class AdminTargetTypes
{
    public const string Customer = "CUSTOMER";
    public const string ServiceProvider = "SERVICE_PROVIDER";
    public const string Review = "REVIEW";
    public const string Category = "CATEGORY";

    public static readonly IReadOnlyList<string> All = new[] { Customer, ServiceProvider, Review, Category };
}
