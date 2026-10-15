# Graph Report - Servisyo  (2026-10-15)

## Corpus Check
- 216 files · ~134,132 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 8 file(s) not represented in the graph (top: .css 4, (none) 3, .ico 1)

## Summary
- 2863 nodes · 8357 edges · 192 communities (117 shown, 75 thin omitted)
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 459 edges (avg confidence: 0.86)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `1e29c4b4`
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
- MessagingService
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
- sync_prompts_to_graphify.py
- Task
- DateOnly
- IReadOnlyList
- datetime
- .Create
- .GetBookingTitlesAsync
- .Create
- http
- ProviderAvailability/Index.cshtml
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
- .Moderate
- CancellationToken
- Customer > Find Services — implementation notes
- TimeProvider
- BookProviderPageViewModel
- ValidateAntiForgeryToken
- Row
- .GetAsync
- .GetAsync
- .Save
- DateOnly
- system_globalization
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
- HttpGet
- CustomerProfilePageViewModel
- ProviderDetailsViewModel
- AvailabilityPageViewModel
- Provider "My Services" - notes
- DateOnly
- .GetAsync
- .ChangeStatus
- TimeProvider
- AdminAction
- BookingActionOutcome
- ProviderManagementService
- ProviderUnavailableDate
- Customer > My Bookings — implementation notes
- ProviderCardViewModel
- MessagesPageViewModel
- PortalShellViewModel
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
- .PollAsync
- Customer dashboard: current-booking card fix
- ReviewItemViewModel
- ScheduleItemViewModel
- BookProvider/Index.cshtml
- AdminAttentionItem
- ProviderStatusForm
- ProvidersPageViewModel
- .Act
- Row
- notifications.js
- CurrentBookingViewModel
- .Save
- CustomerProfileForm
- SendMessageResult
- LoginViewModel
- CustomerPasswordForm
- .Calculate
- Q: graphify claude install
- Q: where i can see the file .md file?
- Q: can you make me .md file for claude to maximize tokens, and you are a Senior Dev which is a full stack and current working on this project servisyo and link the .md file to graphify to that i can build my codebase so you can maximize the 2nd brain
- SYNC_PROMPTS_TO_GRAPHIFY.md
- BookOutcome
- ProviderDashboardAccess
- Q: can you make me a sync_prompts_to_graphify.py so that i can take notes and push it to my 2nd brain on graphify
- ErrorViewModel
- CategoryManagementViewModels.cs
- CustomerBookingsViewModels.cs
- ProviderDetailsOutcome
- Customer.cshtml
- ProviderProfile/Index.cshtml
- deploy.sql

## God Nodes (most connected - your core abstractions)
1. `ServisyoDbContext` - 118 edges
2. `Servisyo.Web.ViewModels` - 111 edges
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
- `3. Admin review` --references--> `AdminApplicationsController`  [INFERRED]
  graphify-out/memory/query_20261015_024747_5e1a33b3_how_does_application_approval_work_in_servisyo.md → src/Servisyo.Web/Controllers/AdminApplicationsController.cs
- `Rules and where each is enforced` --references--> `CustomerBookingsService`  [INFERRED]
  docs/customer-bookings-notes.md → src/Servisyo.Web/Services/CustomerBookingsService.cs
- `Conventions (match existing code)` --references--> `AdminAction`  [INFERRED]
  CLAUDE.md → src/Servisyo.Web/Models/Entities.cs
- `Conventions (match existing code)` --references--> `Notification`  [INFERRED]
  CLAUDE.md → src/Servisyo.Web/Models/Entities.cs

## Import Cycles
- None detected.

## Communities (192 total, 75 thin omitted)

### Community 0 - "Servisyo.Web.ViewModels"
Cohesion: 0.04
Nodes (28): Servisyo.Web.ViewModels, CustomerProfilePageViewModel, DashboardStateInfo, FindServicesPageViewModel, MessagesPageViewModel, ProviderBookingsPageViewModel, ServicesPageViewModel, ProviderApplicationViewModel (+20 more)

### Community 2 - "ProviderProfilePageViewModel"
Cohesion: 0.12
Nodes (16): ProviderProfilePageViewModel, BusinessName, City, FieldErrors, FlashIsError, FlashMessage, Form, FormError (+8 more)

