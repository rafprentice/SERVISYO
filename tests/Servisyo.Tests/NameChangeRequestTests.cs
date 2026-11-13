using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.8: an approved provider's name is locked, so they REQUEST a name change and an administrator
/// approves (the new name is copied to USERS) or rejects it. One waiting request per provider; every answer is an
/// ADMIN_ACTIONS row the request points to.
public class NameChangeRequestTests
{
    /// Records what would have been pushed to the open pages (SignalR).
    private sealed class FakeNameChangeLive : INameChangeLiveUpdates
    {
        public List<(int ProviderId, int ProviderUserId, string What)> Sent { get; } = new();
        public Task NameChangeChangedAsync(int providerId, int providerUserId, string what)
        {
            Sent.Add((providerId, providerUserId, what));
            return Task.CompletedTask;
        }
    }

    private sealed record World(ServisyoDbContext Db, NameChangeRequestService Svc, User AdminUser, Provider Mary, Provider Liza,
        Provider Applicant, Customer Ana, FakeNameChangeLive Live);

    private static User NewUser(string first, string? middle, string last, string role, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, MiddleName = middle, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
        Role = role, AccountStatus = status
    };

    private static async Task<World> WorldAsync()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", null, "Admin", Roles.Administrator);
        var mary = new Provider { BusinessName = "Mary Plumbing", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Mary", "Cruz", "Reyes", Roles.ServiceProvider) };
        var liza = new Provider { BusinessName = "Liza Cleans", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Liza", "Lim", "Tan", Roles.ServiceProvider) };
        var applicant = new Provider { BusinessName = "New Shop", ApprovalStatus = ApprovalStatuses.Pending, User = NewUser("Nico", "Dy", "Go", Roles.ServiceProvider) };
        var ana = new Customer { User = NewUser("Ana", null, "Santos", Roles.Customer) };
        db.Users.Add(admin);
        db.Providers.AddRange(mary, liza, applicant);
        db.Customers.Add(ana);
        await db.SaveChangesAsync();
        var live = new FakeNameChangeLive();
        return new(db, new NameChangeRequestService(db, null, live), admin, mary, liza, applicant, ana, live);
    }

    private static NameChangeForm Form(string? first = "Mary", string? middle = "Cruz", string? last = "Santos", string? reason = "Married") =>
        new() { FirstName = first, MiddleName = middle, LastName = last, Reason = reason };

    private static async Task<int> PendingIdAsync(World w, Provider p) =>
        await w.Db.NameChangeRequests.Where(r => r.ProviderId == p.ProviderId && r.Status == NameChangeStatuses.Pending).Select(r => r.RequestId).SingleAsync();

    // ------------------------------------------------------------ the provider asks

    [Fact]
    public async Task ARequest_IsSavedAsPending_WithOnlyTheNewName_AndPushedLive()
    {
        var w = await WorldAsync();

        var result = await w.Svc.RequestAsync(w.Mary.UserId, Form(first: "  Mary ", last: "Santos   Dela  Cruz", reason: "  Married  "));

        Assert.True(result.Done);
        Assert.Equal(NameChangeRules.Sent, result.Message);
        var saved = await w.Db.NameChangeRequests.AsNoTracking().SingleAsync();
        Assert.Equal(("Mary", "Cruz", "Santos Dela Cruz", "Married"), (saved.FirstName, saved.MiddleName, saved.LastName, saved.Reason));
        Assert.Equal(NameChangeStatuses.Pending, saved.Status);
        Assert.Null(saved.DecidedByActionId);
        Assert.Equal(w.Mary.ProviderId, saved.ProviderId);
        // nothing changes on the account until an administrator approves
        Assert.Equal("Reyes", (await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == w.Mary.UserId)).LastName);
        Assert.Equal((w.Mary.ProviderId, w.Mary.UserId, "requested"), Assert.Single(w.Live.Sent));
    }

    [Theory]
    [InlineData("", "Cruz", "Santos", "Married", "FirstName", NameChangeRules.FirstMissing)]
    [InlineData("Mary2", "Cruz", "Santos", "Married", "FirstName", InputRules.NameMessage)]
    [InlineData("Mary", "", "Santos", "Married", "MiddleName", NameChangeRules.MiddleMissing)]
    [InlineData("Mary", "C", "Santos", "Married", "MiddleName", InputRules.MiddleNameTooShortMessage)]
    [InlineData("Mary", "Cruz", "   ", "Married", "LastName", NameChangeRules.LastMissing)]
    [InlineData("Mary", "Cruz", "Santos", "  ", "Reason", NameChangeRules.ReasonMissing)]
    public async Task AWrongForm_SavesNothing_AndSaysWhichBox(string first, string middle, string last, string reason, string field, string message)
    {
        var w = await WorldAsync();

        var result = await w.Svc.RequestAsync(w.Mary.UserId, Form(first, middle, last, reason));

        Assert.Equal(NameChangeOutcome.Invalid, result.Outcome);
        Assert.Equal(message, result.FieldErrors[field]);
        Assert.Empty(w.Db.NameChangeRequests);
        Assert.Empty(w.Live.Sent);
    }

