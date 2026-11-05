using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;
using Xunit;

namespace AgriAuction.UnitTests;

public sealed class AuctionTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static SimulationTerms Terms() => new() { RegistrationOpensAt = Now.AddMinutes(-1), RegistrationClosesAt = Now.AddHours(1), StartsAt = Now.AddHours(2), EndsAt = Now.AddHours(3), DepositAmount = 123.4567m, Currency = "vnd" };
    public static Lot Lot(bool approved = true)
    {
        var lot = Domain.Lots.Lot.Create(Guid.NewGuid(), new() { CategoryId = Guid.NewGuid(), Title = "Test lot", Description = "Test", Location = "Test", Quantity = 1, QuantityUnit = "box", Weight = 1, WeightUnit = "kg", ProposedStartingPrice = 1, ProposedIncrement = 1, Currency = "VND", PriceBasis = "whole lot proposal", ProposedStartsAt = Now.AddDays(1), ProposedEndsAt = Now.AddDays(2) });
        if (approved) { var revision = lot.Submit(1); lot.Decide(revision.Id, "Approve", "Test approval"); }
        return lot;
    }
    private static Auction Published()
    {
        var auction = Auction.Prepare(Lot(), Guid.NewGuid(), Terms(), Now); auction.Publish(Guid.NewGuid(), Now); return auction;
    }
    [Fact] public void PreparationRequiresApprovedLot() => Assert.Throws<WorkflowConflict>(() => Auction.Prepare(Lot(false), Guid.NewGuid(), Terms(), Now));
    [Fact] public void ExplicitPublishDoesNotOpenLiveAuction()
    {
        var auction = Auction.Prepare(Lot(), Guid.NewGuid(), Terms(), Now); Assert.Equal("Draft", auction.State);
        Assert.Throws<WorkflowConflict>(() => AuctionRegistration.Register(auction, Guid.NewGuid(), Now));
        auction.Publish(Guid.NewGuid(), Now); Assert.Equal("Scheduled", auction.State); Assert.True(auction.IsSimulation); Assert.Equal(2, auction.Version);
        Assert.Throws<WorkflowConflict>(() => auction.Publish(Guid.NewGuid(), Now));
    }
    [Fact] public void ScheduleAndAmountRequireExplicitValidValues()
    {
        foreach (var terms in new[] { Terms() with { RegistrationOpensAt = default }, Terms() with { RegistrationClosesAt = Now.AddHours(4) }, Terms() with { StartsAt = Now.AddHours(3) }, Terms() with { DepositAmount = 0 }, Terms() with { DepositAmount = 0.00001m }, Terms() with { Currency = "" } })
            Assert.Throws<RuleViolation>(() => terms.Validated());
        Assert.Equal("VND", Terms().Validated().Currency);
    }
    [Theory]
    [InlineData(10)] [InlineData(10000)] [InlineData(100000)] [InlineData(10000.25)]
    public void ValidatedDepositAmountPreservesValue(decimal amount) => Assert.Equal(amount, (Terms() with { DepositAmount = amount }).Validated().DepositAmount);
    [Fact] public void ExpiredScheduleCannotBePreparedOrPublished()
    {
        Assert.Throws<RuleViolation>(() => Auction.Prepare(Lot(), Guid.NewGuid(), Terms(), Now.AddDays(1)));
        var auction = Auction.Prepare(Lot(), Guid.NewGuid(), Terms(), Now);
        Assert.Throws<WorkflowConflict>(() => auction.Publish(Guid.NewGuid(), Now.AddDays(1)));
    }
    [Theory]
    [InlineData(-61, false)] [InlineData(-60, true)] [InlineData(0, true)] [InlineData(3599, true)] [InlineData(3600, false)] [InlineData(7200, false)]
    public void RegistrationWindowUsesInclusiveStartExclusiveDeadline(int seconds, bool allowed)
    {
        var auction = Published();
        if (allowed) Assert.Equal("PendingDeposit", AuctionRegistration.Register(auction, Guid.NewGuid(), Now.AddSeconds(seconds)).State);
        else Assert.Throws<WorkflowConflict>(() => AuctionRegistration.Register(auction, Guid.NewGuid(), Now.AddSeconds(seconds)));
    }
    [Fact] public void LotOwnerCannotRegister() { var a = Published(); Assert.Throws<WorkflowConflict>(() => AuctionRegistration.Register(a, a.FarmerId, Now)); }
    [Fact] public void ConfirmationIsSimulationOnlyAndCannotRepeat()
    {
        var r = AuctionRegistration.Register(Published(), Guid.NewGuid(), Now); r.ConfirmSimulation(1, Now);
        Assert.Equal("SimulationEligible", r.State); Assert.Equal(2, r.Version); Assert.Equal(Now, r.EligibleAt);
        Assert.Throws<WorkflowConflict>(() => r.ConfirmSimulation(2, Now));
    }
    [Fact] public void StaleOrMissingVersionDoesNotMutateState()
    {
        var r = AuctionRegistration.Register(Published(), Guid.NewGuid(), Now);
        Assert.Throws<RuleViolation>(() => r.ConfirmSimulation(0, Now)); Assert.Throws<WorkflowConflict>(() => r.ConfirmSimulation(2, Now)); Assert.Equal("PendingDeposit", r.State);
        var auction = Published(); Assert.Throws<WorkflowConflict>(() => auction.Expect(1));
    }
}