### Community 3 - "ReviewModerationServiceTests"
Cohesion: 0.05
Nodes (47): IProviderOfferingsService, ProviderOfferingsService, Who, AddServiceForm, CategoryId, Description, DurationMinutes, Price (+39 more)

### Community 4 - "UsersPageViewModel"
Cohesion: 0.06
Nodes (44): AccountAction, Deactivate, Reactivate, Restore, Suspend, AdminInfo, IUserManagementService, UserManagementService (+36 more)

### Community 5 - "AuthResult"
Cohesion: 0.09
Nodes (31): IReviewModerationService, ReviewModerationService, ReviewOutcome, Invalid, NoChange, NotAllowed, NotFound, Success (+23 more)

### Community 6 - "ProviderApprovalServiceTests"
Cohesion: 0.07
Nodes (30): CustomerRegistrationViewModel, AcceptTerms, Barangay, City, ConfirmPassword, Email, FullName, Password (+22 more)

### Community 7 - "ProviderManagementServiceTests"
Cohesion: 0.33
Nodes (4): ProviderListQuery, Page, Status, ProviderManagementServiceTests

### Community 8 - "BookingMonitorServiceTests"
Cohesion: 0.09
Nodes (28): AdminDashboardService, IAdminDashboardService, AdminBookingDisplay, AdminBookingRowViewModel, BookingId, CustomerName, Display, ProviderName (+20 more)

### Community 9 - "Task"
Cohesion: 0.06
Nodes (22): Plural, AvailabilityHint, AdminAttention, ApplicantNumber, ProviderProfileForm, Address, BusinessName, City (+14 more)

### Community 10 - "InlineData"
Cohesion: 0.07
Nodes (34): CategoryOutcome, Duplicate, Invalid, NoChange, NotAllowed, NotFound, Success, BookingListQuery (+26 more)

### Community 11 - "IActionResult"
Cohesion: 0.17
Nodes (4): DashboardController, HomeController, ProviderServicesController, ICustomerDashboardService

### Community 12 - "MessagingService"
Cohesion: 0.17
Nodes (10): Rules (and where each is enforced), IMessagingService, Me, MessagingService, NowUtc, MarkReadResult, MessageRules, MessagesPageResult (+2 more)

### Community 13 - "CustomerBookingsServiceTests"
Cohesion: 0.06
Nodes (35): ApplicationLookup, ApprovalOutcome, Invalid, NotAllowed, NotFound, NotPending, Success, ApprovalResult (+27 more)

### Community 15 - "FindProviderCardViewModel"
Cohesion: 0.05
Nodes (19): MustBeTrueAttribute, BookingFilters, CustomerBookingTab, ProviderBookingTab, AdminBookingDisplayTests, PluralTests, BookingFiltersTests, CategoryRowViewModelTests (+11 more)

### Community 16 - "ConversationViewModel"
Cohesion: 0.15
Nodes (13): ConversationViewModel, CanSend, ContextText, Initials, Name, PartnerUserId, Preview, SearchText (+5 more)

### Community 17 - "IActionResult"
Cohesion: 0.08
Nodes (28): HomePageService, IHomePageService, CategoryCardViewModel, CategoryId, Description, Name, ProviderCount, FeaturedProviderViewModel (+20 more)

### Community 18 - "ProviderAvailabilityServiceTests"
Cohesion: 0.06
Nodes (41): ProviderUnavailableDate, Provider, ProviderId, Reason, UnavailableDate, UnavailableId, CleanDate, IProviderAvailabilityService (+33 more)

### Community 19 - "ProviderAvailabilityServiceTests"
Cohesion: 0.08
Nodes (23): FindProviderCardViewModel, AvailabilityText, AvailabilityTone, AvgRating, BusinessName, CategoryName, City, Description (+15 more)

### Community 21 - "Servisyo.Web.Models"
Cohesion: 0.14
Nodes (7): Servisyo.Tests, Servisyo.Web.Helpers, Servisyo.Web.Data, Servisyo.Web.Models, Servisyo.Web.Services, ReviewsPageViewModel, System.Globalization

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
Cohesion: 0.07
Nodes (29): Customer, Address, Bookings, City, CustomerId, ProfilePhoto, User, UserId (+21 more)