    [Fact]
    public async Task TooLong_IsRefused()
    {
        var w = await WorldAsync();

        var name = await w.Svc.RequestAsync(w.Mary.UserId, Form(last: new string('a', 101)));
        var reason = await w.Svc.RequestAsync(w.Mary.UserId, Form(reason: new string('a', 256)));

        Assert.Equal(NameChangeRules.TooLong, name.FieldErrors["LastName"]);
        Assert.Equal(NameChangeRules.ReasonTooLong, reason.FieldErrors["Reason"]);
        Assert.Empty(w.Db.NameChangeRequests);
    }

    [Fact]
    public async Task TheSameName_IsNotAChange_ButADifferentCapitalLetterIs()
    {
        var w = await WorldAsync();

        var same = await w.Svc.RequestAsync(w.Mary.UserId, Form(last: "  Reyes "));
        Assert.Equal(NameChangeOutcome.Invalid, same.Outcome);
        Assert.Equal(NameChangeRules.SameName, same.Message);
        Assert.Empty(w.Db.NameChangeRequests);

        Assert.True((await w.Svc.RequestAsync(w.Mary.UserId, Form(last: "REYES", reason: "Capital letters"))).Done);
    }

    [Fact]
    public async Task OnlyOneRequest_CanWait_PerProvider_OthersAreNotAffected()
    {
        var w = await WorldAsync();
        Assert.True((await w.Svc.RequestAsync(w.Mary.UserId, Form())).Done);

        var second = await w.Svc.RequestAsync(w.Mary.UserId, Form(last: "Lopez"));
        var liza = await w.Svc.RequestAsync(w.Liza.UserId, Form("Liza", "Lim", "Uy"));

        Assert.Equal(NameChangeRules.OnePending, second.Message);
        Assert.True(liza.Done);
        Assert.Equal(2, await w.Db.NameChangeRequests.CountAsync());
    }

    [Fact]
    public async Task OnlyAnActiveApprovedProvider_CanAsk()
    {
        var w = await WorldAsync();
        var suspended = new Provider { BusinessName = "Off", ApprovalStatus = ApprovalStatuses.Approved,
            User = NewUser("Sam", "Lee", "Ong", Roles.ServiceProvider, AccountStatuses.Suspended) };
        w.Db.Providers.Add(suspended);
        await w.Db.SaveChangesAsync();

        foreach (var userId in new[] { w.Applicant.UserId, suspended.UserId, w.Ana.UserId, w.AdminUser.UserId, 999 })
        {
            var result = await w.Svc.RequestAsync(userId, Form("Someone", "Else", "Entirely"));
            Assert.Equal(NameChangeOutcome.NotAllowed, result.Outcome);
            Assert.Null(await w.Svc.GetSectionAsync(userId));
        }
        Assert.Empty(w.Db.NameChangeRequests);
    }

    [Fact]
    public async Task TheProvider_CanCancelOnlyTheirOwnWaitingRequest()
    {
        var w = await WorldAsync();
        await w.Svc.RequestAsync(w.Mary.UserId, Form());
        var id = await PendingIdAsync(w, w.Mary);

        var byLiza = await w.Svc.CancelAsync(w.Liza.UserId, id);
        Assert.Equal(NameChangeOutcome.NotFound, byLiza.Outcome);
        Assert.Single(w.Db.NameChangeRequests);

        var byMary = await w.Svc.CancelAsync(w.Mary.UserId, id);
        Assert.True(byMary.Done);
        Assert.Empty(w.Db.NameChangeRequests);
        Assert.Equal("cancelled", w.Live.Sent[^1].What);

        // a new request can be sent again
        Assert.True((await w.Svc.RequestAsync(w.Mary.UserId, Form(last: "Lopez"))).Done);
    }

