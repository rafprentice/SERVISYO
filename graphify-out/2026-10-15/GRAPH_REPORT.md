# Graph Report - Servisyo  (2026-10-15)

## Corpus Check
- 215 files · ~132,399 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 8 file(s) not represented in the graph (top: .css 4, (none) 3, .ico 1)

## Summary
- 2785 nodes · 8244 edges · 177 communities (112 shown, 65 thin omitted)
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 451 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `0a80b525`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Servisyo.Web.ViewModels
- MessagingServiceTests
- ProviderProfilePageViewModel
- ReviewModerationServiceTests
- UsersPageViewModel
- AuthResult
- ProviderApprovalServiceTests
- ProviderManagementServiceTests
- BookingMonitorServiceTests
- Task
- InlineData
- IActionResult
- DateTime
- CustomerBookingsServiceTests
- .AddCustomerAsync
- FindProviderCardViewModel
- ConversationViewModel
- IActionResult
- ProviderAvailabilityServiceTests
- ProviderAvailabilityServiceTests
- .AddCustomerAsync
- Servisyo.Web.Models
- CustomerProfilePageViewModel
- CategoryManagementServiceTests
- CustomerProfileService
- Customer
- Customer
- BookProviderServiceTests
- ServiceCategory
- .GetAsync
- User
- Provider
- Servisyo.Web.Controllers
- Booking
- Booking
- CategoryManagementService
- CategoryManagementService
- CategoriesPageViewModel
- Service
- Task
- DateOnly
- IReadOnlyList
- AccountAction
- DateOnly
- .GetAsync
- .Create
- http
- Servisyo.Web.Helpers
- ProviderDashboardViewModel
- .DateTimeText
- ProviderDashboardServiceTests
- ProviderDashboardAccess
- CustomerBookingCardViewModel
- DashboardState
- DashboardState
- Servisyo.Tests
- CategoriesPageViewModel
- ProviderBookingsPageViewModel
- AdminDashboardServiceTests
- .Decide
- .ChangeStatus
- UserManagementServiceTests
- .Moderate
- CancellationToken
- Customer > Find Services — implementation notes
- TimeProvider
- BookProviderPageViewModel
- ValidateAntiForgeryToken
- Row
- .GetAsync
- .GetAsync
- .GetAsync
- BookProviderService
- .ForStatus
- AuthService
- .Initials
- BookingRow
- 20261014063616_InitialSchema.Designer.cs
- .GetBookingTitlesAsync
- SlotCheck
- Message
- BookingRow
- Row
- JobRow
- CustomerProfilePageViewModel
- ProviderDetailsViewModel
- AvailabilityPageViewModel
- Provider "My Services" - notes
- DateOnly
- ProviderProfileForm
- AvailabilityForm
- TimeProvider
- AdminAction
- BookingActionOutcome
- .AddProvider
- ProviderUnavailableDate
- BookingActionOutcome
- ProviderCardViewModel
- UserRowViewModel
- Provider > My Profile — implementation notes
- BookingActionForm
- ProviderJobViewModel
- customer-profile.js
- Home/Index.cshtml
- provider-services.js
- Servisyo.Web.ViewModels.CustomerRegistrationViewModel
- Servisyo.Web.ViewModels.LoginViewModel
- Servisyo.Web.ViewModels.RegisterBaseViewModel
- Servisyo.Web.ViewModels.RegisterProviderViewModel
- _CategoryIcon.cshtml
- _PasswordToggle.cshtml
- .GetAsync
- book-provider.js
- FindServicesPageViewModel
- ReviewCardViewModel
- .Cancel
- Book this Provider — notes
- .GetProvidersAsync
- NameFormat
- AvailabilityPageViewModel
- Customer dashboard: current-booking card fix
- ReviewItemViewModel
- ScheduleItemViewModel
- BookProvider/Index.cshtml
- AdminAttentionItem
- ProviderDetailsViewModels.cs
- ProvidersPageViewModel
- Notification bell (customer, provider, administrator)
- Row
- notifications.js
- CurrentBookingViewModel
- BreakForm
- Customer > My Profile - notes
- ProviderUnavailableDate
- LoginViewModel
- CustomerPasswordForm