### Community 28 - "ServiceCategory"
Cohesion: 0.28
Nodes (7): ServiceCategory, CategoryId, CategoryName, Description, IsActive, CustomerDashboardServiceTests, FixedClock

### Community 29 - ".GetAsync"
Cohesion: 0.39
Nodes (3): IProviderDashboardService, ProviderDashboardService, ProviderDashboardResult

### Community 30 - "User"
Cohesion: 0.10
Nodes (15): AdminSeeder, CategorySeeder, User, AccountStatus, CreatedAt, Customer, Email, FirstName (+7 more)

### Community 31 - "Provider"
Cohesion: 0.09
Nodes (22): Provider, Address, ApprovalStatus, Availabilities, AvgRating, BaseFee, Bookings, BusinessName (+14 more)

### Community 33 - "Booking"
Cohesion: 0.19
Nodes (5): CustomerProfileService, ICustomerProfileService, ProviderProfileService, CustomerProfileSaveResult, ProfileRules

### Community 34 - "Booking"
Cohesion: 0.07
Nodes (29): Booking, BookingDate, BookingId, BookingTime, CreatedAt, Customer, CustomerId, EndTime (+21 more)

### Community 35 - "CategoryManagementService"
Cohesion: 0.17
Nodes (8): AdminBookingsController, AdminShell, BookingMonitorService, IBookingMonitorService, CategoryManagementService, CategoryResult, Succeeded, ICategoryManagementService

### Community 36 - "CategoryManagementService"
Cohesion: 0.11
Nodes (19): BookingService, Booking, BookingId, BookingServiceId, DurationMinutesSnapshot, PriceSnapshot, Service, ServiceId (+11 more)

### Community 37 - "CategoriesPageViewModel"
Cohesion: 0.11
Nodes (19): CategoriesPageViewModel, ActiveCount, AddFormError, DisabledCount, FlashIsError, FlashMessage, NewCategory, Rows (+11 more)

### Community 38 - "sync_prompts_to_graphify.py"
Cohesion: 0.07
Nodes (32): Commands, Conventions (match existing code), Git, graphify, Layout, Role, Second brain (where knowledge lives), Servisyo — Claude working guide (+24 more)

### Community 39 - "Task"
Cohesion: 0.10
Nodes (11): FindServicesQuery, Category, Page, Q, Sort, AuthServiceTests, FindServicesServiceTests, FixedClock (+3 more)

### Community 40 - "DateOnly"
Cohesion: 0.24
Nodes (17): addMessage(), applySearch(), autosize(), clearError(), field(), handlePoll(), markRead(), nearBottom() (+9 more)

### Community 42 - "datetime"
Cohesion: 0.17
Nodes (4): BookingLink, ChatFormat, ChatStatus, MessagingViewModelTests

### Community 45 - ".Create"
Cohesion: 0.33
Nodes (5): AdminCategoriesController, CategoryForm, CategoryId, Description, Name

### Community 46 - "http"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 48 - "ProviderDashboardViewModel"
Cohesion: 0.18
Nodes (12): Business rules and where each is enforced, IProviderBookingsService, ProviderBookingsService, Who, BookingAction, Accept, Complete, Reject (+4 more)

### Community 51 - "ProviderDashboardAccess"
Cohesion: 0.14
Nodes (12): coverlet.collector (6.0.4), Microsoft.EntityFrameworkCore.Design (10.0.13), Microsoft.EntityFrameworkCore.InMemory (10.0.13), Microsoft.EntityFrameworkCore.SqlServer (10.0.13), Microsoft.NET.Test.Sdk (17.14.1), System.Security.Cryptography.Pkcs (10.0.13), xunit (2.9.3), xunit.runner.visualstudio (3.1.4) (+4 more)

### Community 52 - "CustomerBookingCardViewModel"
Cohesion: 0.11
Nodes (18): 10:19 — graphify claude install, 10:21 — how does application approval work in Servisyo?, 10:32 — can you make me .md file for claude to maximize tokens, and you are a Senior De…, 10:36 — where i can see the file .md file?, 10:46 — can you make me a sync_prompts_to_graphify.py so that i can take notes and push…, 1. Applying, 2. What the provider can open while they wait, 3. Admin review (+10 more)

