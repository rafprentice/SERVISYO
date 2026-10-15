# Graph Report - Servisyo  (2026-10-15)

## Corpus Check
- 203 files · ~124,606 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 8 file(s) not represented in the graph (top: .css 4, (none) 3, .ico 1)

## Summary
- 2608 nodes · 7724 edges · 168 communities (98 shown, 70 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 433 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `cdb43094`
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
- CancellationToken
- BookingMonitorServiceTests
- Task
- InlineData
- HttpGet
- CancellationToken
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
- ProviderCategory
- DateOnly
- IReadOnlyList
- AccountAction
- BookProviderForm
- .GetAsync
- .Create
- http
- ProviderOfferingsServiceTests
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
- ProviderBookingCardViewModel
- .GetAsync
- TimeProvider
- BookProviderPageViewModel
- ValidateAntiForgeryToken
- Row
- .GetAsync
- Row
- .GetAsync
- BookProviderService
- .ForStatus
- ValidateAntiForgeryToken
- .Save
- BookingRow
- 20261014063616_InitialSchema.Designer.cs
- .GetBookingTitlesAsync
- SlotCheck
- Message
- BookingRow
- Row
- JobRow
- CustomerBookingTabViewModel
- ServiceCardViewModel
- AvailabilityPageViewModel
- Provider "My Services" - notes
- DateOnly
- ProviderProfileForm
- CustomerBookingTabViewModel
- RecommendedProviderViewModel
- .OnModelCreating
- BookingActionOutcome
- .AddProvider
- ProviderUnavailableDate
- BookingActionOutcome
- IActionResult
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
- BookServiceOptionViewModel
- DashboardController
- .Cancel
- Book this Provider — notes
- .Create
- NameFormat
- .Calculate
- Customer dashboard: current-booking card fix
- ReviewItemViewModel
- ScheduleItemViewModel
- BookProvider/Index.cshtml
- AttentionItemViewModel
- Customer.cshtml
- UnitTest1.cs

## God Nodes (most connected - your core abstractions)
1. `ServisyoDbContext` - 120 edges
2. `Servisyo.Web.ViewModels` - 103 edges
3. `Servisyo.Web.Models` - 77 edges
4. `Provider` - 76 edges
5. `Servisyo.Web.Services` - 67 edges
6. `User` - 64 edges
7. `Customer` - 49 edges
8. `FindServicesServiceTests` - 48 edges
9. `Booking` - 45 edges
10. `Servisyo.Web.Data` - 45 edges

## Surprising Connections (you probably didn't know these)
- `Stack` --references--> `ServisyoDbContext`  [INFERRED]
  CLAUDE.md → src/Servisyo.Web/Data/ServisyoDbContext.cs
- `Rules and where each is enforced` --references--> `CustomerBookingsService`  [INFERRED]
  docs/customer-bookings-notes.md → src/Servisyo.Web/Services/CustomerBookingsService.cs
- `Conventions (match existing code)` --references--> `AdminAction`  [INFERRED]
  CLAUDE.md → src/Servisyo.Web/Models/Entities.cs
- `Customer > My Profile - notes` --references--> `ProfileRules`  [INFERRED]
  docs/customer-profile-notes.md → src/Servisyo.Web/ViewModels/ProviderProfileViewModels.cs
- `Conventions (match existing code)` --references--> `Notification`  [INFERRED]
  CLAUDE.md → src/Servisyo.Web/Models/Entities.cs

## Import Cycles
- None detected.

## Communities (168 total, 70 thin omitted)

### Community 0 - "Servisyo.Web.ViewModels"
Cohesion: 0.04
Nodes (30): AvailabilityPageViewModel, Servisyo.Web.Helpers, Servisyo.Web.ViewModels, CustomerProfilePageViewModel, DashboardStateInfo, FindServicesPageViewModel, MessagesPageViewModel, ProviderBookingsPageViewModel (+22 more)

### Community 2 - "ProviderProfilePageViewModel"
Cohesion: 0.12
Nodes (16): ProviderProfilePageViewModel, BusinessName, City, FieldErrors, FlashIsError, FlashMessage, Form, FormError (+8 more)

### Community 3 - "ReviewModerationServiceTests"
Cohesion: 0.08
Nodes (32): IProviderOfferingsService, ProviderOfferingsService, Who, AddServiceForm, CategoryId, Description, DurationMinutes, Price (+24 more)

### Community 4 - "UsersPageViewModel"
Cohesion: 0.11
Nodes (17): AdminInfo, IUserManagementService, UserManagementService, UsersPageViewModel, CountActive, CountAll, CountDeactivated, CountSuspended (+9 more)

### Community 5 - "AuthResult"
Cohesion: 0.07
Nodes (44): IReviewModerationService, ReviewModerationService, ReviewOutcome, Invalid, NoChange, NotAllowed, NotFound, Success (+36 more)

### Community 6 - "ProviderApprovalServiceTests"
Cohesion: 0.06
Nodes (36): AuthResult, Error, Succeeded, User, AuthService, IAuthService, CustomerRegistrationViewModel, AcceptTerms (+28 more)

### Community 7 - "CancellationToken"
Cohesion: 0.06
Nodes (39): AdminProvidersController, IProviderManagementService, ProviderManagementService, ProviderBadge, ProviderCardViewModel, ApprovalStatus, Badges, BusinessName (+31 more)

### Community 8 - "BookingMonitorServiceTests"
Cohesion: 0.06
Nodes (39): Plural, AdminDashboardService, IAdminDashboardService, AdminAttention, AdminAttentionAction, ApplicationList, ModerateReviews, None (+31 more)

### Community 9 - "Task"
Cohesion: 0.08
Nodes (13): FindServicesQuery, Category, Page, Q, Sort, AuthServiceTests, FindServicesServiceTests, FixedClock (+5 more)

### Community 10 - "InlineData"
Cohesion: 0.08
Nodes (32): AdminBookingsController, BookingMonitorService, IBookingMonitorService, AdminBookingDisplay, BookingFilters, BookingListQuery, Page, Q (+24 more)

### Community 11 - "HttpGet"
Cohesion: 0.25
Nodes (5): AccountController, LoginViewModel, Email, Password, RememberMe

### Community 12 - "CancellationToken"
Cohesion: 0.12
Nodes (9): Rules (and where each is enforced), BookingLink, Me, MessagingService, NowUtc, ChatFormat, ChatStatus, MessageRules (+1 more)

### Community 13 - "CustomerBookingsServiceTests"
Cohesion: 0.05
Nodes (37): ApplicationLookup, ApprovalOutcome, Invalid, NotAllowed, NotFound, NotPending, Success, ApprovalResult (+29 more)

### Community 15 - "FindProviderCardViewModel"
Cohesion: 0.07
Nodes (14): MustBeTrueAttribute, ProviderBookingTab, ReviewFilters, AdminBookingDisplayTests, PluralTests, BookingFiltersTests, CategoryRowViewModelTests, PasswordRulesTests (+6 more)

### Community 16 - "ConversationViewModel"
Cohesion: 0.05
Nodes (44): IMessagingService, ChatMessageViewModel, Id, Mine, Text, TimeText, ConversationViewModel, CanSend (+36 more)

### Community 17 - "IActionResult"
Cohesion: 0.08
Nodes (28): HomePageService, IHomePageService, CategoryCardViewModel, CategoryId, Description, Name, ProviderCount, FeaturedProviderViewModel (+20 more)

### Community 18 - "ProviderAvailabilityServiceTests"
Cohesion: 0.06
Nodes (37): ProviderAvailabilityController, CleanDate, CleanDay, IProviderAvailabilityService, ProviderAvailabilityService, Who, AvailabilityDays, AvailabilityForm (+29 more)

### Community 19 - "ProviderAvailabilityServiceTests"
Cohesion: 0.05
Nodes (37): FindProviderCardViewModel, AvailabilityText, AvailabilityTone, AvgRating, BusinessName, CategoryName, City, Description (+29 more)

### Community 20 - ".AddCustomerAsync"
Cohesion: 0.06
Nodes (33): CustomerAddressForm, Address, CustomerPasswordForm, ConfirmPassword, CurrentPassword, NewPassword, CustomerProfileForm, City (+25 more)

### Community 21 - "Servisyo.Web.Models"
Cohesion: 0.16
Nodes (4): Servisyo.Tests, Servisyo.Web.Data, Servisyo.Web.Models, Servisyo.Web.Services

### Community 22 - "CustomerProfilePageViewModel"
Cohesion: 0.12
Nodes (16): ProviderDashboardViewModel, Attention, AvailabilityText, AvgRating, BusinessName, City, InProgress, NewRequests (+8 more)

### Community 23 - "CategoryManagementServiceTests"
Cohesion: 0.07
Nodes (19): AdminSeeder, CategorySeeder, ServisyoDbContext, AdminActions, BookingReports, Bookings, BookingServices, Customers (+11 more)

### Community 25 - "Customer"
Cohesion: 0.08
Nodes (24): 10. Implementation workflow, 11. Build and testing requirements, 12. Visual quality assurance, 13. First response and definition of done, 1. Your role, 2. Project source of truth, 3. Required technology stack, 4. Visual design direction (+16 more)

### Community 26 - "Customer"
Cohesion: 0.09
Nodes (23): Customer, Address, Bookings, City, CustomerId, ProfilePhoto, User, UserId (+15 more)

### Community 27 - "BookProviderServiceTests"
Cohesion: 0.06
Nodes (37): CustomerDashboardService, ICustomerDashboardService, CurrentBookingViewModel, BookingId, Date, Fee, ProviderName, ProviderUserId (+29 more)

### Community 28 - "ServiceCategory"
Cohesion: 0.30
Nodes (6): ServiceCategory, CategoryId, CategoryName, Description, IsActive, CustomerDashboardServiceTests

### Community 29 - ".GetAsync"
Cohesion: 0.39
Nodes (3): IProviderDashboardService, ProviderDashboardService, ProviderDashboardResult

### Community 30 - "User"
Cohesion: 0.10
Nodes (20): Notification, CreatedAt, IsRead, Message, NotificationId, User, UserId, User (+12 more)

### Community 31 - "Provider"
Cohesion: 0.09
Nodes (22): Provider, Address, ApprovalStatus, Availabilities, AvgRating, BaseFee, Bookings, BusinessName (+14 more)

### Community 33 - "Booking"
Cohesion: 0.21
Nodes (5): CustomerProfileService, ICustomerProfileService, ProviderProfileService, CustomerProfileSaveResult, ProfileRules

### Community 34 - "Booking"
Cohesion: 0.10
Nodes (21): Booking, BookingDate, BookingId, BookingTime, CreatedAt, Customer, CustomerId, EndTime (+13 more)

### Community 35 - "CategoryManagementService"
Cohesion: 0.27
Nodes (4): CategoryManagementService, CategoryResult, Succeeded, ICategoryManagementService

### Community 36 - "CategoryManagementService"
Cohesion: 0.11
Nodes (19): BookingService, Booking, BookingId, BookingServiceId, DurationMinutesSnapshot, PriceSnapshot, Service, ServiceId (+11 more)

### Community 37 - "CategoriesPageViewModel"
Cohesion: 0.11
Nodes (19): CategoriesPageViewModel, ActiveCount, AddFormError, DisabledCount, FlashIsError, FlashMessage, NewCategory, Rows (+11 more)

### Community 38 - "Service"
Cohesion: 0.20
Nodes (9): Commands, Git, graphify, Layout, Role, Second brain (where knowledge lives), Servisyo — Claude working guide, Stack (+1 more)

### Community 39 - "ProviderCategory"
Cohesion: 0.33
Nodes (6): ProviderCategory, Category, CategoryId, Provider, ProviderCategoryId, ProviderId

### Community 40 - "DateOnly"
Cohesion: 0.24
Nodes (17): addMessage(), applySearch(), autosize(), clearError(), field(), handlePoll(), markRead(), nearBottom() (+9 more)

### Community 42 - "AccountAction"
Cohesion: 0.13
Nodes (15): Conventions (match existing code), AccountAction, Deactivate, Reactivate, Restore, Suspend, AccountTransitions, ChangeStatusOutcome (+7 more)

### Community 43 - "BookProviderForm"
Cohesion: 0.10
Nodes (20): BookingFormRules, BookOutcome, Created, Invalid, NotAllowed, ProviderUnavailable, BookPageOutcome, NotAllowed (+12 more)

### Community 44 - ".GetAsync"
Cohesion: 0.17
Nodes (4): BookingQueryExtensions, Icons, AvailabilityHint, AvailabilityHintTests

### Community 45 - ".Create"
Cohesion: 0.23
Nodes (8): AdminCategoriesController, CategoryForm, CategoryId, Description, Name, CategoryToggleForm, CategoryId, Enable

### Community 46 - "http"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 48 - "ProviderDashboardViewModel"
Cohesion: 0.21
Nodes (10): IProviderBookingsService, ProviderBookingsService, Who, BookingAction, Accept, Complete, Reject, Start (+2 more)

### Community 51 - "ProviderDashboardAccess"
Cohesion: 0.14
Nodes (12): coverlet.collector (6.0.4), Microsoft.EntityFrameworkCore.Design (10.0.13), Microsoft.EntityFrameworkCore.InMemory (10.0.13), Microsoft.EntityFrameworkCore.SqlServer (10.0.13), Microsoft.NET.Test.Sdk (17.14.1), System.Security.Cryptography.Pkcs (10.0.13), xunit (2.9.3), xunit.runner.visualstudio (3.1.4) (+4 more)

### Community 52 - "CustomerBookingCardViewModel"
Cohesion: 0.23
Nodes (8): AdminApplicationsController, ApplicationDecisionForm, Decision, Reason, ReturnPage, ReturnStatus, ReturnTo, ApplicationReturn

### Community 53 - "DashboardState"
Cohesion: 0.13
Nodes (15): CustomerBookingCardViewModel, BookingId, CanCancel, CanReview, CategoryText, FromLine, IsReviewed, Location (+7 more)

### Community 54 - "DashboardState"
Cohesion: 0.17
Nodes (10): DashboardState, BookingConfirmed, NoBooking, QuotationReceived, ServiceCompleted, ServiceInProgress, WaitingForQuotation, DashboardStateInfo (+2 more)

### Community 55 - "Servisyo.Tests"
Cohesion: 0.20
Nodes (10): ProviderBookingsPageViewModel, ActiveTab, Cards, FlashIsError, FlashMessage, Page, PageCount, Shell (+2 more)

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
Cohesion: 0.26
Nodes (7): AdminReviewsController, ReviewModerationForm, Action, Page, Reason, ReviewId, Status

### Community 63 - "ProviderBookingCardViewModel"
Cohesion: 0.17
Nodes (9): AdminShell, PortalShellViewModel, FirstName, FullName, Initials, Role, Subtitle, UnreadMessages (+1 more)

### Community 64 - ".GetAsync"
Cohesion: 0.05
Nodes (35): Customer > My Profile - notes, Deliberately NOT built (and why), Install, Known limits, Rules (and where each is enforced), What is on the page, Choices I made where the design is silent — please check these, Customer > Find Services — implementation notes (+27 more)

### Community 65 - "TimeProvider"
Cohesion: 0.27
Nodes (3): Business rules and where each is enforced, BookingActionInfo, ProviderBookingActionInfoTests

### Community 66 - "BookProviderPageViewModel"
Cohesion: 0.11
Nodes (16): BookProviderPageViewModel, AvgRating, BusinessName, City, EarliestDate, FieldErrors, Form, FormError (+8 more)

### Community 68 - "Row"
Cohesion: 0.18
Nodes (11): Row, BookingDate, BookingId, BookingTime, CreatedAt, HasReview, ProviderFirstName, ProviderLastName (+3 more)

### Community 69 - ".GetAsync"
Cohesion: 0.18
Nodes (11): CustomerBookingsPageViewModel, ActiveTab, Cards, FlashIsError, FlashMessage, HasAnyBookings, Page, PageCount (+3 more)

### Community 70 - "Row"
Cohesion: 0.20
Nodes (9): Choices I made where the design is silent — please check these, Customer > My Bookings — implementation notes, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), Rules and where each is enforced, Try it with sample data (+1 more)