## God Nodes (most connected - your core abstractions)
1. `ServisyoDbContext` - 118 edges
2. `Servisyo.Web.ViewModels` - 112 edges
3. `Provider` - 74 edges
4. `Servisyo.Web.Models` - 74 edges
5. `Servisyo.Web.Services` - 71 edges
6. `User` - 58 edges
7. `Customer` - 48 edges
8. `FindServicesServiceTests` - 48 edges
9. `ServiceCategory` - 45 edges
10. `Booking` - 45 edges

## Surprising Connections (you probably didn't know these)
- `Stack` --references--> `ServisyoDbContext`  [INFERRED]
  CLAUDE.md → src/Servisyo.Web/Data/ServisyoDbContext.cs
- `Limits / choices` --references--> `NotificationKinds`  [INFERRED]
  docs/notification-bell-notes.md → src/Servisyo.Web/ViewModels/NotificationViewModels.cs
- `Rules (enforced on the server)` --references--> `AccountTransitions`  [INFERRED]
  docs/provider-details-notes.md → src/Servisyo.Web/Services/AccountTransitions.cs
- `Conventions (match existing code)` --references--> `ProviderDashboardAccess`  [INFERRED]
  CLAUDE.md → src/Servisyo.Web/ViewModels/ProviderDashboardViewModels.cs
- `Conventions (match existing code)` --references--> `Notification`  [INFERRED]
  CLAUDE.md → src/Servisyo.Web/Models/Entities.cs

## Import Cycles
- None detected.

## Communities (177 total, 65 thin omitted)

### Community 0 - "Servisyo.Web.ViewModels"
Cohesion: 0.05
Nodes (22): Servisyo.Web.ViewModels, DashboardStateInfo, FindServicesPageViewModel, ServicesPageViewModel, ProviderApplicationViewModel, ProviderApplicationViewModel, CategoriesPageViewModel, ProviderDetailsViewModel (+14 more)

### Community 2 - "ProviderProfilePageViewModel"
Cohesion: 0.12
Nodes (16): ProviderProfilePageViewModel, BusinessName, City, FieldErrors, FlashIsError, FlashMessage, Form, FormError (+8 more)

### Community 3 - "ReviewModerationServiceTests"
Cohesion: 0.05
Nodes (46): IProviderOfferingsService, ProviderOfferingsService, Who, AddServiceForm, CategoryId, Description, DurationMinutes, Price (+38 more)

### Community 4 - "UsersPageViewModel"
Cohesion: 0.11
Nodes (17): AdminInfo, IUserManagementService, UserManagementService, UsersPageViewModel, CountActive, CountAll, CountDeactivated, CountSuspended (+9 more)

### Community 5 - "AuthResult"
Cohesion: 0.20
Nodes (9): ReviewAction, Hide, Keep, Restore, ReviewActionInfo, ReviewListQuery, Page, Status (+1 more)

### Community 6 - "ProviderApprovalServiceTests"
Cohesion: 0.07
Nodes (30): CustomerRegistrationViewModel, AcceptTerms, Barangay, City, ConfirmPassword, Email, FullName, Password (+22 more)

### Community 7 - "ProviderManagementServiceTests"
Cohesion: 0.33
Nodes (4): ProviderListQuery, Page, Status, ProviderManagementServiceTests

### Community 8 - "BookingMonitorServiceTests"
Cohesion: 0.05
Nodes (47): AdminDashboardService, IAdminDashboardService, AdminBookingRowViewModel, BookingId, CustomerName, Display, ProviderName, Status (+39 more)

### Community 9 - "Task"
Cohesion: 0.06
Nodes (15): Plural, AdminAttention, ApplicantNumber, ProfileStrength, AdminAttentionTests, MessagingViewModelTests, AdminAttentionLinkTests, ApplicantNumberTests (+7 more)

### Community 10 - "InlineData"
Cohesion: 0.08
Nodes (31): AdminBookingsController, BookingMonitorService, IBookingMonitorService, AdminBookingDisplay, BookingFilters, BookingListQuery, Page, Q (+23 more)