### Community 53 - "DashboardState"
Cohesion: 0.13
Nodes (15): CustomerBookingCardViewModel, BookingId, CanCancel, CanReview, CategoryText, FromLine, IsReviewed, Location (+7 more)

### Community 54 - "DashboardState"
Cohesion: 0.17
Nodes (10): DashboardState, BookingConfirmed, NoBooking, QuotationReceived, ServiceCompleted, ServiceInProgress, WaitingForQuotation, DashboardStateInfo (+2 more)

### Community 55 - "Servisyo.Tests"
Cohesion: 0.20
Nodes (10): ProviderBookingsPageViewModel, ActiveTab, Cards, FlashIsError, FlashMessage, Page, PageCount, Shell (+2 more)

### Community 57 - "ProviderBookingsPageViewModel"
Cohesion: 0.16
Nodes (3): Servisyo.Web.Migrations, InitialSchema, ServisyoDbContextModelSnapshot

### Community 58 - "AdminDashboardServiceTests"
Cohesion: 0.23
Nodes (8): AdminUsersController, ChangeStatusForm, Action, Page, Q, Reason, Status, UserId

### Community 59 - ".Decide"
Cohesion: 0.15
Nodes (13): BookingReport, Booking, BookingId, CreatedAt, Reason, ReportedBy, ReportedByUserId, ReportId (+5 more)

### Community 60 - ".ChangeStatus"
Cohesion: 0.15
Nodes (13): ProviderBookingCardViewModel, Actions, BookingId, CategoryText, CustomerName, FromLine, Location, Notes (+5 more)

### Community 62 - ".Moderate"
Cohesion: 0.26
Nodes (7): AdminReviewsController, ReviewModerationForm, Action, Page, Reason, ReviewId, Status

### Community 63 - "CancellationToken"
Cohesion: 0.08
Nodes (23): Install, Limits / choices, New notification: provider applications -> administrators, Notification bell (customer, provider, administrator), Rules, What it does, INotificationService, Me (+15 more)

### Community 64 - "Customer > Find Services — implementation notes"
Cohesion: 0.25
Nodes (7): Choices I made where the design is silent — please check these, Customer > Find Services — implementation notes, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), What it does

### Community 66 - "BookProviderPageViewModel"
Cohesion: 0.07
Nodes (25): BookProviderPageViewModel, AvgRating, BusinessName, City, EarliestDate, FieldErrors, Form, FormError (+17 more)

### Community 68 - "Row"
Cohesion: 0.18
Nodes (11): Row, BookingDate, BookingId, BookingTime, CreatedAt, HasReview, ProviderFirstName, ProviderLastName (+3 more)

### Community 69 - ".GetAsync"
Cohesion: 0.11
Nodes (16): CustomerBookingsPageViewModel, ActiveTab, Cards, FlashIsError, FlashMessage, HasAnyBookings, Page, PageCount (+8 more)

### Community 70 - ".GetAsync"
Cohesion: 0.18
Nodes (11): CustomerBookingsService, ICustomerBookingsService, Who, CustomerBookingsResult, CustomerCancelOutcome, Changed, NotFound, NotPending (+3 more)

### Community 71 - ".Save"
Cohesion: 0.36
Nodes (3): ProviderProfileController, IProviderProfileService, ProviderProfileSaveResult

### Community 72 - "DateOnly"
Cohesion: 0.08
Nodes (23): BookProviderService, DayData, IBookProviderService, ProviderInfo, ServiceRow, Who, CleanDay, BookingFormRules (+15 more)

### Community 74 - ".ForStatus"
Cohesion: 0.22
Nodes (9): Message, IsRead, MessageId, MessageText, Receiver, ReceiverId, Sender, SenderId (+1 more)

### Community 75 - "AuthService"
Cohesion: 0.16
Nodes (7): AuthResult, Error, Succeeded, User, AuthService, IAuthService, AdminApplicationNotificationTests