### Community 71 - ".GetAsync"
Cohesion: 0.15
Nodes (6): FindServicesController, HomeController, ErrorViewModel, RequestId, ShowRequestId, IFindServicesService

### Community 72 - "BookProviderService"
Cohesion: 0.18
Nodes (7): BookProviderService, DayData, IBookProviderService, ProviderInfo, ServiceRow, Who, BookProviderPageResult

### Community 74 - ".ForStatus"
Cohesion: 0.22
Nodes (9): Message, IsRead, MessageId, MessageText, Receiver, ReceiverId, Sender, SenderId (+1 more)

### Community 75 - "ValidateAntiForgeryToken"
Cohesion: 0.36
Nodes (5): CustomerBookingsService, ICustomerBookingsService, Who, CustomerCancelResult, Succeeded

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

### Community 85 - "CustomerBookingTabViewModel"
Cohesion: 0.25
Nodes (5): CustomerBookingTabViewModel, Count, Key, Label, CustomerBookingsViewModelTests

### Community 86 - "ServiceCardViewModel"
Cohesion: 0.14
Nodes (13): ServiceCardViewModel, CategoryName, Description, DurationMinutes, IsActive, MetaText, Name, Price (+5 more)

### Community 87 - "AvailabilityPageViewModel"
Cohesion: 0.29
Nodes (6): Limits (on purpose), Program.cs - one new line next to the other services, Provider application approval - notes, Try it by hand, What it does, Where the code is

