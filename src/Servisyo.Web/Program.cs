using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));

builder.Services.AddDbContext<ServisyoDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
           .AddInterceptors(new Servisyo.Web.Data.AuditSessionInterceptor(new Microsoft.AspNetCore.Http.HttpContextAccessor())));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISystemLogService, SystemLogService>();
builder.Services.AddScoped<IAdminReportService, AdminReportService>();
builder.Services.AddScoped<IProfilePhotoService, ProfilePhotoService>();
builder.Services.AddScoped<IBookingPaymentService, BookingPaymentService>();
builder.Services.AddScoped<IBookingLiveUpdates, BookingLiveUpdates>();
builder.Services.AddScoped<IDayOffLiveUpdates, DayOffLiveUpdates>();      // database v2.7: day-off requests, live
builder.Services.AddScoped<INameChangeLiveUpdates, NameChangeLiveUpdates>();   // database v2.8: name change requests, live
builder.Services.AddScoped<INameChangeRequestService, NameChangeRequestService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<ICustomerReviewService, CustomerReviewService>();
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.AddScoped<IBookProviderService, BookProviderService>();
builder.Services.AddScoped<ICategoryManagementService, CategoryManagementService>();
builder.Services.AddScoped<ICustomerBookingsService, CustomerBookingsService>();
builder.Services.AddScoped<ICustomerProfileService, CustomerProfileService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IFindServicesService, FindServicesService>();
builder.Services.AddScoped<IMessagingService, MessagingService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
// database v2.3: the reset code is calculated, not stored (no EMAIL_OTPS table). Secret: user-secrets "PasswordReset:CodeSecret"
builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection(OtpOptions.SectionName));
builder.Services.AddSingleton<IOtpService, OtpService>();
// database v2.3: notifications are worked out by dbo.usp_GetNotifications (no NOTIFICATIONS table)
builder.Services.AddScoped<INotificationSource, StoredProcedureNotificationSource>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IProviderApprovalService, ProviderApprovalService>();
builder.Services.AddScoped<IProviderAvailabilityService, ProviderAvailabilityService>();
// January 1 (Philippine time): last year's unavailable dates are deleted (runs at start-up and then every hour)
builder.Services.AddHostedService<UnavailableDatesResetService>();
builder.Services.AddScoped<IProviderBookingsService, ProviderBookingsService>();
builder.Services.AddScoped<IProviderOfferingsService, ProviderOfferingsService>();
builder.Services.AddScoped<IProviderProfileService, ProviderProfileService>();
builder.Services.AddScoped<IReviewModerationService, ReviewModerationService>();
builder.Services.AddScoped<IHomePageService, HomePageService>();
builder.Services.AddScoped<ICustomerDashboardService, CustomerDashboardService>();
builder.Services.AddScoped<IProviderDashboardService, ProviderDashboardService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IProviderManagementService, ProviderManagementService>();
builder.Services.AddScoped<IBookingMonitorService, BookingMonitorService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;

        // /api/... is called by scripts: answer 401 (not signed in) / 403 (wrong role) instead of sending the
        // login page; every other page still goes to the login / access-denied page as before
        var toLogin = o.Events.OnRedirectToLogin;
        var toDenied = o.Events.OnRedirectToAccessDenied;
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (!Servisyo.Web.Controllers.ApiPaths.IsApi(ctx.Request)) return toLogin(ctx);
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (!Servisyo.Web.Controllers.ApiPaths.IsApi(ctx.Request)) return toDenied(ctx);
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Create the first administrator and the starter categories (only if missing).
await AdminSeeder.SeedAsync(app.Services, builder.Configuration);
await CategorySeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();   // must come BEFORE UseAuthorization
app.UseAuthorization();
// a signed-in user's pages are never kept by the browser (no-store): after logout or a change of account nothing
// from the previous account can be shown again from the cache
Servisyo.Web.Helpers.PrivatePages.UsePrivatePages(app);

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<Servisyo.Web.Hubs.BookingHub>(Servisyo.Web.Hubs.BookingHub.Path);
app.Run();