### Community 76 - ".Initials"
Cohesion: 0.10
Nodes (20): CustomerDashboardViewModel, Current, Recent, Recommended, Shell, State, RecentBookingViewModel, BookingId (+12 more)

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

### Community 85 - "CustomerProfilePageViewModel"
Cohesion: 0.09
Nodes (20): CustomerProfilePageViewModel, AddressError, AddressForm, AddressHasProblem, Email, FieldErrors, FlashIsError, FlashMessage (+12 more)

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

### Community 90 - ".GetAsync"
Cohesion: 0.21
Nodes (8): Rules and where each is enforced, FindServicesService, CategoryChipViewModel, CategoryId, Count, Name, FindServicesResult, FindServicesSort

### Community 92 - "TimeProvider"
Cohesion: 0.16
Nodes (5): CustomerDashboardService, FixedClock, FixedClock, DashboardCurrentBookingTests, FixedClock

### Community 93 - "AdminAction"
Cohesion: 0.08
Nodes (24): 1. Applying, 2. What the provider can open while they wait, 3. Admin review, 4. Making the decision, Answer, Outcome, Q: how does application approval work in Servisyo?, Source Nodes (+16 more)

### Community 94 - "BookingActionOutcome"
Cohesion: 0.33
Nodes (6): BookingActionOutcome, Changed, InvalidTransition, NotFound, Overlap, Success

### Community 95 - "ProviderManagementService"
Cohesion: 0.36
Nodes (4): IProviderManagementService, ProviderManagementService, ProviderDetailsLookup, ProviderServiceLine

### Community 96 - "ProviderUnavailableDate"
Cohesion: 0.47
Nodes (3): addBreak(), days(), renumber()

### Community 97 - "Customer > My Bookings — implementation notes"
Cohesion: 0.20
Nodes (9): Choices I made where the design is silent — please check these, Customer > My Bookings — implementation notes, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), Rules and where each is enforced, Try it with sample data (+1 more)

### Community 98 - "ProviderCardViewModel"
Cohesion: 0.11
Nodes (18): ProviderBadge, ProviderCardViewModel, ApprovalStatus, Badges, BusinessName, Details, ExtraServices, HasRating (+10 more)

### Community 99 - "MessagesPageViewModel"
Cohesion: 0.22
Nodes (9): MessagesPageViewModel, Conversations, FlashIsError, FlashMessage, LastMessageId, Messages, PartnerNoun, Selected (+1 more)

### Community 100 - "PortalShellViewModel"
Cohesion: 0.10
Nodes (17): Choices I made where the design/PDF is silent — please check these, Files, Manual test checklist, Not covered by automated tests, One line to add (Program.cs), Provider > My Profile — implementation notes, Rules and where each is enforced, What it does (+9 more)

### Community 101 - "BookingActionForm"
Cohesion: 0.50
Nodes (4): BookingActionForm, Action, Page, Tab

### Community 102 - "ProviderJobViewModel"
Cohesion: 0.18
Nodes (10): ProviderBookingDisplay, ProviderJobViewModel, BookingId, CustomerName, Date, Display, Location, Status (+2 more)

### Community 152 - ".GetAsync"
Cohesion: 0.25
Nodes (6): ProviderBookingBadge, ProviderBookingsResult, ProviderBookingTabViewModel, Count, Key, Label

### Community 153 - "book-provider.js"
Cohesion: 0.36
Nodes (9): clearSlots(), currentTime(), drawSlots(), durationText(), loadSlots(), peso(), scheduleLoad(), selectedIds() (+1 more)

### Community 154 - "FindServicesPageViewModel"
Cohesion: 0.14
Nodes (13): FindServicesPageViewModel, AllCount, Cards, CategoryId, Chips, FoundText, IsFiltered, Page (+5 more)

### Community 155 - "ReviewCardViewModel"
Cohesion: 0.13
Nodes (14): ReviewActionInfo, ReviewCardViewModel, Actions, Comment, CustomerName, ProviderName, Rating, RatingText (+6 more)

### Community 156 - ".Cancel"
Cohesion: 0.39
Nodes (3): CustomerBookingsController, FindServicesController, IFindServicesService