### Community 11 - "IActionResult"
Cohesion: 0.14
Nodes (5): AccountController, DashboardController, ProviderAvailabilityController, ProviderBookingsController, ProviderServicesController

### Community 12 - "DateTime"
Cohesion: 0.08
Nodes (30): Rules (and where each is enforced), BookingLink, IMessagingService, Me, MessagingService, NowUtc, ChatFormat, ChatMessageViewModel (+22 more)

### Community 13 - "CustomerBookingsServiceTests"
Cohesion: 0.06
Nodes (35): ApplicationLookup, ApprovalOutcome, Invalid, NotAllowed, NotFound, NotPending, Success, ApprovalResult (+27 more)

### Community 15 - "FindProviderCardViewModel"
Cohesion: 0.07
Nodes (15): MustBeTrueAttribute, ProviderStatuses, AdminBookingDisplayTests, PluralTests, BookingFiltersTests, PaymentDisplayTests, CategoryRowViewModelTests, PasswordRulesTests (+7 more)

### Community 16 - "ConversationViewModel"
Cohesion: 0.09
Nodes (22): ConversationViewModel, CanSend, ContextText, Initials, Name, PartnerUserId, Preview, SearchText (+14 more)

### Community 17 - "IActionResult"
Cohesion: 0.08
Nodes (28): HomePageService, IHomePageService, CategoryCardViewModel, CategoryId, Description, Name, ProviderCount, FeaturedProviderViewModel (+20 more)

### Community 19 - "ProviderAvailabilityServiceTests"
Cohesion: 0.07
Nodes (24): FindProviderCardViewModel, AvailabilityText, AvailabilityTone, AvgRating, BusinessName, CategoryName, City, Description (+16 more)

### Community 20 - ".AddCustomerAsync"
Cohesion: 0.18
Nodes (3): CustomerAddressForm, Address, CustomerProfileServiceTests

### Community 21 - "Servisyo.Web.Models"
Cohesion: 0.17
Nodes (4): Servisyo.Tests, Servisyo.Web.Data, Servisyo.Web.Models, Servisyo.Web.Services

### Community 22 - "CustomerProfilePageViewModel"
Cohesion: 0.12
Nodes (16): ProviderDashboardViewModel, Attention, AvailabilityText, AvgRating, BusinessName, City, InProgress, NewRequests (+8 more)

### Community 23 - "CategoryManagementServiceTests"
Cohesion: 0.09
Nodes (18): ServisyoDbContext, AdminActions, BookingReports, Bookings, BookingServices, Customers, Messages, Notifications (+10 more)

### Community 25 - "Customer"
Cohesion: 0.08
Nodes (24): 10. Implementation workflow, 11. Build and testing requirements, 12. Visual quality assurance, 13. First response and definition of done, 1. Your role, 2. Project source of truth, 3. Required technology stack, 4. Visual design direction (+16 more)

### Community 26 - "Customer"
Cohesion: 0.06
Nodes (31): ServisyoDbContextModelSnapshot, Customer, Address, Bookings, City, CustomerId, ProfilePhoto, User (+23 more)

### Community 28 - "ServiceCategory"
Cohesion: 0.28
Nodes (7): ServiceCategory, CategoryId, CategoryName, Description, IsActive, CustomerDashboardServiceTests, FixedClock

### Community 29 - ".GetAsync"
Cohesion: 0.31
Nodes (3): IProviderDashboardService, ProviderDashboardService, ProviderDashboardResult

### Community 30 - "User"
Cohesion: 0.10
Nodes (15): AdminSeeder, CategorySeeder, User, AccountStatus, CreatedAt, Customer, Email, FirstName (+7 more)

### Community 31 - "Provider"
Cohesion: 0.07
Nodes (28): Provider, Address, ApprovalStatus, Availabilities, AvgRating, BaseFee, Bookings, BusinessName (+20 more)

### Community 33 - "Booking"
Cohesion: 0.18
Nodes (5): CustomerProfileService, ICustomerProfileService, ProviderProfileService, CustomerProfileSaveResult, ProfileRules

### Community 34 - "Booking"
Cohesion: 0.10
Nodes (21): Booking, BookingDate, BookingId, BookingTime, CreatedAt, Customer, CustomerId, EndTime (+13 more)

