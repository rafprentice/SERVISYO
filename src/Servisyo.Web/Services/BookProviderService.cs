using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IBookProviderService
{
    /// The "Book this Provider" form for the signed-in customer.
    Task<BookProviderPageResult> GetPageAsync(int userId, int providerId, CancellationToken ct = default);

    /// The start times that are free on a date for the chosen services (the form shows them as buttons).
    Task<BookSlotsResult> GetSlotsAsync(int userId, int providerId, DateOnly date, IReadOnlyCollection<int> serviceIds, CancellationToken ct = default);

    /// Creates a Pending booking. Everything is checked again here (never trusting what the page showed), inside a transaction.
    Task<BookProviderResult> CreateAsync(int userId, int providerId, BookProviderForm form, CancellationToken ct = default);
}

public class BookProviderService : IBookProviderService
{
    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);     // the Philippines has no daylight saving
    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();
    private static readonly string[] ActiveStatuses = { BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress };

    private const string FixFields = "Please fix the highlighted fields.";
    private const string ProviderGone = "That provider isn't available for booking right now.";

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<BookProviderService> _logger;

    public BookProviderService(ServisyoDbContext db, TimeProvider? clock = null, ILogger<BookProviderService>? logger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BookProviderService>.Instance;
    }

    private (DateOnly Today, TimeOnly NowTime) ManilaNow()
    {
        var now = _clock.GetUtcNow().ToOffset(ManilaOffset);
        return (DateOnly.FromDateTime(now.DateTime), TimeOnly.FromDateTime(now.DateTime));
    }

    // ------------------------------------------------------------------ small loaders

    private sealed record Who(int CustomerId, string FirstName, string LastName, string? Address);

    /// The customer is found from the SIGNED-IN user id only; only an Active customer may book.
    private Task<Who?> FindCustomerAsync(int userId, CancellationToken ct) =>
        _db.Customers.AsNoTracking()
            .Where(c => c.UserId == userId && c.User.Role == Roles.Customer && c.User.AccountStatus == AccountStatuses.Active)
            .Select(c => new Who(c.CustomerId, c.User.FirstName, c.User.LastName, c.Address))
            .FirstOrDefaultAsync(ct);

    private sealed record ProviderInfo(int ProviderId, int UserId, string Name, string BusinessName, string? City,
        decimal? AvgRating, int ReviewCount, int MinNoticeDays, int MaxJobsPerDay);

    /// A provider can be booked when: Approved + Active account (same rule as everywhere) + at least one active service.
    private Task<ProviderInfo?> FindProviderAsync(int providerId, CancellationToken ct) =>
        _db.Providers.AsNoTracking()
            .WhereEligible()
            .Where(p => p.ProviderId == providerId && p.Services.Any(s => s.IsActive && s.Category.IsActive))
            .Select(p => new ProviderInfo(p.ProviderId, p.UserId, p.User.FirstName + " " + p.User.LastName, p.BusinessName, p.City,
                p.AvgRating, p.ReviewCount, p.MinNoticeDays, p.MaxJobsPerDay))
            .FirstOrDefaultAsync(ct);

    private sealed record ServiceRow(int ServiceId, string Name, string CategoryName, string? Description, int DurationMinutes, decimal Price);

    private async Task<List<ServiceRow>> LoadServicesAsync(int providerId, IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        // only this provider's ACTIVE services in an ACTIVE category: anything else is "no longer available"
        var query = _db.Services.AsNoTracking()
            .Where(s => s.ProviderId == providerId && s.IsActive && s.Category.IsActive);
        if (ids is not null)
        {
            var list = ids.ToList();
            query = query.Where(s => list.Contains(s.ServiceId));
        }

        return await query
            .OrderBy(s => s.Category.CategoryName).ThenBy(s => s.ServiceName).ThenBy(s => s.ServiceId)
            .Select(s => new ServiceRow(s.ServiceId, s.ServiceName, s.Category.CategoryName, s.Description, s.DurationMinutes, s.Price))
            .ToListAsync(ct);
    }

    private sealed record DayData(bool Unavailable, List<BookingSlotRules.Window> Windows, List<BookingSlotRules.Busy> Busy);

    /// What the rules need to know about ONE date of ONE provider: schedule + breaks, unavailable date, active bookings.
    private async Task<DayData> LoadDayAsync(int providerId, DateOnly date, CancellationToken ct)
    {
        var weekday = (byte)date.DayOfWeek;      // 0 = Sunday ... 6 = Saturday, the same numbers as PROVIDER_AVAILABILITY

        var schedule = await _db.ProviderAvailabilities.AsNoTracking()
            .Where(a => a.ProviderId == providerId && a.DayOfWeek == weekday && a.IsAvailable)
            .Select(a => new { a.AvailabilityId, a.StartTime, a.EndTime })
            .ToListAsync(ct);

        var scheduleIds = schedule.Select(s => s.AvailabilityId).ToList();
        var breaks = scheduleIds.Count == 0
            ? new List<(int AvailabilityId, TimeOnly Start, TimeOnly End)>()
            : (await _db.ProviderBreaks.AsNoTracking()
                    .Where(b => scheduleIds.Contains(b.AvailabilityId))
                    .Select(b => new { b.AvailabilityId, b.BreakStart, b.BreakEnd })
                    .ToListAsync(ct))
                .Select(b => (AvailabilityId: b.AvailabilityId, Start: b.BreakStart, End: b.BreakEnd)).ToList();

        var windows = schedule
            .Select(s => new BookingSlotRules.Window(s.StartTime, s.EndTime,
                breaks.Where(b => b.AvailabilityId == s.AvailabilityId).Select(b => (b.Start, b.End)).ToList()))
            .ToList();

        var unavailable = await _db.ProviderUnavailableDates.AsNoTracking()
            .AnyAsync(u => u.ProviderId == providerId && u.UnavailableDate == date, ct);

        var busy = (await _db.Bookings.AsNoTracking()
                .Where(b => b.ProviderId == providerId && b.BookingDate == date && ActiveStatuses.Contains(b.Status))
                .Select(b => new { b.BookingTime, b.TotalDurationMinutes })
                .ToListAsync(ct))
            .Select(b => new BookingSlotRules.Busy(b.BookingTime, b.TotalDurationMinutes))
            .ToList();

        return new DayData(unavailable, windows, busy);
    }

    private static PortalShellViewModel ShellOf(Who who, int unreadNotifications, int unreadMessages)
    {
        var fullName = $"{who.FirstName} {who.LastName}".Trim();
        return new PortalShellViewModel
        {
            Role = "Customer",
            Subtitle = "Customer Account",
            FirstName = who.FirstName,
            FullName = fullName,
            Initials = NameFormat.Initials(fullName),
            UnreadNotifications = unreadNotifications,
            UnreadMessages = unreadMessages
        };
    }

    private async Task<PortalShellViewModel> BuildShellAsync(Who who, int userId, CancellationToken ct) =>
        ShellOf(who,
            await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
            await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct));

    // ------------------------------------------------------------------ the form

    public async Task<BookProviderPageResult> GetPageAsync(int userId, int providerId, CancellationToken ct = default)
    {
        var who = await FindCustomerAsync(userId, ct);
        if (who is null) return new(BookPageOutcome.NotAllowed, null, null);

        var shell = await BuildShellAsync(who, userId, ct);

        var provider = await FindProviderAsync(providerId, ct);
        if (provider is null) return new(BookPageOutcome.ProviderUnavailable, null, shell);

        var services = await LoadServicesAsync(providerId, null, ct);
        var (today, _) = ManilaNow();

        var vm = new BookProviderPageViewModel
        {
            Shell = shell,
            ProviderId = provider.ProviderId,
            ProviderName = provider.Name.Trim(),
            Initials = NameFormat.Initials(provider.Name),
            BusinessName = provider.BusinessName,
            City = string.IsNullOrWhiteSpace(provider.City) ? null : provider.City.Trim(),
            AvgRating = provider.ReviewCount > 0 ? provider.AvgRating : null,
            ReviewCount = provider.ReviewCount,
            MinNoticeDays = provider.MinNoticeDays,
            Services = services.Select(s => new BookServiceOptionViewModel
            {
                ServiceId = s.ServiceId, Name = s.Name, CategoryName = s.CategoryName,
                Description = string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim(),
                DurationMinutes = s.DurationMinutes, Price = s.Price
            }).ToList(),
            EarliestDate = today.AddDays(Math.Max(0, provider.MinNoticeDays)),
            LatestDate = today.AddDays(BookingSlotRules.MaxAdvanceDays),
            // the saved address of the profile is offered as the place of service (the customer can change it)
            Form = new BookProviderForm { Location = string.IsNullOrWhiteSpace(who.Address) ? null : who.Address.Trim() }
        };

        return new(BookPageOutcome.Ok, vm, shell);
    }

    // ------------------------------------------------------------------ free times

    public async Task<BookSlotsResult> GetSlotsAsync(int userId, int providerId, DateOnly date, IReadOnlyCollection<int> serviceIds, CancellationToken ct = default)
    {
        var who = await FindCustomerAsync(userId, ct);
        if (who is null) return new(false, false, null, Array.Empty<SlotViewModel>());

        var provider = await FindProviderAsync(providerId, ct);
        if (provider is null) return new(true, false, ProviderGone, Array.Empty<SlotViewModel>());

        var ids = serviceIds.Distinct().Take(BookingFormRules.MaxServices + 1).ToList();
        var services = ids.Count == 0 ? new List<ServiceRow>() : await LoadServicesAsync(providerId, ids, ct);
        if (services.Count == 0 || services.Count != ids.Count)
            return new(true, true, "Choose at least one service first.", Array.Empty<SlotViewModel>());

        var duration = services.Sum(s => s.DurationMinutes);
        var (today, nowTime) = ManilaNow();
        var day = await LoadDayAsync(providerId, date, ct);

        var slots = BookingSlotRules.Slots(date, duration, today, nowTime, provider.MinNoticeDays, day.Unavailable, day.Windows, day.Busy, provider.MaxJobsPerDay);
        if (slots.Count > 0)
            return new(true, true, null, slots.Select(t => new SlotViewModel(
                t.ToString("HH:mm", CultureInfo.InvariantCulture), t.ToString("h:mm tt", CultureInfo.InvariantCulture))).ToList());

        var dayProblem = BookingSlotRules.CheckDay(date, today, provider.MinNoticeDays, day.Unavailable, day.Windows, day.Busy.Count, provider.MaxJobsPerDay);
        var message = dayProblem != SlotCheck.Ok
            ? BookingSlotRules.Explain(dayProblem, provider.MinNoticeDays)
            : "No free times on that date for the services you chose. Try another date or fewer services.";
        return new(true, true, message, Array.Empty<SlotViewModel>());
    }

    // ------------------------------------------------------------------ creating the booking

    public async Task<BookProviderResult> CreateAsync(int userId, int providerId, BookProviderForm form, CancellationToken ct = default)
    {
        // 1. who is asking: from the sign-in cookie, and only an Active customer may book
        var who = await FindCustomerAsync(userId, ct);
        if (who is null) return new(BookOutcome.NotAllowed, NoErrors, string.Empty);

        // 2. the provider must be bookable (Approved, Active account, at least one active service)
        var provider = await FindProviderAsync(providerId, ct);
        if (provider is null)
        {
            _logger.LogWarning("Customer {CustomerId} tried to book provider {ProviderId}, who cannot be booked.", who.CustomerId, providerId);
            return new(BookOutcome.ProviderUnavailable, NoErrors, ProviderGone);
        }

        // 3. the form: every problem is collected, nothing is saved until all are fixed
        var errors = new Dictionary<string, string>();

        var ids = (form.ServiceIds ?? new List<int>()).Distinct().ToList();
        List<ServiceRow> services = new();
        if (ids.Count == 0)
            errors["ServiceIds"] = "Choose at least one service.";
        else if (ids.Count > BookingFormRules.MaxServices)
            errors["ServiceIds"] = $"You can book at most {BookingFormRules.MaxServices} services at once.";
        else
        {
            services = await LoadServicesAsync(providerId, ids, ct);
            if (services.Count != ids.Count)       // another provider's service, an inactive one, or one that does not exist
                errors["ServiceIds"] = "One of the selected services is no longer available. Please review your choices.";
        }

        var location = ProfileRules.NormalizeText(form.Location);
        if (location is null) errors["Location"] = "Enter the address where the service will be done.";
        else if (location.Length > BookingFormRules.MaxLocationLength)
            errors["Location"] = $"The address can be at most {BookingFormRules.MaxLocationLength} characters.";

        var description = ProfileRules.NormalizeText(form.Description);
        if (description is null) errors["Description"] = "Describe what you need done.";
        else if (description.Length > BookingFormRules.MaxDescriptionLength)
            errors["Description"] = $"The description can be at most {BookingFormRules.MaxDescriptionLength} characters.";

        if (form.Date is null) errors["Date"] = "Choose a date.";
        if (form.Time is null) errors["Time"] = "Choose a time.";

        if (errors.Count > 0)
            return new(BookOutcome.Invalid, errors, FixFields);

        var date = form.Date!.Value;
        var time = form.Time!.Value;
        var duration = services.Sum(s => s.DurationMinutes);
        var total = services.Sum(s => s.Price);

        // 4. check the schedule and save, all inside ONE transaction so two customers cannot take the same time.
        //    Serializable makes the database lock what was read (the provider's bookings of that day), so the second
        //    request waits or fails instead of also seeing the time as free. The unique index on
        //    (provider, date, time) is a second safety net.
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        try
        {
            var (today, nowTime) = ManilaNow();
            var day = await LoadDayAsync(providerId, date, ct);

            var problem = BookingSlotRules.Check(date, time, duration, today, nowTime, provider.MinNoticeDays,
                day.Unavailable, day.Windows, day.Busy, provider.MaxJobsPerDay);

            if (problem == SlotCheck.Ok
                && !BookingSlotRules.Slots(date, duration, today, nowTime, provider.MinNoticeDays, day.Unavailable, day.Windows, day.Busy, provider.MaxJobsPerDay).Contains(time))
            {
                // a time that is not one of the offered start times (for example typed by hand)
                errors["Time"] = "Please choose one of the available times.";
                return new(BookOutcome.Invalid, errors, FixFields);
            }

            if (problem != SlotCheck.Ok)
            {
                errors[BookingSlotRules.IsDateProblem(problem) ? "Date" : "Time"] = BookingSlotRules.Explain(problem, provider.MinNoticeDays);
                _logger.LogInformation("Customer {CustomerId}: booking with provider {ProviderId} refused ({Problem}).", who.CustomerId, providerId, problem);
                return new(BookOutcome.Invalid, errors, FixFields);
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var booking = new Booking
            {
                CustomerId = who.CustomerId,           // from the sign-in cookie
                ProviderId = providerId,
                BookingDate = date,
                BookingTime = time,
                TotalDurationMinutes = duration,       // worked out here from the services, never from the browser
                TotalFee = total,
                Status = BookingStatuses.Pending,      // a new booking always starts Pending
                PaymentStatus = PaymentStatuses.Unpaid,
                ServiceLocation = location,
                ServiceDescription = description,
                CreatedAt = now,
                UpdatedAt = now
            };
            // SQL Server generates the row version; the in-memory test database cannot, so it needs a value
            if (!relational) booking.RowVersion = new byte[] { 1 };

            foreach (var s in services)
                booking.Services.Add(new BookingService { ServiceId = s.ServiceId, PriceSnapshot = s.Price, DurationMinutesSnapshot = s.DurationMinutes });

            _db.Bookings.Add(booking);
            await _db.SaveChangesAsync(ct);

            // tell the provider (same wording as usp_CreateBooking)
            _db.Notifications.Add(new Notification
            {
                UserId = provider.UserId,
                Message = $"You have a new booking request (Booking #{booking.BookingId}).",
                CreatedAt = now
            });
            await _db.SaveChangesAsync(ct);

            if (tx is not null) await tx.CommitAsync(ct);

            _logger.LogInformation("Customer {CustomerId} booked provider {ProviderId}: booking {BookingId} on {Date} {Time}.",
                who.CustomerId, providerId, booking.BookingId, date, time);
            return new(BookOutcome.Created, NoErrors, string.Empty, booking.BookingId, provider.Name.Trim());
        }
        catch (Exception ex) when (ex is DbUpdateException || ex.GetType().Name == "SqlException")
        {
            // another customer took the time at the same moment (unique index, lock timeout or deadlock victim)
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Customer {CustomerId}: booking with provider {ProviderId} on {Date} {Time} lost a race or failed to save.",
                who.CustomerId, providerId, date, time);
            return new(BookOutcome.Invalid,
                new Dictionary<string, string> { ["Time"] = "That time was just taken by someone else. Please choose another." },
                FixFields);
        }
    }
}