### Community 157 - "Book this Provider — notes"
Cohesion: 0.29
Nodes (6): Book this Provider — notes, Deliberate differences from the PDF, Install, Not done, Rules enforced on the server (inside one Serializable transaction), What it does

### Community 160 - ".PollAsync"
Cohesion: 0.32
Nodes (6): ChatMessageViewModel, Id, Mine, Text, TimeText, PollResult

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

### Community 166 - "ProviderStatusForm"
Cohesion: 0.40
Nodes (5): ProviderStatusForm, Action, Reason, ReturnPage, ReturnStatus

### Community 167 - "ProvidersPageViewModel"
Cohesion: 0.15
Nodes (13): ProvidersPageViewModel, Cards, CountAll, CountApproved, CountDeactivated, CountPending, CountRejected, CountSuspended (+5 more)

### Community 169 - "Row"
Cohesion: 0.17
Nodes (12): Row, AvgRating, BusinessName, City, Description, EffectivePrice, FirstName, HasBaseFee (+4 more)

### Community 170 - "notifications.js"
Cohesion: 0.38
Nodes (9): close(), el(), icon(), isOpen(), load(), message(), open(), render() (+1 more)

### Community 171 - "CurrentBookingViewModel"
Cohesion: 0.20
Nodes (10): CurrentBookingViewModel, BookingId, Date, Fee, ProviderName, ProviderUserId, Reference, Time (+2 more)

### Community 173 - "CustomerProfileForm"
Cohesion: 0.12
Nodes (15): Customer > My Profile - notes, Deliberately NOT built (and why), Install, Known limits, Rules (and where each is enforced), What is on the page, CustomerAddressForm, Address (+7 more)

### Community 174 - "SendMessageResult"
Cohesion: 0.33
Nodes (6): SendMessageResult, Succeeded, SendOutcome, Invalid, NotAllowed, Sent

### Community 175 - "LoginViewModel"
Cohesion: 0.50
Nodes (4): LoginViewModel, Email, Password, RememberMe

### Community 176 - "CustomerPasswordForm"
Cohesion: 0.50
Nodes (4): CustomerPasswordForm, ConfirmPassword, CurrentPassword, NewPassword

### Community 178 - "Q: graphify claude install"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: graphify claude install, Source Nodes

### Community 179 - "Q: where i can see the file .md file?"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: where i can see the file .md file?, Source Nodes

### Community 180 - "Q: can you make me .md file for claude to maximize tokens, and you are a Senior Dev which is a full stack and current working on this project servisyo and link the .md file to graphify to that i can build my codebase so you can maximize the 2nd brain"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: can you make me .md file for claude to maximize tokens, and you are a Senior Dev which is a full stack and current working on this project servisyo and link the .md file to graphify to that i can build my codebase so you can maximize the 2nd brain, Source Nodes

### Community 181 - "SYNC_PROMPTS_TO_GRAPHIFY.md"
Cohesion: 0.40
Nodes (4): Load existing Graphify data, Paths, Read markdown prompt files from Obsidian folder, Save updated graph JSON

### Community 182 - "BookOutcome"
Cohesion: 0.40
Nodes (5): BookOutcome, Created, Invalid, NotAllowed, ProviderUnavailable

### Community 183 - "ProviderDashboardAccess"
Cohesion: 0.40
Nodes (5): ProviderDashboardAccess, Allowed, NotAllowed, Pending, Rejected

### Community 184 - "Q: can you make me a sync_prompts_to_graphify.py so that i can take notes and push it to my 2nd brain on graphify"
Cohesion: 0.50
Nodes (3): Answer, Outcome, Q: can you make me a sync_prompts_to_graphify.py so that i can take notes and push it to my 2nd brain on graphify

### Community 185 - "ErrorViewModel"
Cohesion: 0.50
Nodes (3): ErrorViewModel, RequestId, ShowRequestId

### Community 186 - "CategoryManagementViewModels.cs"
Cohesion: 0.50
Nodes (3): CategoryToggleForm, CategoryId, Enable

### Community 187 - "CustomerBookingsViewModels.cs"
Cohesion: 0.50
Nodes (3): CancelBookingForm, Page, Tab

### Community 188 - "ProviderDetailsOutcome"
Cohesion: 0.50
Nodes (4): ProviderDetailsOutcome, NotAllowed, NotFound, Ok

