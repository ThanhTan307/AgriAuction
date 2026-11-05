using AgriAuction.Domain.Disputes;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;
using Xunit;

namespace AgriAuction.UnitTests;

public sealed class ComplaintTests
{
    private static readonly Guid Farmer = Guid.NewGuid(), Enterprise = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static Complaint Case(string scope = "TransactionRecord", int images = 1) => Complaint.Create(Guid.NewGuid(), Farmer, Enterprise, scope, "Lô thử", "Lý do", "Mô tả", images, Now);
    [Theory] [InlineData("AuctionRecord")] [InlineData("TransactionRecord")]
    public void NewCaseDoesNotPresumePaymentOrDelivery(string scope) { var c = Case(scope); Assert.Equal("Simulation", c.Mode); Assert.Equal("Open", c.State); Assert.Null(c.Outcome); Assert.Equal(1, c.Version); }
    [Theory] [InlineData(0)] [InlineData(4)]
    public void EvidenceResourceBounds(int count) => Assert.Throws<RuleViolation>(() => Case(images: count));
    [Theory] [InlineData("")] [InlineData(" ")] [InlineData("\u0000")]
    public void RejectEmptyAndControlText(string text) => Assert.Throws<RuleViolation>(() => Complaint.Text(text, "text", 4000));
    [Fact] public void RejectLongTextAndUnknownScope() { Assert.Throws<RuleViolation>(() => Complaint.Text(new string('x', 201), "reason", 200)); Assert.Throws<RuleViolation>(() => Case("Paid")); }
    [Fact] public void RequireDistinctParties() => Assert.Throws<WorkflowConflict>(() => Complaint.Create(Guid.NewGuid(), Farmer, Farmer, "AuctionRecord", "title", "reason", "text", 1, Now));
    [Fact] public void BothPartiesAppendWithVersion() { var c = Case(); c.Respond(Farmer, 1, 1, Now.AddSeconds(1)); c.Respond(Enterprise, 2, 1, Now.AddSeconds(2)); Assert.Equal(3, c.Version); Assert.Equal("Open", c.State); }
    [Fact] public void StaleVersionAndOutsiderCannotAdvance() { var c = Case(); Assert.Throws<WorkflowConflict>(() => c.Respond(Farmer, 0, 1, Now)); Assert.Throws<WorkflowConflict>(() => c.Respond(Guid.NewGuid(), 1, 1, Now)); Assert.Equal(1, c.Version); }
    [Fact] public void ExplicitReviewRequired() { var c = Case(); Assert.Throws<WorkflowConflict>(() => c.Decide(1, "Accept", true, Now)); c.StartReview(1, Now); Assert.Throws<WorkflowConflict>(() => c.StartReview(2, Now)); }
    [Theory] [InlineData("Accept")] [InlineData("Reject")]
    public void DecisionClosesNarrativeWithoutSanctions(string outcome) { var c = Case(); c.StartReview(1, Now); c.Comment(2, Now); c.Decide(3, outcome, true, Now); Assert.Equal("DecisionRecorded", c.State); Assert.Throws<WorkflowConflict>(() => c.Respond(Farmer, 4, 1, Now)); Assert.Throws<WorkflowConflict>(() => c.Comment(4, Now)); }
    [Fact] public void DecisionRequiresExplicitSimulationAndKnownOutcome() { var c = Case(); c.StartReview(1, Now); Assert.Throws<RuleViolation>(() => c.Decide(2, "Accept", false, Now)); Assert.Throws<RuleViolation>(() => c.Decide(2, "Refund", true, Now)); Assert.Equal(2, c.Version); }
    [Fact] public void MediationIsPendingAndAllowsRepliesWithoutInventingCompletion() { var c = Case(); c.StartReview(1, Now); c.Decide(2, "Mediate", true, Now); Assert.Equal("MediationPending", c.State); c.Respond(Enterprise, 3, 1, Now); c.Comment(4, Now); Assert.Throws<WorkflowConflict>(() => c.Decide(5, "Accept", true, Now)); }
}