### Community 88 - "Provider "My Services" - notes"
Cohesion: 0.29
Nodes (6): Left out on purpose, Program.cs - one new line next to the other services, Provider "My Services" - notes, Try it by hand, What it does, Where the code is

### Community 89 - "DateOnly"
Cohesion: 0.25
Nodes (4): BookingSlotRules, Busy, Window, BookingSlotRulesTests

### Community 90 - "ProviderProfileForm"
Cohesion: 0.17
Nodes (12): ProviderProfileController, IProviderProfileService, ProviderProfileForm, Address, BusinessName, City, Description, Email (+4 more)

### Community 91 - "CustomerBookingTabViewModel"
Cohesion: 0.29
Nodes (7): CategoryOutcome, Duplicate, Invalid, NoChange, NotAllowed, NotFound, Success

### Community 93 - ".OnModelCreating"
Cohesion: 0.10
Nodes (16): ServisyoDbContextModelSnapshot, AdminAction, ActionId, ActionType, Admin, AdminUserId, CreatedAt, Reason (+8 more)

### Community 94 - "BookingActionOutcome"
Cohesion: 0.33
Nodes (6): BookingActionOutcome, Changed, InvalidTransition, NotFound, Overlap, Success

### Community 96 - "ProviderUnavailableDate"
Cohesion: 0.47
Nodes (3): addBreak(), days(), renumber()