### Community 35 - "CategoryManagementService"
Cohesion: 0.17
Nodes (11): CategoryManagementService, CategoryOutcome, Duplicate, Invalid, NoChange, NotAllowed, NotFound, Success (+3 more)

### Community 36 - "CategoryManagementService"
Cohesion: 0.11
Nodes (19): BookingService, Booking, BookingId, BookingServiceId, DurationMinutesSnapshot, PriceSnapshot, Service, ServiceId (+11 more)

### Community 37 - "CategoriesPageViewModel"
Cohesion: 0.11
Nodes (19): CategoriesPageViewModel, ActiveCount, AddFormError, DisabledCount, FlashIsError, FlashMessage, NewCategory, Rows (+11 more)

### Community 38 - "Service"
Cohesion: 0.11
Nodes (17): Commands, Conventions (match existing code), Git, graphify, Layout, Role, Second brain (where knowledge lives), Servisyo — Claude working guide (+9 more)

### Community 39 - "Task"
Cohesion: 0.10
Nodes (12): FindServicesService, FindServicesQuery, Category, Page, Q, Sort, FindServicesResult, AuthServiceTests (+4 more)

### Community 40 - "DateOnly"
Cohesion: 0.24
Nodes (17): addMessage(), applySearch(), autosize(), clearError(), field(), handlePoll(), markRead(), nearBottom() (+9 more)

### Community 42 - "AccountAction"
Cohesion: 0.15
Nodes (13): AccountAction, Deactivate, Reactivate, Restore, Suspend, ChangeStatusOutcome, InvalidTransition, NotAllowed (+5 more)

### Community 43 - "DateOnly"
Cohesion: 0.21
Nodes (8): BookProviderController, IBookProviderService, BookProviderForm, Date, Description, Location, ServiceIds, Time

### Community 44 - ".GetAsync"
Cohesion: 0.17
Nodes (4): BookingQueryExtensions, Icons, AvailabilityHint, AvailabilityHintTests

### Community 45 - ".Create"
Cohesion: 0.23
Nodes (8): AdminCategoriesController, CategoryForm, CategoryId, Description, Name, CategoryToggleForm, CategoryId, Enable

### Community 46 - "http"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 47 - "Servisyo.Web.Helpers"
Cohesion: 0.06
Nodes (17): AvailabilityPageViewModel, Servisyo.Web.Helpers, CustomerProfilePageViewModel, MessagesPageViewModel, ProviderBookingsPageViewModel, ProviderProfilePageViewModel, BookingsPageViewModel, System.Globalization (+9 more)

### Community 48 - "ProviderDashboardViewModel"
Cohesion: 0.21
Nodes (10): IProviderBookingsService, ProviderBookingsService, Who, BookingAction, Accept, Complete, Reject, Start (+2 more)

### Community 51 - "ProviderDashboardAccess"
Cohesion: 0.14
Nodes (12): coverlet.collector (6.0.4), Microsoft.EntityFrameworkCore.Design (10.0.13), Microsoft.EntityFrameworkCore.InMemory (10.0.13), Microsoft.EntityFrameworkCore.SqlServer (10.0.13), Microsoft.NET.Test.Sdk (17.14.1), System.Security.Cryptography.Pkcs (10.0.13), xunit (2.9.3), xunit.runner.visualstudio (3.1.4) (+4 more)

### Community 52 - "CustomerBookingCardViewModel"
Cohesion: 0.17
Nodes (9): AdminApplicationsController, AdminProvidersController, ApplicationDecisionForm, Decision, Reason, ReturnPage, ReturnStatus, ReturnTo (+1 more)

### Community 53 - "DashboardState"
Cohesion: 0.13
Nodes (15): CustomerBookingCardViewModel, BookingId, CanCancel, CanReview, CategoryText, FromLine, IsReviewed, Location (+7 more)

### Community 54 - "DashboardState"
Cohesion: 0.17
Nodes (10): DashboardState, BookingConfirmed, NoBooking, QuotationReceived, ServiceCompleted, ServiceInProgress, WaitingForQuotation, DashboardStateInfo (+2 more)

