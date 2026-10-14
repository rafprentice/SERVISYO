using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class MessagingViewModelTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private static readonly DateTime NowUtc = new(2026, 10, 14, 2, 0, 0);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   \n  ", null)]
    [InlineData("  hello  ", "hello")]
    [InlineData("line one\r\nline two", "line one\nline two")]
    [InlineData("a\rb", "a\nb")]
    public void Normalize_TrimsAndFixesLineBreaks(string? input, string? expected)
        => Assert.Equal(expected, MessageRules.Normalize(input));

    [Fact]
    public void Preview_ShortTextIsKept_LongTextIsCut()
    {
        Assert.Equal("Hello there", MessageRules.Preview("Hello there"));
        Assert.Equal(new string('a', 40), MessageRules.Preview(new string('a', 40)));
        Assert.Equal(new string('a', 39) + "…", MessageRules.Preview(new string('a', 60)));
    }

    [Fact]
    public void Preview_IsOneLine_AndBlankGivesEmpty()
    {
        Assert.Equal("Line one Line two", MessageRules.Preview("Line one\nLine two"));
        Assert.Equal(string.Empty, MessageRules.Preview("   "));
        Assert.Equal(string.Empty, MessageRules.Preview(null));
    }

    [Fact]
    public void ListTime_TodayYesterdayWeekdayAndDate()
    {
        Assert.Equal("10:42 AM", ChatFormat.ListTime(new DateTime(2026, 10, 14, 2, 42, 0), NowUtc));
        Assert.Equal("Yesterday", ChatFormat.ListTime(new DateTime(2026, 10, 13, 4, 0, 0), NowUtc));
        Assert.Equal("Sun", ChatFormat.ListTime(new DateTime(2026, 10, 11, 4, 0, 0), NowUtc));       // 3 days ago
        Assert.Equal("Oct 7", ChatFormat.ListTime(new DateTime(2026, 10, 7, 4, 0, 0), NowUtc));      // a week ago
    }

    [Fact]
    public void ListTime_UsesManilaDays_NotUtcDays()
    {
        // 17:00 UTC on the 13th is 1:00 AM on the 14th in Manila, so it is "today"
        Assert.Equal("1:00 AM", ChatFormat.ListTime(new DateTime(2026, 10, 13, 17, 0, 0), NowUtc));
    }

    [Fact]
    public void BubbleTime_SameDayShowsTimeOnly_OtherDaysShowTheDate()
    {
        Assert.Equal("10:20 AM", ChatFormat.BubbleTime(new DateTime(2026, 10, 14, 2, 20, 0), NowUtc));
        Assert.Equal("Oct 13 · 10:20 AM", ChatFormat.BubbleTime(new DateTime(2026, 10, 13, 2, 20, 0), NowUtc));
    }

    [Theory]
    [InlineData(BookingStatuses.Pending, "Pending Negotiation", "pending", true)]
    [InlineData(BookingStatuses.Accepted, "Active Booking", "completed", true)]
    [InlineData(BookingStatuses.InProgress, "Active Booking", "completed", true)]
    [InlineData(BookingStatuses.Completed, "Completed", "muted", false)]
    [InlineData(BookingStatuses.Rejected, "Rejected", "danger", false)]
    [InlineData(BookingStatuses.Cancelled, "Cancelled", "muted", false)]
    public void ChatStatus_MapsEveryBookingStatus(string status, string text, string tone, bool live)
    {
        var (t, c) = ChatStatus.For(status);
        Assert.Equal(text, t);
        Assert.Equal(tone, c);
        Assert.Equal(live, ChatStatus.IsLive(status));
    }

    [Fact]
    public void SearchText_IsLowerCase_AndCoversNameAndContext()
    {
        var c = new ConversationViewModel { Name = "Marco Reyes", ContextText = "Pipe Repair" };
        Assert.Equal("marco reyes pipe repair", c.SearchText);
        Assert.Contains("marco", new ConversationViewModel { Name = "Marco Reyes" }.SearchText);
    }

    [Fact]
    public void PartnerNoun_FollowsTheRole()
    {
        Assert.Equal("provider", new MessagesPageViewModel { Shell = new PortalShellViewModel { Role = "Customer" } }.PartnerNoun);
        Assert.Equal("customer", new MessagesPageViewModel { Shell = new PortalShellViewModel { Role = "Provider" } }.PartnerNoun);
    }
}