### Community 189 - "Customer.cshtml"
Cohesion: 0.50
Nodes (3): CustomerDashboardViewModel, Servisyo.Web.Helpers, System.Globalization

## Knowledge Gaps
- **939 isolated node(s):** `Servisyo.Web.ViewModels`, `DashboardStateInfo`, `FindServicesPageViewModel`, `ServicesPageViewModel`, `ProviderApplicationViewModel` (+934 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1195 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **75 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Work-memory lessons

**Preferred sources** — corroborated by past sessions; start here.
- `CLAUDE.md` (3× useful, score=2.996894879)
- `graphify` (2× useful, score=1.9979298)

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Servisyo.Web.ViewModels` connect `Servisyo.Web.ViewModels` to `ReviewModerationServiceTests`, `InlineData`, `IActionResult`, `ProviderAvailabilityServiceTests`, `Servisyo.Web.Models`, `Servisyo.Web.Controllers`, `BookProvider/Index.cshtml`, `CustomerProfileForm`, `ProviderAvailability/Index.cshtml`, `CategoryManagementViewModels.cs`, `CustomerBookingsViewModels.cs`, `Customer.cshtml`, `ProviderProfile/Index.cshtml`, `CancellationToken`, `DateOnly`, `system_globalization`, `.GetAsync`, `PortalShellViewModel`, `ProviderJobViewModel`?**
  _High betweenness centrality (0.093) - this node is a cross-community bridge._
- **What connects `Servisyo.Web.ViewModels`, `DashboardStateInfo`, `FindServicesPageViewModel` to the rest of the system?**
  _939 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Servisyo.Web.ViewModels` be split into smaller, more focused modules?**
  _Cohesion score 0.03891449052739376 - nodes in this community are weakly interconnected._
- **Why does `PortalShellViewModel` connect `PortalShellViewModel` to `ProviderProfilePageViewModel`, `ReviewModerationServiceTests`, `UsersPageViewModel`, `AuthResult`, `BookingMonitorServiceTests`, `InlineData`, `MessagingService`, `CustomerBookingsServiceTests`, `ProviderAvailabilityServiceTests`, `CustomerProfilePageViewModel`, `.GetAsync`, `FindServicesPageViewModel`, `.GetAsync`, `CategoryManagementService`, `CategoriesPageViewModel`, `ProvidersPageViewModel`, `datetime`, `CustomerProfileForm`, `Servisyo.Tests`, `.GetAsync`, `.GetAsync`, `CustomerProfilePageViewModel`, `.GetAsync`, `MessagesPageViewModel`?**
  _High betweenness centrality (0.050) - this node is a cross-community bridge._
- **Should `ProviderProfilePageViewModel` be split into smaller, more focused modules?**
  _Cohesion score 0.125 - nodes in this community are weakly interconnected._
- **Why does `ServisyoDbContext` connect `CategoryManagementServiceTests` to `Servisyo.Web.ViewModels`, `MessagingServiceTests`, `ReviewModerationServiceTests`, `UsersPageViewModel`, `AuthResult`, `ProviderManagementServiceTests`, `BookingMonitorServiceTests`, `Task`, `InlineData`, `MessagingService`, `CustomerBookingsServiceTests`, `.AddCustomerAsync`, `IActionResult`, `ProviderAvailabilityServiceTests`, `.AddCustomerAsync`, `CustomerProfileService`, `Customer`, `ServiceCategory`, `.GetAsync`, `User`, `Provider`, `Booking`, `Booking`, `CategoryManagementService`, `CategoryManagementService`, `sync_prompts_to_graphify.py`, `Task`, `.GetBookingTitlesAsync`, `ProviderDashboardViewModel`, `ProviderDashboardServiceTests`, `CategoriesPageViewModel`, `.Decide`, `.GetAsync`, `.ForStatus`, `.GetAsync`, `AdminAction`?**
  _High betweenness centrality (0.040) - this node is a cross-community bridge._
- **Should `ReviewModerationServiceTests` be split into smaller, more focused modules?**
  _Cohesion score 0.053164556962025315 - nodes in this community are weakly interconnected._