### Community 55 - "Servisyo.Tests"
Cohesion: 0.14
Nodes (14): ProviderBookingsPageViewModel, ActiveTab, Cards, FlashIsError, FlashMessage, Page, PageCount, Shell (+6 more)

### Community 58 - "AdminDashboardServiceTests"
Cohesion: 0.23
Nodes (8): AdminUsersController, ChangeStatusForm, Action, Page, Q, Reason, Status, UserId

### Community 59 - ".Decide"
Cohesion: 0.15
Nodes (13): BookingReport, Booking, BookingId, CreatedAt, Reason, ReportedBy, ReportedByUserId, ReportId (+5 more)

### Community 60 - ".ChangeStatus"
Cohesion: 0.15
Nodes (13): ProviderBookingCardViewModel, Actions, BookingId, CategoryText, CustomerName, FromLine, Location, Notes (+5 more)

### Community 61 - "UserManagementServiceTests"
Cohesion: 0.31
Nodes (5): UserListQuery, Page, Q, Status, UserManagementServiceTests

### Community 62 - ".Moderate"
Cohesion: 0.10
Nodes (18): AdminReviewsController, IReviewModerationService, ReviewModerationService, ReviewOutcome, Invalid, NoChange, NotAllowed, NotFound (+10 more)

### Community 63 - "CancellationToken"
Cohesion: 0.10
Nodes (17): NotificationsController, AdminShell, INotificationService, Me, NotificationOpenResult, NotificationService, NotificationFormat, NotificationItem (+9 more)

### Community 64 - "Customer > Find Services — implementation notes"
Cohesion: 0.15
Nodes (10): Choices I made where the design is silent — please check these, Customer > Find Services — implementation notes, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), Rules and where each is enforced, What it does (+2 more)

### Community 65 - "TimeProvider"
Cohesion: 0.27
Nodes (3): Business rules and where each is enforced, BookingActionInfo, ProviderBookingActionInfoTests

### Community 66 - "BookProviderPageViewModel"
Cohesion: 0.05
Nodes (40): BookingFormRules, BookOutcome, Created, Invalid, NotAllowed, ProviderUnavailable, BookPageOutcome, NotAllowed (+32 more)

### Community 68 - "Row"
Cohesion: 0.18
Nodes (11): Row, BookingDate, BookingId, BookingTime, CreatedAt, HasReview, ProviderFirstName, ProviderLastName (+3 more)

### Community 69 - ".GetAsync"
Cohesion: 0.11
Nodes (16): CustomerBookingsPageViewModel, ActiveTab, Cards, FlashIsError, FlashMessage, HasAnyBookings, Page, PageCount (+8 more)

### Community 70 - ".GetAsync"
Cohesion: 0.11
Nodes (16): Choices I made where the design is silent — please check these, Customer > My Bookings — implementation notes, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), Rules and where each is enforced, Try it with sample data (+8 more)

### Community 71 - ".GetAsync"
Cohesion: 0.14
Nodes (7): FindServicesController, HomeController, ProviderProfileController, ErrorViewModel, RequestId, ShowRequestId, IFindServicesService

### Community 72 - "BookProviderService"
Cohesion: 0.20
Nodes (5): BookProviderService, DayData, ProviderInfo, ServiceRow, Who

### Community 74 - ".ForStatus"
Cohesion: 0.22
Nodes (9): Message, IsRead, MessageId, MessageText, Receiver, ReceiverId, Sender, SenderId (+1 more)

### Community 75 - "AuthService"
Cohesion: 0.16
Nodes (7): AuthResult, Error, Succeeded, User, AuthService, IAuthService, AdminApplicationNotificationTests

### Community 76 - ".Initials"
Cohesion: 0.09
Nodes (22): CustomerDashboardService, ICustomerDashboardService, CustomerDashboardViewModel, Current, Recent, Recommended, Shell, State (+14 more)

### Community 77 - "BookingRow"
Cohesion: 0.20
Nodes (10): BookingRow, BookingDate, BookingId, BookingTime, CreatedAt, PaymentStatus, ProviderName, ProviderUserId (+2 more)