public class MessagingServiceTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime NowUtc = new(2026, 10, 14, 2, 0, 0);
    private static readonly DateOnly Day = new(2026, 10, 20);

    private static (MessagingService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new MessagingService(db, new FixedClock()), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string account = AccountStatuses.Active,
        string first = "Marco", string last = "Reyes", string approval = ApprovalStatuses.Approved)
    {
        var p = new Provider { BusinessName = $"{first}'s Works", ApprovalStatus = approval, User = NewUser(first, last, Roles.ServiceProvider, account) };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<Customer> AddCustomerAsync(ServisyoDbContext db, string first = "Ana", string last = "Reyes",
        string status = AccountStatuses.Active)
    {
        var c = new Customer { User = NewUser(first, last, Roles.Customer, status) };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static async Task<Booking> AddBookingAsync(ServisyoDbContext db, Customer c, Provider p, string status = BookingStatuses.Pending,
        DateTime? createdUtc = null, (string Service, string Category)[]? services = null)
    {
        var b = new Booking
        {
            CustomerId = c.CustomerId, ProviderId = p.ProviderId,
            BookingDate = Day, BookingTime = new TimeOnly(9, 0), TotalDurationMinutes = 60,
            TotalFee = 600m, Status = status, ServiceLocation = "45 Mabini St", ServiceDescription = "Leaking pipe.",
            CreatedAt = createdUtc ?? NowUtc.AddDays(-1), UpdatedAt = NowUtc.AddDays(-1),
            RowVersion = new byte[] { 1 }
        };

        foreach (var (serviceName, categoryName) in services ?? new[] { ("Pipe Repair", "Plumbing") })
        {
            var category = await db.ServiceCategories.FirstOrDefaultAsync(x => x.CategoryName == categoryName)
                           ?? new ServiceCategory { CategoryName = categoryName };
            var service = new Service { ProviderId = p.ProviderId, Category = category, ServiceName = serviceName, Price = 600m, DurationMinutes = 60 };
            b.Services.Add(new BookingService { Service = service, PriceSnapshot = 600m, DurationMinutesSnapshot = 60 });
        }

        db.Bookings.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    private static async Task<Message> AddMessageAsync(ServisyoDbContext db, int fromUserId, int toUserId, string text,
        DateTime? sentUtc = null, bool isRead = false)
    {
        var m = new Message { SenderId = fromUserId, ReceiverId = toUserId, MessageText = text, SentAt = sentUtc ?? NowUtc.AddMinutes(-5), IsRead = isRead };
        db.Messages.Add(m);
        await db.SaveChangesAsync();
        return m;
    }

    // ------------------------------------------------------------------ who may use the chat

    [Fact]
    public async Task Administrator_IsNotAllowed()
    {
        var (svc, db) = Create();
        var admin = NewUser("Ad", "Min", Roles.Administrator);
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var page = await svc.GetPageAsync(admin.UserId, null);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, page.Access);
        Assert.Null(page.Page);
        Assert.Equal(SendOutcome.NotAllowed, (await svc.SendAsync(admin.UserId, 1, "hi")).Outcome);
    }

    [Theory]
    [InlineData(AccountStatuses.Suspended)]
    [InlineData(AccountStatuses.Deactivated)]
    public async Task InactiveCustomer_IsNotAllowed(string status)
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db, status: status);

        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetPageAsync(c.UserId, null)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.PollAsync(c.UserId, null, 0)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.MarkReadAsync(c.UserId, 1)).Access);
    }

    [Fact]
    public async Task UnknownUser_IsNotAllowed()
    {
        var (svc, _) = Create();
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetPageAsync(9999, null)).Access);
    }

    [Fact]
    public async Task PendingAndRejectedProviders_AreTurnedAway_WithTheirOwnAccess()
    {
        var (svc, db) = Create();
        var pending = await AddProviderAsync(db, approval: ApprovalStatuses.Pending, first: "Pen");
        var rejected = await AddProviderAsync(db, approval: ApprovalStatuses.Rejected, first: "Rej");

        Assert.Equal(ProviderDashboardAccess.Pending, (await svc.GetPageAsync(pending.UserId, null)).Access);
        Assert.Equal(ProviderDashboardAccess.Rejected, (await svc.GetPageAsync(rejected.UserId, null)).Access);

        var customer = await AddCustomerAsync(db);
        await AddBookingAsync(db, customer, pending);
        var send = await svc.SendAsync(pending.UserId, customer.UserId, "hello");
        Assert.Equal(SendOutcome.NotAllowed, send.Outcome);
        Assert.Equal(ProviderDashboardAccess.Pending, send.Access);
        Assert.Empty(db.Messages);
    }

    // ------------------------------------------------------------------ the page

    [Fact]
    public async Task NoBookings_MeansNoConversations()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);

        var page = (await svc.GetPageAsync(c.UserId, null)).Page!;
        Assert.Empty(page.Conversations);
        Assert.Null(page.Selected);
        Assert.Empty(page.Messages);
        Assert.Equal("Customer", page.Shell.Role);
        Assert.Equal("provider", page.PartnerNoun);
    }

    [Fact]
    public async Task Customer_SeesTheProvider_WithCategoryAndStatus()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p, BookingStatuses.Pending);

        var page = (await svc.GetPageAsync(c.UserId, null)).Page!;
        var conv = Assert.Single(page.Conversations);
        Assert.Equal(p.UserId, conv.PartnerUserId);
        Assert.Equal("Marco Reyes", conv.Name);
        Assert.Equal("MR", conv.Initials);
        Assert.Equal("Plumbing", conv.ContextText);                 // a customer sees the provider's trade
        Assert.Equal("Pending Negotiation", conv.StatusText);
        Assert.Equal("No messages yet", conv.Preview);
        Assert.True(conv.CanSend);
        Assert.Same(conv, page.Selected);                           // the only conversation opens by itself
    }

    [Fact]
    public async Task Provider_SeesTheCustomer_WithTheBookedService()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p, BookingStatuses.Accepted,
            services: new[] { ("Pipe Repair", "Plumbing"), ("Waterproofing", "Plumbing") });

        var page = (await svc.GetPageAsync(p.UserId, null)).Page!;
        Assert.Equal("Provider", page.Shell.Role);
        Assert.Equal("Marco's Works", page.Shell.Subtitle);
        Assert.Equal("customer", page.PartnerNoun);

        var conv = Assert.Single(page.Conversations);
        Assert.Equal("Ana Reyes", conv.Name);
        Assert.Equal("Pipe Repair & Waterproofing", conv.ContextText);   // a provider sees what was booked
        Assert.Equal("Active Booking", conv.StatusText);
    }

    [Fact]
    public async Task SeveralBookingsWithTheSamePerson_AreOneConversation_DescribedByTheLiveOne()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p, BookingStatuses.Accepted, createdUtc: NowUtc.AddDays(-10));
        await AddBookingAsync(db, c, p, BookingStatuses.Completed, createdUtc: NowUtc.AddDays(-2));      // newer, but not live

        var page = (await svc.GetPageAsync(c.UserId, null)).Page!;
        var conv = Assert.Single(page.Conversations);
        Assert.Equal("Active Booking", conv.StatusText);
    }

    [Fact]
    public async Task WithoutALiveBooking_TheNewestBookingDescribesTheConversation()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p, BookingStatuses.Cancelled, createdUtc: NowUtc.AddDays(-10));
        await AddBookingAsync(db, c, p, BookingStatuses.Completed, createdUtc: NowUtc.AddDays(-2));

        var conv = Assert.Single((await svc.GetPageAsync(c.UserId, null)).Page!.Conversations);
        Assert.Equal("Completed", conv.StatusText);
    }

    [Fact]
    public async Task Conversations_AreSortedByLatestActivity_AndShowPreviewAndTime()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p1 = await AddProviderAsync(db, first: "Marco");
        var p2 = await AddProviderAsync(db, first: "Elena", last: "Cruz");
        await AddBookingAsync(db, c, p1);
        await AddBookingAsync(db, c, p2);
        await AddMessageAsync(db, p1.UserId, c.UserId, "Older words", NowUtc.AddHours(-3), isRead: true);
        await AddMessageAsync(db, p2.UserId, c.UserId, "Newest words", NowUtc.AddMinutes(-18), isRead: true);

        var list = (await svc.PollAsync(c.UserId, null, 0)).Conversations;
        Assert.Equal(new[] { "Elena Cruz", "Marco Reyes" }, list.Select(x => x.Name).ToArray());
        Assert.Equal("Newest words", list[0].Preview);
        Assert.Equal("9:42 AM", list[0].TimeText);
    }

    [Fact]
    public async Task OpeningAConversation_ShowsTheThreadOldestFirst_AndMarksItRead()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p);
        await AddMessageAsync(db, p.UserId, c.UserId, "Hello Ana", NowUtc.AddMinutes(-30));
        await AddMessageAsync(db, c.UserId, p.UserId, "Hi Marco", NowUtc.AddMinutes(-20), isRead: true);
        await AddMessageAsync(db, p.UserId, c.UserId, "Can you come at 9?", NowUtc.AddMinutes(-10));

        Assert.Equal(2, (await svc.PollAsync(c.UserId, p.UserId, 0)).UnreadTotal);      // polling does not read anything

        var page = (await svc.GetPageAsync(c.UserId, p.UserId)).Page!;
        Assert.Equal(new[] { "Hello Ana", "Hi Marco", "Can you come at 9?" }, page.Messages.Select(m => m.Text).ToArray());
        Assert.Equal(new[] { false, true, false }, page.Messages.Select(m => m.Mine).ToArray());
        Assert.Equal(page.Messages.Max(m => m.Id), page.LastMessageId);
        Assert.Equal(0, page.Selected!.Unread);
        Assert.Equal(0, page.Shell.UnreadMessages);
        Assert.All(db.Messages, m => Assert.True(m.IsRead));
    }

    [Fact]
    public async Task OpeningOneConversation_DoesNotMarkAnotherRead()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p1 = await AddProviderAsync(db, first: "Marco");
        var p2 = await AddProviderAsync(db, first: "Elena", last: "Cruz");
        await AddBookingAsync(db, c, p1);
        await AddBookingAsync(db, c, p2);
        await AddMessageAsync(db, p1.UserId, c.UserId, "from Marco");
        await AddMessageAsync(db, p2.UserId, c.UserId, "from Elena");

        var page = (await svc.GetPageAsync(c.UserId, p1.UserId)).Page!;
        Assert.Equal(1, page.Shell.UnreadMessages);
        Assert.Equal(1, page.Conversations.Single(x => x.PartnerUserId == p2.UserId).Unread);
        Assert.True(db.Messages.Single(m => m.SenderId == p1.UserId).IsRead);
        Assert.False(db.Messages.Single(m => m.SenderId == p2.UserId).IsRead);
    }

    [Fact]
    public async Task AskingForSomeoneElse_OpensTheFirstConversationInstead()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        var stranger = await AddProviderAsync(db, first: "Stan", last: "Ranger");
        await AddBookingAsync(db, c, p);
        await AddMessageAsync(db, stranger.UserId, c.UserId, "secret from a stranger");

        var page = (await svc.GetPageAsync(c.UserId, stranger.UserId)).Page!;
        Assert.Equal(p.UserId, page.Selected!.PartnerUserId);
        Assert.Empty(page.Messages);
        Assert.DoesNotContain(page.Conversations, x => x.PartnerUserId == stranger.UserId);
        Assert.False(db.Messages.Single().IsRead);                      // a stranger's message is not marked read either
    }

    [Fact]
    public async Task ATrulyOtherPersonsMessages_NeverAppear()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var ben = await AddCustomerAsync(db, "Ben", "Cruz");
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, p);
        await AddBookingAsync(db, ben, p);
        await AddMessageAsync(db, p.UserId, ana.UserId, "for Ana");
        await AddMessageAsync(db, p.UserId, ben.UserId, "for Ben");
        await AddMessageAsync(db, ben.UserId, p.UserId, "Ben to Marco");

        var page = (await svc.GetPageAsync(ana.UserId, p.UserId)).Page!;
        Assert.Equal(new[] { "for Ana" }, page.Messages.Select(m => m.Text).ToArray());

        var providerSide = (await svc.GetPageAsync(p.UserId, ana.UserId)).Page!;
        Assert.Equal(new[] { "for Ana" }, providerSide.Messages.Select(m => m.Text).ToArray());
        Assert.Equal(2, providerSide.Conversations.Count);
    }

    [Fact]
    public async Task OnlyTheNewest100Messages_AreLoaded_OldestFirst()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p);
        for (var i = 1; i <= 105; i++)
            await AddMessageAsync(db, i % 2 == 0 ? p.UserId : c.UserId, i % 2 == 0 ? c.UserId : p.UserId, $"m{i}", NowUtc.AddMinutes(-200 + i), isRead: true);

        var messages = (await svc.GetPageAsync(c.UserId, p.UserId)).Page!.Messages;
        Assert.Equal(MessageRules.ThreadLimit, messages.Count);
        Assert.Equal("m6", messages[0].Text);
        Assert.Equal("m105", messages[^1].Text);
    }

    [Fact]
    public async Task SuspendedPartner_HistoryStaysReadable_ButSendingIsOff()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db, account: AccountStatuses.Suspended);
        await AddBookingAsync(db, c, p, BookingStatuses.Completed);
        await AddMessageAsync(db, p.UserId, c.UserId, "Thanks for booking");

        var page = (await svc.GetPageAsync(c.UserId, p.UserId)).Page!;
        Assert.False(page.Selected!.CanSend);
        Assert.Single(page.Messages);

        var send = await svc.SendAsync(c.UserId, p.UserId, "are you there?");
        Assert.Equal(SendOutcome.NotAllowed, send.Outcome);
        Assert.Single(db.Messages);
    }

    // ------------------------------------------------------------------ sending

    [Fact]
    public async Task Send_SavesATrimmedUnreadMessage_ForTheOtherPerson()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p);

        var result = await svc.SendAsync(c.UserId, p.UserId, "  Hello there\r\nSecond line  ");

        Assert.True(result.Succeeded);
        Assert.Equal("Hello there\nSecond line", result.Sent!.Text);
        Assert.True(result.Sent.Mine);
        Assert.Equal("10:00 AM", result.Sent.TimeText);

        var saved = Assert.Single(db.Messages);
        Assert.Equal(c.UserId, saved.SenderId);          // the sender is the signed-in user
        Assert.Equal(p.UserId, saved.ReceiverId);
        Assert.False(saved.IsRead);
        Assert.Equal(NowUtc, saved.SentAt);
        Assert.Equal("Hello there\nSecond line", saved.MessageText);
    }

    [Fact]
    public async Task Provider_CanReply_ToTheirCustomer()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p, BookingStatuses.Accepted);

        Assert.True((await svc.SendAsync(p.UserId, c.UserId, "See you at 9")).Succeeded);
        Assert.Equal(1, (await svc.PollAsync(c.UserId, null, 0)).UnreadTotal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("\r\n \n")]
    public async Task Send_RefusesAnEmptyMessage(string? text)
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p);

        var result = await svc.SendAsync(c.UserId, p.UserId, text);
        Assert.Equal(SendOutcome.Invalid, result.Outcome);
        Assert.Equal("Type a message first.", result.Message);
        Assert.Empty(db.Messages);
    }

    [Fact]
    public async Task Send_AllowsExactly1000Characters_AndRefusesMore()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p);

        Assert.True((await svc.SendAsync(c.UserId, p.UserId, new string('x', MessageRules.MaxLength))).Succeeded);

        var tooLong = await svc.SendAsync(c.UserId, p.UserId, new string('x', MessageRules.MaxLength + 1));
        Assert.Equal(SendOutcome.Invalid, tooLong.Outcome);
        Assert.Single(db.Messages);
    }

    [Fact]
    public async Task Send_ToSomeoneWithNoSharedBooking_IsRefused()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        var otherProvider = await AddProviderAsync(db, first: "Stan", last: "Ranger");
        await AddBookingAsync(db, c, p);

        var result = await svc.SendAsync(c.UserId, otherProvider.UserId, "hello?");
        Assert.Equal(SendOutcome.NotAllowed, result.Outcome);
        Assert.Empty(db.Messages);
    }

    [Fact]
    public async Task Send_CustomerCannotMessageAnotherCustomer()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var ben = await AddCustomerAsync(db, "Ben", "Cruz");
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, p);
        await AddBookingAsync(db, ben, p);

        // customers only ever talk to providers of their own bookings
        Assert.Equal(SendOutcome.NotAllowed, (await svc.SendAsync(ana.UserId, ben.UserId, "hi Ben")).Outcome);
        // the provider shares a booking with each of them, so both are fine
        Assert.True((await svc.SendAsync(p.UserId, ben.UserId, "hi Ben")).Succeeded);
        Assert.Equal(ben.UserId, db.Messages.Single().ReceiverId);
    }

    [Fact]
    public async Task Send_ToAProviderWhoseBookingIsAnotherCustomers_IsRefused()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var ben = await AddCustomerAsync(db, "Ben", "Cruz");
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, ben, p);                // only Ben has booked this provider

        Assert.Equal(SendOutcome.NotAllowed, (await svc.SendAsync(ana.UserId, p.UserId, "hello")).Outcome);
        Assert.Empty(db.Messages);
    }

    [Fact]
    public async Task Send_ToASuspendedUser_IsRefused()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db, status: AccountStatuses.Suspended);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p);

        // the provider cannot message a suspended customer
        var toSuspended = await svc.SendAsync(p.UserId, c.UserId, "hello");
        Assert.Equal(SendOutcome.NotAllowed, toSuspended.Outcome);
        // and the suspended customer cannot send either
        Assert.Equal(SendOutcome.NotAllowed, (await svc.SendAsync(c.UserId, p.UserId, "hello")).Outcome);
        Assert.Empty(db.Messages);
    }

    // ------------------------------------------------------------------ polling and reading

    [Fact]
    public async Task Poll_ReturnsOnlyMessagesAfterTheGivenNumber()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p);
        var first = await AddMessageAsync(db, p.UserId, c.UserId, "one", NowUtc.AddMinutes(-3));
        await AddMessageAsync(db, p.UserId, c.UserId, "two", NowUtc.AddMinutes(-2));
        await AddMessageAsync(db, c.UserId, p.UserId, "three", NowUtc.AddMinutes(-1));

        var poll = await svc.PollAsync(c.UserId, p.UserId, first.MessageId);
        Assert.Equal(new[] { "two", "three" }, poll.Messages.Select(m => m.Text).ToArray());
        Assert.Equal(new[] { false, true }, poll.Messages.Select(m => m.Mine).ToArray());
        Assert.Equal("three", Assert.Single(poll.Conversations).Preview);

        Assert.Empty((await svc.PollAsync(c.UserId, p.UserId, poll.Messages.Max(m => m.Id))).Messages);
    }

    [Fact]
    public async Task Poll_AboutAStranger_GivesNoMessages()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        var stranger = await AddProviderAsync(db, first: "Stan", last: "Ranger");
        await AddBookingAsync(db, c, p);
        await AddMessageAsync(db, stranger.UserId, c.UserId, "psst");

        var poll = await svc.PollAsync(c.UserId, stranger.UserId, 0);
        Assert.Empty(poll.Messages);
        Assert.DoesNotContain(poll.Conversations, x => x.PartnerUserId == stranger.UserId);
    }

    [Fact]
    public async Task Poll_NegativeAfterNumber_IsTreatedAsZero()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        await AddBookingAsync(db, c, p);
        await AddMessageAsync(db, p.UserId, c.UserId, "one");

        Assert.Single((await svc.PollAsync(c.UserId, p.UserId, -50)).Messages);
    }

    [Fact]
    public async Task MarkRead_ClearsOnlyThatPersonsMessages_AndReturnsTheTotal()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p1 = await AddProviderAsync(db, first: "Marco");
        var p2 = await AddProviderAsync(db, first: "Elena", last: "Cruz");
        await AddBookingAsync(db, c, p1);
        await AddBookingAsync(db, c, p2);
        await AddMessageAsync(db, p1.UserId, c.UserId, "a");
        await AddMessageAsync(db, p1.UserId, c.UserId, "b");
        await AddMessageAsync(db, p2.UserId, c.UserId, "c");
        await AddMessageAsync(db, c.UserId, p1.UserId, "mine, unread by Marco");

        var result = await svc.MarkReadAsync(c.UserId, p1.UserId);
        Assert.Equal(1, result.UnreadTotal);                                     // only Elena's message is still unread
        Assert.False(db.Messages.Single(m => m.MessageText == "mine, unread by Marco").IsRead);   // the customer cannot "read" their own message
    }

    [Fact]
    public async Task MarkRead_ForAStranger_ChangesNothing()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        var stranger = await AddProviderAsync(db, first: "Stan", last: "Ranger");
        await AddBookingAsync(db, c, p);
        await AddMessageAsync(db, stranger.UserId, c.UserId, "psst");

        var result = await svc.MarkReadAsync(c.UserId, stranger.UserId);
        Assert.Equal(1, result.UnreadTotal);
        Assert.False(db.Messages.Single().IsRead);
    }
}
