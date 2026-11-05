using AgriAuction.Domain.Disputes;
using AgriAuction.Domain.Lots;
using Xunit;
namespace AgriAuction.UnitTests;
public sealed class CompletionComplaintTests
{
    private static readonly DateTimeOffset Now=new(2030,1,10,0,0,0,TimeSpan.Zero);
    [Fact]public void SevenDayBoundaryAndServerResponseDeadlineAreExplicit()
    {
        var c=Complaint.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"TransactionRecord","Synthetic","Reason","Description",1,Now);c.UseCompletionPolicy(Guid.NewGuid(),Now.AddDays(-7),Now);Assert.Equal(Now,c.ComplaintDeadline);Assert.Equal(Now.AddDays(3),c.ResponseDueAt);
        Assert.Throws<WorkflowConflict>(()=>c.UseCompletionPolicy(Guid.NewGuid(),Now.AddDays(-7).AddTicks(-1),Now));
    }
    [Fact]public void ExpiryAdvancesOnlyUnansweredCasesAndRespondentCannotReplyLate()
    {
        var respondent=Guid.NewGuid();var c=Complaint.Create(Guid.NewGuid(),Guid.NewGuid(),respondent,"TransactionRecord","Synthetic","Reason","Description",1,Now);c.UseCompletionPolicy(Guid.NewGuid(),Now,Now);
        Assert.Throws<WorkflowConflict>(()=>c.ExpireResponse(Now.AddDays(3).AddTicks(-1)));Assert.Throws<WorkflowConflict>(()=>c.Respond(respondent,1,1,Now.AddDays(3)));c.ExpireResponse(Now.AddDays(3));Assert.Equal("UnderReview",c.State);Assert.Equal(2,c.Version);
    }
}