### Community 78 - "20261014063616_InitialSchema.Designer.cs"
Cohesion: 0.22
Nodes (9): Row, BookingDate, BookingId, BookingTime, CreatedAt, CustomerName, ServiceDescription, ServiceLocation (+1 more)

### Community 79 - ".GetBookingTitlesAsync"
Cohesion: 0.22
Nodes (9): JobRow, BookingDate, BookingId, BookingTime, CreatedAt, CustomerCity, CustomerName, ServiceLocation (+1 more)

### Community 80 - "SlotCheck"
Cohesion: 0.14
Nodes (11): SlotCheck, Clash, DayFull, DayOff, InBreak, NoticeTooShort, NotWorking, Ok (+3 more)

### Community 81 - "Message"
Cohesion: 0.25
Nodes (7): Deliberately NOT built (and why), How "live" works, Install, Known limits, Messages (customer and service provider) - notes, The status pill, What it does

### Community 82 - "BookingRow"
Cohesion: 0.25
Nodes (7): Choices I made where the design/PDF is silent, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), Provider > Booking Requests — implementation notes, What it does

### Community 83 - "Row"
Cohesion: 0.25
Nodes (7): AccountStatuses, ApprovalStatuses, BookingStatuses, PaymentStatuses, ReportStatuses, ReviewStatuses, Roles

### Community 84 - "JobRow"
Cohesion: 0.25
Nodes (8): Review, Booking, BookingId, Comment, CreatedAt, Rating, ReviewId, Status

### Community 85 - "CustomerProfilePageViewModel"
Cohesion: 0.07
Nodes (26): CustomerProfileForm, City, Email, FullName, PhoneNumber, CustomerProfilePageResult, CustomerProfilePageViewModel, AddressError (+18 more)

### Community 86 - "ProviderDetailsViewModel"
Cohesion: 0.08
Nodes (24): ProviderDetailsViewModel, AccountStatus, Actions, Address, ApprovalStatus, AsksReason, Badges, BusinessName (+16 more)

### Community 87 - "AvailabilityPageViewModel"
Cohesion: 0.29
Nodes (6): Limits (on purpose), Program.cs - one new line next to the other services, Provider application approval - notes, Try it by hand, What it does, Where the code is

### Community 88 - "Provider "My Services" - notes"
Cohesion: 0.29
Nodes (6): Left out on purpose, Program.cs - one new line next to the other services, Provider "My Services" - notes, Try it by hand, What it does, Where the code is

### Community 89 - "DateOnly"
Cohesion: 0.26
Nodes (4): BookingSlotRules, Busy, Window, BookingSlotRulesTests

### Community 90 - "ProviderProfileForm"
Cohesion: 0.20
Nodes (11): IProviderProfileService, ProviderProfileForm, Address, BusinessName, City, Description, Email, FullName (+3 more)

### Community 91 - "AvailabilityForm"
Cohesion: 0.16
Nodes (12): CleanDate, CleanDay, IProviderAvailabilityService, ProviderAvailabilityService, Who, AvailabilityForm, Dates, Days (+4 more)

### Community 92 - "TimeProvider"
Cohesion: 0.13
Nodes (4): DashboardCurrentBookingTests, FixedClock, FixedClock, FixedClock

### Community 93 - "AdminAction"
Cohesion: 0.22
Nodes (9): AdminAction, ActionId, ActionType, Admin, AdminUserId, CreatedAt, Reason, TargetId (+1 more)

### Community 94 - "BookingActionOutcome"
Cohesion: 0.33
Nodes (6): BookingActionOutcome, Changed, InvalidTransition, NotFound, Overlap, Success

### Community 96 - "ProviderUnavailableDate"
Cohesion: 0.47
Nodes (3): addBreak(), days(), renumber()

### Community 97 - "BookingActionOutcome"
Cohesion: 0.40
Nodes (5): CustomerCancelOutcome, Changed, NotFound, NotPending, Success

### Community 98 - "ProviderCardViewModel"
Cohesion: 0.11
Nodes (18): ProviderBadge, ProviderCardViewModel, ApprovalStatus, Badges, BusinessName, Details, ExtraServices, HasRating (+10 more)