    [Fact]
    public async Task AnAnsweredRequest_CannotBeCancelled()
    {
        var w = await WorldAsync();
        await w.Svc.RequestAsync(w.Mary.UserId, Form());
        var id = await PendingIdAsync(w, w.Mary);
        await w.Svc.DecideAsync(w.AdminUser.UserId, id, approve: false);

        Assert.Equal(NameChangeOutcome.NotFound, (await w.Svc.CancelAsync(w.Mary.UserId, id)).Outcome);
        Assert.Equal(NameChangeStatuses.Rejected, (await w.Db.NameChangeRequests.AsNoTracking().SingleAsync()).Status);
    }

    // ------------------------------------------------------------ the administrator answers

    [Fact]
    public async Task Approve_CopiesTheNewName_ToTheAccount_WithTheAdminLog()
    {
        var w = await WorldAsync();
        await w.Svc.RequestAsync(w.Mary.UserId, Form("Maria", "Cruz", "Santos"));
        var id = await PendingIdAsync(w, w.Mary);

        var result = await w.Svc.DecideAsync(w.AdminUser.UserId, id, approve: true);

        Assert.Equal(NameChangeDecisionOutcome.Success, result.Outcome);
        Assert.Equal("Name changed: Mary Cruz Reyes is now Maria Cruz Santos.", result.Message);
        var user = await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == w.Mary.UserId);
        Assert.Equal(("Maria", "Cruz", "Santos"), (user.FirstName, user.MiddleName, user.LastName));

