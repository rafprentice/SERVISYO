using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IProviderDashboardService
{
    Task<ProviderDashboardResult> GetAsync(int userId, CancellationToken ct = default);
}

public class ProviderDashboardService : IProviderDashboardService
{
    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);
    private const int SoonMinutes = 60;

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;

    public ProviderDashboardService(ServisyoDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
    }

    private sealed class JobRow
    {
        public int BookingId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateOnly BookingDate { get; set; }
        public TimeOnly BookingTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerCity { get; set; }
        public string? ServiceLocation { get; set; }
    }

    private static readonly Expression<Func<Booking, JobRow>> ToRow = b => new JobRow
    {
        BookingId = b.BookingId,
        Status = b.Status,
        BookingDate = b.BookingDate,
        BookingTime = b.BookingTime,
        CreatedAt = b.CreatedAt,
        CustomerName = b.Customer.User.FirstName + " " + b.Customer.User.LastName,
        CustomerCity = b.Customer.City,
        ServiceLocation = b.ServiceLocation
    };

    public async Task<ProviderDashboardResult> GetAsync(int userId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().ToOffset(ManilaOffset).DateTime;   // Manila wall-clock time
        var today = DateOnly.FromDateTime(now);
        var nowTime = TimeOnly.FromDateTime(now);

        // The provider is found from the SIGNED-IN user id only.
        var p = await _db.Providers.AsNoTracking()
            .Where(x => x.UserId == userId
                     && x.User.Role == Roles.ServiceProvider
                     && x.User.AccountStatus == AccountStatuses.Active)
            .Select(x => new
            {
                x.ProviderId, x.BusinessName, x.City, x.Description, x.ProfilePhoto, x.Address,
                x.AvgRating, x.ReviewCount, x.ApprovalStatus,
                x.User.FirstName, x.User.LastName, x.User.PhoneNumber
            })
            .FirstOrDefaultAsync(ct);

        if (p is null) return new(ProviderDashboardAccess.NotAllowed, null);
        if (p.ApprovalStatus == ApprovalStatuses.Pending) return new(ProviderDashboardAccess.Pending, null);
        if (p.ApprovalStatus != ApprovalStatuses.Approved) return new(ProviderDashboardAccess.Rejected, null);

        var mine = _db.Bookings.AsNoTracking().Where(b => b.ProviderId == p.ProviderId);

        // ---- Numbers ----
        var newRequests = await mine.CountAsync(b => b.Status == BookingStatuses.Pending, ct);
        var upcoming = await mine.CountAsync(b => b.Status == BookingStatuses.Accepted, ct);
        var inProgress = await mine.CountAsync(b => b.Status == BookingStatuses.InProgress, ct);

        // ---- Rows ----
        var recentRows = await mine.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BookingId)
            .Take(5).Select(ToRow).ToListAsync(ct);

        var todayRows = await mine
            .Where(b => b.BookingDate == today
                     && (b.Status == BookingStatuses.Accepted || b.Status == BookingStatuses.InProgress))
            .OrderBy(b => b.BookingTime).Take(6).Select(ToRow).ToListAsync(ct);

        var acceptedToday = await mine
            .Where(b => b.Status == BookingStatuses.Accepted && b.BookingDate <= today)
            .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime).Take(20).Select(ToRow).ToListAsync(ct);
        var soonRows = acceptedToday
            .Where(r => r.BookingDate.ToDateTime(r.BookingTime) <= now.AddMinutes(SoonMinutes))
            .Take(3).ToList();

        var activeRows = await mine.Where(b => b.Status == BookingStatuses.InProgress)
            .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime).Take(3).Select(ToRow).ToListAsync(ct);

        // finished jobs whose customer chose cash on completion and the cash is not confirmed yet
        var cashRows = await mine
            .Where(b => b.Status == BookingStatuses.Completed
                     && b.Payment != null && b.Payment.PaymentStatus == PaymentRecordStatuses.PendingCollection)
            .OrderBy(b => b.BookingDate).ThenBy(b => b.BookingTime).Take(3)
            .Select(b => new { b.BookingId, Amount = b.Payment!.Amount, CustomerName = b.Customer.User.FirstName + " " + b.Customer.User.LastName })
            .ToListAsync(ct);

        var ids = recentRows.Concat(todayRows).Concat(soonRows).Concat(activeRows)
                            .Select(r => r.BookingId).Concat(cashRows.Select(r => r.BookingId)).Distinct().ToList();
        var titles = await _db.GetBookingTitlesAsync(ids, ct);
        string TitleOf(int id) => titles.TryGetValue(id, out var t) ? t : "Service booking";

        // ---- "Need your attention": only things that are really true right now ----
        var attention = new List<AttentionItemViewModel>();
        if (newRequests > 0)
        {
            attention.Add(new AttentionItemViewModel
            {
                Text = $"{newRequests} new booking {(newRequests == 1 ? "request" : "requests")} waiting for your review",
                Tone = "info",
                ActionLabel = "Review Request"
            });
        }
        foreach (var r in soonRows)
        {
            var starts = r.BookingDate.ToDateTime(r.BookingTime);
            var minutes = (int)Math.Ceiling((starts - now).TotalMinutes);
            var what = $"{TitleOf(r.BookingId)}, {r.CustomerName}";
            attention.Add(new AttentionItemViewModel
            {
                Text = minutes > 0
                    ? $"Upcoming job in {minutes} min — {what}"
                    : $"Job was due {(r.BookingDate == today ? "at " + Time(r.BookingTime) : "on " + r.BookingDate.ToString("MMM d", CultureInfo.InvariantCulture))} — {what}",
                Tone = minutes > 0 ? "info" : "warning",
                ActionLabel = "Start Service"
            });
        }
        foreach (var r in activeRows)
        {
            attention.Add(new AttentionItemViewModel
            {
                Text = $"In progress: {TitleOf(r.BookingId)}, {r.CustomerName} — mark it complete when finished",
                Tone = "success",
                ActionLabel = "Complete Service"
            });
        }
        foreach (var r in cashRows)
        {
            attention.Add(new AttentionItemViewModel
            {
                Text = $"Collect {PaymentNotes.Money(r.Amount)} cash: {TitleOf(r.BookingId)}, {r.CustomerName} — confirm it once received",
                Tone = "warning",
                ActionLabel = "Confirm Cash"
            });
        }

        // ---- Availability today ----
        var dow = (byte)today.DayOfWeek;
        var offToday = await _db.ProviderUnavailableDates.AnyAsync(u => u.ProviderId == p.ProviderId && u.UnavailableDate == today, ct);
        var endToday = await _db.ProviderAvailabilities.AsNoTracking()
            .Where(a => a.ProviderId == p.ProviderId && a.IsAvailable && a.DayOfWeek == dow)
            .Select(a => (TimeOnly?)a.EndTime).FirstOrDefaultAsync(ct);
        var availabilityText =
            offToday ? "You marked today as unavailable"
            : endToday is null ? "No working hours set for today"
            : nowTime >= endToday.Value ? "Today's working hours have ended"
            : $"Available today until {Time(endToday.Value)}";

        // ---- Profile strength ----
        var hasService = await _db.Services.AnyAsync(s => s.ProviderId == p.ProviderId && s.IsActive, ct);
        var hasAvailability = await _db.ProviderAvailabilities.AnyAsync(a => a.ProviderId == p.ProviderId && a.IsAvailable, ct);
        var (percent, nextStep) = ProfileStrength.Calculate(
            hasService, hasAvailability,
            !string.IsNullOrWhiteSpace(p.Description),
            !string.IsNullOrWhiteSpace(p.ProfilePhoto),
            !string.IsNullOrWhiteSpace(p.Address) && !string.IsNullOrWhiteSpace(p.City),
            !string.IsNullOrWhiteSpace(p.PhoneNumber));

        // ---- Latest visible reviews of THIS provider ----
        var reviewRows = await _db.Reviews.AsNoTracking()
            .Where(r => r.Status == ReviewStatuses.Visible && r.Booking.ProviderId == p.ProviderId)
            .OrderByDescending(r => r.CreatedAt).Take(2)
            .Select(r => new
            {
                r.Rating, r.Comment, r.CreatedAt,
                First = r.Booking.Customer.User.FirstName,
                Last = r.Booking.Customer.User.LastName
            })
            .ToListAsync(ct);

        var fullName = $"{p.FirstName} {p.LastName}";
        var vm = new ProviderDashboardViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Provider",
                FirstName = p.FirstName,
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                Subtitle = p.BusinessName,
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            BusinessName = p.BusinessName,
            City = p.City,
            NewRequests = newRequests,
            Upcoming = upcoming,
            InProgress = inProgress,
            AvgRating = p.ReviewCount > 0 ? p.AvgRating : null,
            ReviewCount = p.ReviewCount,
            Attention = attention,
            RecentRequests = recentRows.Select(r => new ProviderJobViewModel
            {
                BookingId = r.BookingId,
                Title = TitleOf(r.BookingId),
                CustomerName = r.CustomerName,
                Date = r.BookingDate,
                Time = r.BookingTime,
                Location = LocationFor(r),
                Status = r.Status,
                Display = ProviderBookingDisplay.For(r.Status)
            }).ToList(),
            Today = todayRows.Select(r => new ScheduleItemViewModel
            {
                Time = r.BookingTime,
                Title = TitleOf(r.BookingId),
                CustomerName = r.CustomerName,
                Status = r.Status
            }).ToList(),
            AvailabilityText = availabilityText,
            ProfilePercent = percent,
            ProfileNextStep = nextStep,
            Reviews = reviewRows.Select(r => new ReviewItemViewModel
            {
                Rating = r.Rating,
                Comment = NameFormat.Truncate(r.Comment, 140),
                Author = NameFormat.ShortName(r.First, r.Last),
                Date = DateOnly.FromDateTime(r.CreatedAt)
            }).ToList()
        };

        return new(ProviderDashboardAccess.Allowed, vm);
    }

    /// The full service address is only shown once the provider has accepted the job;
    /// before that, only the customer's city.
    private static string? LocationFor(JobRow r)
    {
        var acceptedOrLater = r.Status is BookingStatuses.Accepted or BookingStatuses.InProgress or BookingStatuses.Completed;
        var text = acceptedOrLater && !string.IsNullOrWhiteSpace(r.ServiceLocation) ? r.ServiceLocation : r.CustomerCity;
        return string.IsNullOrWhiteSpace(text) ? null : NameFormat.Truncate(text, 40);
    }

    private static string Time(TimeOnly t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);
}