### Community 97 - "BookingActionOutcome"
Cohesion: 0.40
Nodes (5): CustomerCancelOutcome, Changed, NotFound, NotPending, Success

### Community 99 - "UserRowViewModel"
Cohesion: 0.18
Nodes (10): UserActionInfo, UserRowViewModel, Actions, City, Email, Initials, Name, Status (+2 more)

### Community 100 - "Provider > My Profile — implementation notes"
Cohesion: 0.20
Nodes (8): Choices I made where the design/PDF is silent — please check these, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), Provider > My Profile — implementation notes, Rules and where each is enforced, What it does

### Community 101 - "BookingActionForm"
Cohesion: 0.50
Nodes (4): BookingActionForm, Action, Page, Tab

### Community 102 - "ProviderJobViewModel"
Cohesion: 0.15
Nodes (11): ProviderBookingDisplay, ProviderJobViewModel, BookingId, CustomerName, Date, Display, Location, Status (+3 more)

### Community 152 - ".GetAsync"
Cohesion: 0.27
Nodes (6): ProviderBookingBadge, ProviderBookingsResult, ProviderBookingTabViewModel, Count, Key, Label

### Community 153 - "book-provider.js"
Cohesion: 0.36
Nodes (9): clearSlots(), currentTime(), drawSlots(), durationText(), loadSlots(), peso(), scheduleLoad(), selectedIds() (+1 more)

