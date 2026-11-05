using AgriAuction.Application.Identity;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Catalog;
using AgriAuction.Infrastructure.Identity;
using AgriAuction.Infrastructure.Lots;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace AgriAuction.IntegrationTests;

public sealed class CompletionAccountTests(CompletionPostgresFixture fixture):IClassFixture<CompletionPostgresFixture>
{
    [Fact]public async Task RegistrationCannotCreateAdminOrUseShortPasswordOrSkipApproval()
    {
        await using var db=fixture.Create();var service=new AccountWorkflow(db,new PasswordHasher<Account>());
        await Assert.ThrowsAsync<RuleViolation>(()=>service.RegisterAsync(new("bad@completion.invalid","abcdefgh","Bad","Administrator"),default));
        await Assert.ThrowsAsync<RuleViolation>(()=>service.RegisterAsync(new("bad@completion.invalid","short","Bad","Farmer"),default));
        var a=await service.RegisterAsync(new(Guid.NewGuid()+"@completion.invalid","Password1","Applicant","Farmer"),default);Assert.Equal("PendingReview",a.ApprovalState);
        await Assert.ThrowsAsync<WorkflowConflict>(()=>new LotService(db,new(true)).CreateAsync(new(new()),a.Id,default));
    }
    [Fact]public async Task PasswordChangeVerifiesCurrentPasswordAndRevokesEverySession()
    {
        await using var db=fixture.Create();var hasher=new PasswordHasher<Account>();var service=new AccountWorkflow(db,hasher);
        var a=await service.RegisterAsync(new(Guid.NewGuid()+"@completion.invalid","Password1","Applicant","Farmer"),default);
        var identity=new IdentityService(db,hasher);Assert.NotNull(await identity.SignInAsync(a.Email,"Password1",30,default));Assert.NotNull(await identity.SignInAsync(a.Email,"Password1",30,default));
        await Assert.ThrowsAsync<WorkflowConflict>(()=>service.ChangePasswordAsync(a.Id,new("wrong","NewPassword1"),default));
        await service.ChangePasswordAsync(a.Id,new("Password1","NewPassword1"),default);Assert.Equal(0,await db.Sessions.CountAsync(s=>s.AccountId==a.Id&&s.RevokedAt==null));
        Assert.Null(await identity.SignInAsync(a.Email,"Password1",30,default));Assert.NotNull(await identity.SignInAsync(a.Email,"NewPassword1",30,default));
    }
    [Fact]public async Task AdminReviewRetainsReasonAndLegacyIsNotAutomaticallyVerified()
    {
        await using var db=fixture.Create();var admin=new Account{Email=Guid.NewGuid()+"@completion.invalid",NormalizedEmail=Guid.NewGuid().ToString(),DisplayName="Admin",Role=Roles.Administrator,PasswordHash="synthetic"};
        var legacy=new Account{Email=Guid.NewGuid()+"@completion.invalid",NormalizedEmail=Guid.NewGuid().ToString(),DisplayName="Legacy",Role=Roles.Farmer,PasswordHash="synthetic"};db.Users.AddRange(admin,legacy);await db.SaveChangesAsync();Assert.Null(legacy.ApprovalState);
        var service=new AccountWorkflow(db,new PasswordHasher<Account>());var approved=await service.ReviewAsync(legacy.Id,admin.Id,new(1,"Approve","Explicit test approval"),default);Assert.Equal("Approved",approved.ApprovalState);
        await Assert.ThrowsAsync<WorkflowConflict>(()=>service.ReviewAsync(legacy.Id,admin.Id,new(1,"Reject","stale"),default));
        var history=await service.HistoryAsync(legacy.Id,admin.Id,default);Assert.Single(history);Assert.Equal("Explicit test approval",history[0].Reason);
    }
}