### Community 99 - "UserRowViewModel"
Cohesion: 0.18
Nodes (10): UserActionInfo, UserRowViewModel, Actions, City, Email, Initials, Name, Status (+2 more)

### Community 100 - "Provider > My Profile — implementation notes"
Cohesion: 0.22
Nodes (8): Choices I made where the design/PDF is silent — please check these, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), Provider > My Profile — implementation notes, Rules and where each is enforced, What it does

### Community 101 - "BookingActionForm"
Cohesion: 0.50
Nodes (4): BookingActionForm, Action, Page, Tab

### Community 102 - "ProviderJobViewModel"
Cohesion: 0.18
Nodes (10): ProviderBookingDisplay, ProviderJobViewModel, BookingId, CustomerName, Date, Display, Location, Status (+2 more)

### Community 152 - ".GetAsync"
Cohesion: 0.33
Nodes (3): ProviderBookingBadge, ProviderBookingsResult, ProviderBookingTab

### Community 153 - "book-provider.js"
Cohesion: 0.36
Nodes (9): clearSlots(), currentTime(), drawSlots(), durationText(), loadSlots(), peso(), scheduleLoad(), selectedIds() (+1 more)

### Community 154 - "FindServicesPageViewModel"
Cohesion: 0.11
Nodes (17): CategoryChipViewModel, CategoryId, Count, Name, FindServicesPageViewModel, AllCount, Cards, CategoryId (+9 more)

### Community 155 - "ReviewCardViewModel"
Cohesion: 0.13
Nodes (14): ReviewCardViewModel, Actions, Comment, CustomerName, ProviderName, Rating, RatingText, ReviewId (+6 more)

### Community 156 - ".Cancel"
Cohesion: 0.39
Nodes (4): CustomerBookingsController, CancelBookingForm, Page, Tab

### Community 157 - "Book this Provider — notes"
Cohesion: 0.29
Nodes (6): Book this Provider — notes, Deliberate differences from the PDF, Install, Not done, Rules enforced on the server (inside one Serializable transaction), What it does

### Community 160 - "AvailabilityPageViewModel"
Cohesion: 0.14
Nodes (13): AvailabilityDays, AvailabilityPageViewModel, Errors, FlashIsError, FlashMessage, Form, IsSuggested, NoticeOptions (+5 more)

### Community 161 - "Customer dashboard: current-booking card fix"
Cohesion: 0.40
Nodes (4): Buttons now real, Cause of the squashed stepper, Customer dashboard: current-booking card fix, Not changed

### Community 162 - "ReviewItemViewModel"
Cohesion: 0.40
Nodes (5): ReviewItemViewModel, Author, Comment, Date, Rating

### Community 163 - "ScheduleItemViewModel"
Cohesion: 0.40
Nodes (5): ScheduleItemViewModel, CustomerName, Status, Time, Title

### Community 164 - "BookProvider/Index.cshtml"
Cohesion: 0.50
Nodes (3): BookProviderPageViewModel, Servisyo.Web.Helpers, System.Globalization

### Community 165 - "AdminAttentionItem"
Cohesion: 0.17
Nodes (12): AdminAttentionAction, ApplicationList, ModerateReviews, None, ReviewApplication, AdminAttentionItem, Action, TargetId (+4 more)

### Community 166 - "ProviderDetailsViewModels.cs"
Cohesion: 0.17
Nodes (11): ProviderDetailsLookup, ProviderDetailsOutcome, NotAllowed, NotFound, Ok, ProviderServiceLine, ProviderStatusForm, Action (+3 more)

### Community 167 - "ProvidersPageViewModel"
Cohesion: 0.15
Nodes (13): ProvidersPageViewModel, Cards, CountAll, CountApproved, CountDeactivated, CountPending, CountRejected, CountSuspended (+5 more)

### Community 168 - "Notification bell (customer, provider, administrator)"
Cohesion: 0.18
Nodes (7): Install, Limits / choices, New notification: provider applications -> administrators, Notification bell (customer, provider, administrator), Rules, What it does, NotificationKinds

### Community 169 - "Row"
Cohesion: 0.17
Nodes (12): Row, AvgRating, BusinessName, City, Description, EffectivePrice, FirstName, HasBaseFee (+4 more)