### Community 154 - "BookServiceOptionViewModel"
Cohesion: 0.22
Nodes (9): BookServiceOptionViewModel, CategoryName, Description, DurationMinutes, DurationText, Name, Price, PriceText (+1 more)

### Community 156 - ".Cancel"
Cohesion: 0.39
Nodes (4): CustomerBookingsController, CancelBookingForm, Page, Tab

### Community 157 - "Book this Provider — notes"
Cohesion: 0.29
Nodes (6): Book this Provider — notes, Deliberate differences from the PDF, Install, Not done, Rules enforced on the server (inside one Serializable transaction), What it does

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

### Community 165 - "AttentionItemViewModel"
Cohesion: 0.50
Nodes (4): AttentionItemViewModel, ActionLabel, Text, Tone

### Community 166 - "Customer.cshtml"
Cohesion: 0.50
Nodes (3): CustomerDashboardViewModel, Servisyo.Web.Helpers, System.Globalization

## Knowledge Gaps
- **896 isolated node(s):** `Ok`, `NoticeTooShort`, `TooFar`, `DayOff`, `NotWorking` (+891 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1096 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **70 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Work-memory lessons

**Preferred sources** — corroborated by past sessions; start here.
- `CLAUDE.md` (3× useful, score=2.999999069)
- `graphify` (2× useful, score=1.99999926)

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Servisyo.Web.ViewModels` connect `Servisyo.Web.ViewModels` to `Servisyo.Web.Controllers`, `ReviewModerationServiceTests`, `BookProvider/Index.cshtml`, `Customer.cshtml`, `system_globalization`, `InlineData`, `BookProviderForm`, `ConversationViewModel`, `IActionResult`, `ProviderAvailabilityServiceTests`, `.AddCustomerAsync`, `Servisyo.Web.Models`, `.GetAsync`, `ProviderBookingCardViewModel`?**
  _High betweenness centrality (0.108) - this node is a cross-community bridge._
- **What connects `Ok`, `NoticeTooShort`, `TooFar` to the rest of the system?**
  _896 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Servisyo.Web.ViewModels` be split into smaller, more focused modules?**
  _Cohesion score 0.041826923076923074 - nodes in this community are weakly interconnected._
- **Why does `ServisyoDbContext` connect `CategoryManagementServiceTests` to `MessagingServiceTests`, `ReviewModerationServiceTests`, `UsersPageViewModel`, `AuthResult`, `ProviderApprovalServiceTests`, `CancellationToken`, `BookingMonitorServiceTests`, `Task`, `InlineData`, `CancellationToken`, `CustomerBookingsServiceTests`, `.AddCustomerAsync`, `IActionResult`, `ProviderAvailabilityServiceTests`, `.AddCustomerAsync`, `Servisyo.Web.Models`, `CustomerProfileService`, `Customer`, `ServiceCategory`, `.GetAsync`, `User`, `Provider`, `Booking`, `Booking`, `CategoryManagementService`, `CategoryManagementService`, `Service`, `ProviderCategory`, `.GetAsync`, `ProviderOfferingsServiceTests`, `ProviderDashboardViewModel`, `ProviderDashboardServiceTests`, `CategoriesPageViewModel`, `.Decide`, `UserManagementServiceTests`, `ProviderBookingCardViewModel`, `.GetAsync`, `.ForStatus`, `ValidateAntiForgeryToken`, `JobRow`, `RecommendedProviderViewModel`, `.OnModelCreating`?**
  _High betweenness centrality (0.061) - this node is a cross-community bridge._
- **Should `ProviderProfilePageViewModel` be split into smaller, more focused modules?**
  _Cohesion score 0.125 - nodes in this community are weakly interconnected._
- **Why does `Servisyo.Web.Models` connect `Servisyo.Web.Models` to `Servisyo.Web.Controllers`, `Servisyo.Web.ViewModels`, `.GetAsync`, `system_globalization`, `InlineData`, `ConversationViewModel`, `ProviderAvailabilityServiceTests`, `Row`, `.GetAsync`, `Customer`?**
  _High betweenness centrality (0.043) - this node is a cross-community bridge._
- **Should `ReviewModerationServiceTests` be split into smaller, more focused modules?**
  _Cohesion score 0.07682926829268293 - nodes in this community are weakly interconnected._