        var request = await w.Db.NameChangeRequests.AsNoTracking().SingleAsync();
        Assert.Equal(NameChangeStatuses.Approved, request.Status);
        var log = await w.Db.AdminActions.AsNoTracking().SingleAsync(a => a.ActionId == request.DecidedByActionId);
        Assert.Equal(NameChangeRequestsSql.ApprovedActionType, log.ActionType);
        Assert.Equal(AdminTargetTypes.ServiceProvider, log.TargetType);
        Assert.Equal(w.Mary.ProviderId, log.TargetProviderId);
        Assert.Equal(w.AdminUser.UserId, log.AdminUserId);
        Assert.Equal("Mary Cruz Reyes to Maria Cruz Santos", log.Reason);
        Assert.Equal((w.Mary.ProviderId, w.Mary.UserId, "approved"), w.Live.Sent[^1]);
    }

    [Fact]
    public async Task Reject_KeepsTheName_AndIsAnsweredOnlyOnce()
    {
        var w = await WorldAsync();
        await w.Svc.RequestAsync(w.Mary.UserId, Form());
        var id = await PendingIdAsync(w, w.Mary);

        var result = await w.Svc.DecideAsync(w.AdminUser.UserId, id, approve: false);
        var again = await w.Svc.DecideAsync(w.AdminUser.UserId, id, approve: true);

        Assert.Equal(NameChangeDecisionOutcome.Success, result.Outcome);
        Assert.Equal(NameChangeDecisionOutcome.Invalid, again.Outcome);
        Assert.Contains("already rejected", again.Message);
        Assert.Equal("Reyes", (await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == w.Mary.UserId)).LastName);
        var log = Assert.Single(w.Db.AdminActions);
        Assert.Equal(NameChangeRequestsSql.RejectedActionType, log.ActionType);
        Assert.Equal("rejected", w.Live.Sent[^1].What);
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanAnswer_AndAMissingRequestIsNotFound()
    {
        var w = await WorldAsync();
        await w.Svc.RequestAsync(w.Mary.UserId, Form());
        var id = await PendingIdAsync(w, w.Mary);
        var suspendedAdmin = NewUser("Old", null, "Admin", Roles.Administrator, AccountStatuses.Suspended);
        w.Db.Users.Add(suspendedAdmin);
        await w.Db.SaveChangesAsync();

        Assert.Equal(NameChangeDecisionOutcome.NotAllowed, (await w.Svc.DecideAsync(w.Mary.UserId, id, true)).Outcome);
        Assert.Equal(NameChangeDecisionOutcome.NotAllowed, (await w.Svc.DecideAsync(suspendedAdmin.UserId, id, true)).Outcome);
        Assert.Equal(NameChangeDecisionOutcome.NotFound, (await w.Svc.DecideAsync(w.AdminUser.UserId, 9999, true)).Outcome);
        Assert.Equal(NameChangeStatuses.Pending, (await w.Db.NameChangeRequests.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(w.Db.AdminActions);
    }

    // ------------------------------------------------------------ what the pages show

    [Fact]
    public async Task MyProfile_ShowsTheWaitingRequest_ThenTheAnswer()
    {
        var w = await WorldAsync();

        var empty = (await w.Svc.GetSectionAsync(w.Mary.UserId))!;
        Assert.Equal("Mary Cruz Reyes", empty.CurrentName);
        Assert.True(empty.CanRequest);
        Assert.Null(empty.Pending);
        Assert.Null(empty.LastAnswer);
        Assert.Equal(("Mary", "Cruz", "Reyes"), (empty.Form.FirstName, empty.Form.MiddleName, empty.Form.LastName));

        await w.Svc.RequestAsync(w.Mary.UserId, Form());
        var waiting = (await w.Svc.GetSectionAsync(w.Mary.UserId))!;
        Assert.False(waiting.CanRequest);
        Assert.Equal("Mary Cruz Santos", waiting.Pending!.NewName);
        Assert.Equal(NameChangeRules.PendingLabel, waiting.Pending.Label);

        await w.Svc.DecideAsync(w.AdminUser.UserId, await PendingIdAsync(w, w.Mary), approve: true);
        var answered = (await w.Svc.GetSectionAsync(w.Mary.UserId))!;
        Assert.True(answered.CanRequest);
        Assert.Null(answered.Pending);
        Assert.Equal(NameChangeRules.ApprovedLabel, answered.LastAnswer!.Label);
        Assert.Equal("Mary Cruz Santos", answered.CurrentName);
    }

    [Fact]
    public async Task AdminList_PendingFirst_OldestFirst_WithCountsPerTab()
    {
        var w = await WorldAsync();
        await w.Svc.RequestAsync(w.Liza.UserId, Form("Liza", "Lim", "Uy"));
        await w.Svc.RequestAsync(w.Mary.UserId, Form());
        // Liza asked first
        var lizaRequest = await w.Db.NameChangeRequests.SingleAsync(r => r.ProviderId == w.Liza.ProviderId);
        lizaRequest.RequestedAt = DateTime.UtcNow.AddHours(-2);
        await w.Db.SaveChangesAsync();

        var pending = (await w.Svc.GetListAsync(w.AdminUser.UserId, null, 1))!;
        Assert.Equal(NameChangeStatuses.Pending, pending.Status);
        Assert.Equal(new[] { "Liza Lim Tan", "Mary Cruz Reyes" }, pending.Rows.Select(r => r.CurrentName));
        Assert.All(pending.Rows, r => Assert.True(r.CanDecide));
        Assert.Equal((2, 0, 0), (pending.CountPending, pending.CountApproved, pending.CountRejected));

        await w.Svc.DecideAsync(w.AdminUser.UserId, lizaRequest.RequestId, approve: true);
        var approved = (await w.Svc.GetListAsync(w.AdminUser.UserId, "approved", 1))!;
        var row = Assert.Single(approved.Rows);
        Assert.Equal("Liza Lim Uy", row.CurrentName);           // the account already has the new name
        Assert.StartsWith("Approved by Ada on ", row.DecidedText);
        Assert.False(row.CanDecide);
        Assert.Equal(2, (await w.Svc.GetListAsync(w.AdminUser.UserId, "all", 1))!.Rows.Count);

        Assert.Null(await w.Svc.GetListAsync(w.Mary.UserId, null, 1));      // not an administrator
    }

    [Theory]
    [InlineData(null, "Pending")]
    [InlineData("nonsense", "Pending")]
    [InlineData("APPROVED", "Approved")]
    [InlineData("rejected", "Rejected")]
    [InlineData("All", "All")]
    public void Tabs_UnknownMeansPending(string? asked, string tab) => Assert.Equal(tab, NameChangeTabs.Normalize(asked));

    // ------------------------------------------------------------ the database model, the SQL and the design file

    private static IModel Model()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return db.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void TheTable_HasItsRules_OneWaitingRequestPerProvider_AndKeepsTheAdminHistory()
    {
        var entity = Model().FindEntityType(typeof(ProviderNameChangeRequest))!;
        Assert.Equal("PROVIDER_NAME_CHANGE_REQUESTS", entity.GetTableName());
        Assert.Equal(new[] { "REQUEST_ID", "PROVIDER_ID", "FIRST_NAME", "MIDDLE_NAME", "LAST_NAME", "REASON", "STATUS", "REQUESTED_AT", "DECIDED_BY_ACTION_ID" }.OrderBy(x => x),
            entity.GetProperties().Select(p => p.GetColumnName()).OrderBy(x => x));
        Assert.Equal(100, entity.FindProperty(nameof(ProviderNameChangeRequest.FirstName))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(ProviderNameChangeRequest.Reason))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(ProviderNameChangeRequest.MiddleName))!.IsNullable);

        var checks = entity.GetCheckConstraints().ToDictionary(c => c.Name!, c => c.Sql);
        Assert.Equal("[STATUS] IN ('Pending','Approved','Rejected')", checks["CK_PROVIDER_NAME_CHANGE_REQUESTS_STATUS"]);
        Assert.Equal(ServisyoDbContext.DayOffDecisionCheck, checks["CK_PROVIDER_NAME_CHANGE_REQUESTS_DECISION"]);

        var decision = Assert.Single(entity.GetForeignKeys(), f => f.PrincipalEntityType.ClrType == typeof(AdminAction));
        Assert.Equal(DeleteBehavior.Restrict, decision.DeleteBehavior);

        var onePending = Assert.Single(entity.GetIndexes(), i => i.GetDatabaseName() == ServisyoDbContext.NameChangeOnePendingIndex);
        Assert.True(onePending.IsUnique);
        Assert.Equal("[STATUS] = 'Pending'", onePending.GetFilter());
        Assert.Equal(new[] { nameof(ProviderNameChangeRequest.ProviderId) }, onePending.Properties.Select(p => p.Name));
    }

    [Fact]
    public void TheBell_KeepsEveryV27Line_AndAddsTheTwoNameChangeParts()
    {
        var v28 = NameChangeRequestsSql.GetNotifications28;
        Assert.All(DayOffRequestsSql.GetNotifications27.Split('\n').Select(l => l.TrimEnd('\r')), l => Assert.Contains(l, v28));
        Assert.Contains("N''name-change''", v28);
        Assert.Contains("N''name-change-new''", v28);
        Assert.Contains("WHERE @Role = N''Administrator'' AND n.STATUS = N''Pending''", v28);
        Assert.StartsWith("EXEC(N'CREATE OR ALTER ", v28);

        Assert.Equal(new[] { NameChangeRequestsSql.GetNotifications28 }, NameChangeRequestsSql.CreateAll);
        Assert.Equal(new[] { DayOffRequestsSql.GetNotifications27 }, NameChangeRequestsSql.RestoreOld);
        Assert.Contains(NameChangeStatuses.Approved, v28);
    }

    [Fact]
    public void TheDesignFile_IsOnV28_WithTheSameSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "sql", "DatabaseDesign-v2.sql"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var design = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "sql", "DatabaseDesign-v2.sql")).Replace("\r\n", "\n");

        Assert.Contains("DATABASE DESIGN v2.10", design);                        // v2.9 and v2.10 came after
        Assert.Contains("WHAT CHANGED IN v2.8 (EF migration \"NameChangeRequests\")", design);
        Assert.Contains("CREATE TABLE PROVIDER_NAME_CHANGE_REQUESTS (", design);
        Assert.Contains("CONSTRAINT CK_PROVIDER_NAME_CHANGE_REQUESTS_STATUS CHECK (" + ServisyoDbContext.NameChangeStatusCheck + ")", design);
        Assert.Contains("CONSTRAINT CK_PROVIDER_NAME_CHANGE_REQUESTS_DECISION CHECK (" + ServisyoDbContext.NameChangeDecisionCheck + ")", design);
        Assert.Contains("CREATE UNIQUE INDEX " + ServisyoDbContext.NameChangeOnePendingIndex + " ON PROVIDER_NAME_CHANGE_REQUESTS (PROVIDER_ID) WHERE "
            + ServisyoDbContext.NameChangeOnePendingFilter, design);
        Assert.Contains("MigrationId LIKE N'%[_]AuditStandard'", design);          // PART 0: the newest migration (v2.10)
        var sql = NameChangeRequestsSql.GetNotifications28;
        var body = sql["EXEC(N'".Length..sql.LastIndexOf("')", StringComparison.Ordinal)].Replace("''", "'");
        Assert.Contains(body.Replace("\r\n", "\n").Trim(), design);
    }

    [Fact]
    public void TheTools_KnowTheNewMigration()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var add = File.ReadAllText(Path.Combine(dir!.FullName, "tools", "add-name-change-requests.ps1"));
        Assert.Contains(ServisyoDbContext.NameChangeOnePendingIndex, add);
        Assert.Contains("NameChangeRequestsSql.CreateAll[0]", add);
        Assert.Contains("NameChangeRequestsSql.RestoreOld[0]", add);
        var deploy = File.ReadAllText(Path.Combine(dir.FullName, "tools", "make-monster-deploy.ps1"));
        Assert.Contains("$Latest = \"AuditStandard\"", deploy);                   // v2.10 is the newest now
        Assert.Contains("SET QUOTED_IDENTIFIER ON;", deploy);
    }
}