### Community 170 - "notifications.js"
Cohesion: 0.38
Nodes (9): close(), el(), icon(), isOpen(), load(), message(), open(), render() (+1 more)

### Community 171 - "CurrentBookingViewModel"
Cohesion: 0.20
Nodes (10): CurrentBookingViewModel, BookingId, Date, Fee, ProviderName, ProviderUserId, Reference, Time (+2 more)

### Community 172 - "BreakForm"
Cohesion: 0.20
Nodes (9): BreakForm, End, Start, DayScheduleForm, Breaks, DayOfWeek, End, IsAvailable (+1 more)

### Community 173 - "Customer > My Profile - notes"
Cohesion: 0.22
Nodes (7): Customer > My Profile - notes, Deliberately NOT built (and why), Install, Known limits, Rules (and where each is enforced), What is on the page, PasswordRules

### Community 174 - "ProviderUnavailableDate"
Cohesion: 0.33
Nodes (6): ProviderUnavailableDate, Provider, ProviderId, Reason, UnavailableDate, UnavailableId

### Community 175 - "LoginViewModel"
Cohesion: 0.50
Nodes (4): LoginViewModel, Email, Password, RememberMe

### Community 176 - "CustomerPasswordForm"
Cohesion: 0.50
Nodes (4): CustomerPasswordForm, ConfirmPassword, CurrentPassword, NewPassword

## Knowledge Gaps
- **939 isolated node(s):** `Succeeded`, `Error`, `User`, `Icon`, `Shell` (+934 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1153 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **65 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Work-memory lessons

**Preferred sources** — corroborated by past sessions; start here.
- `CLAUDE.md` (3× useful, score=2.998643695)
- `graphify` (2× useful, score=1.999095677)

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Servisyo.Web.ViewModels` connect `Servisyo.Web.ViewModels` to `Servisyo.Web.Controllers`, `AvailabilityPageViewModel`, `BookProviderPageViewModel`, `ReviewModerationServiceTests`, `BookProvider/Index.cshtml`, `ProviderDetailsViewModels.cs`, `BookingMonitorServiceTests`, `system_globalization`, `InlineData`, `DateTime`, `Servisyo.Web.Helpers`, `IActionResult`, `Servisyo.Web.Models`, `DashboardState`, `ProviderProfileForm`, `CancellationToken`?**
  _High betweenness centrality (0.105) - this node is a cross-community bridge._
- **What connects `Succeeded`, `Error`, `User` to the rest of the system?**
  _939 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Servisyo.Web.ViewModels` be split into smaller, more focused modules?**
  _Cohesion score 0.04784313725490196 - nodes in this community are weakly interconnected._
- **Why does `ProviderDetailsViewModel` connect `ProviderDetailsViewModel` to `BookProviderService`, `DateOnly`, `.GetProvidersAsync`, `ProviderDetailsViewModels.cs`?**
  _High betweenness centrality (0.037) - this node is a cross-community bridge._
- **Should `ProviderProfilePageViewModel` be split into smaller, more focused modules?**
  _Cohesion score 0.125 - nodes in this community are weakly interconnected._
- **Why does `PortalShellViewModel` connect `BookingMonitorServiceTests` to `ProviderProfilePageViewModel`, `ReviewModerationServiceTests`, `UsersPageViewModel`, `Task`, `InlineData`, `DateTime`, `CustomerBookingsServiceTests`, `ConversationViewModel`, `CustomerProfilePageViewModel`, `.GetAsync`, `FindServicesPageViewModel`, `.GetAsync`, `AvailabilityPageViewModel`, `CategoriesPageViewModel`, `Task`, `ProvidersPageViewModel`, `Servisyo.Tests`, `CancellationToken`, `.GetAsync`, `.GetAsync`, `CustomerProfilePageViewModel`, `ProviderProfileForm`, `AvailabilityForm`?**
  _High betweenness centrality (0.036) - this node is a cross-community bridge._
- **Should `ReviewModerationServiceTests` be split into smaller, more focused modules?**
  _Cohesion score 0.054945054945054944 - nodes in this community are weakly interconnected._