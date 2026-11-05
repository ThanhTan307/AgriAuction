using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Realtime;
using Xunit;

namespace AgriAuction.UnitTests;

public sealed class RoomTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static SimulationBidRules Rules() => new() { StartingPrice = 100, MinimumIncrement = 10, Currency = "VND", PriceBasis = "whole lot simulation", FirstBidRule = "AtLeastStartingPrice", IncrementRule = "MinimumIncrease", TieRule = "FirstCommittedWins", ExtensionRule = "Disabled", ExtensionWindowSeconds = 0, ExtensionSeconds = 0, MaxExtensions = 0, CloseRule = "AtOrAfterDeadline" };
    private static SimulationRoom Open(SimulationBidRules? rules = null)
    {
        var r = SimulationRoom.Configure(Guid.NewGuid(), Guid.NewGuid(), rules ?? Rules(), Now, Now.AddSeconds(60), Now.AddSeconds(-1)); r.Start(Guid.NewGuid(), Now); return r;
    }
    [Fact] public void ExplicitConfigurationRequiredAndNoPolicyDefaults()
    {
        Assert.Throws<RuleViolation>(() => new SimulationBidRules().Validated());
        foreach (var bad in new[] { Rules() with { MinimumIncrement = 0 }, Rules() with { StartingPrice = 0.00001m }, Rules() with { Currency = "" }, Rules() with { PriceBasis = "" }, Rules() with { FirstBidRule = "" }, Rules() with { TieRule = "Random" }, Rules() with { CloseRule = "ClientTimer" }, Rules() with { ExtensionSeconds = null }, Rules() with { ExtensionSeconds = 1 } }) Assert.Throws<RuleViolation>(() => bad.Validated());
    }
    [Fact] public void AdminStartRequiresScheduledTimeAndCannotRepeat()
    {
        var r = SimulationRoom.Configure(Guid.NewGuid(), Guid.NewGuid(), Rules(), Now, Now.AddMinutes(1), Now.AddSeconds(-1));
        Assert.Throws<WorkflowConflict>(() => r.Start(Guid.NewGuid(), Now.AddSeconds(-1))); Assert.Throws<WorkflowConflict>(() => r.Accept(Guid.NewGuid(), 100, Now)); r.Start(Guid.NewGuid(), Now); Assert.Throws<WorkflowConflict>(() => r.Start(Guid.NewGuid(), Now));
    }
    [Theory] [InlineData(-1, false)] [InlineData(0, true)] [InlineData(59, true)] [InlineData(60, false)] [InlineData(61, false)]
    public void InclusiveStartExclusiveDeadline(int seconds, bool allowed)
    {
        var r = Open(); if (allowed) Assert.Equal(100, r.Accept(Guid.NewGuid(), 100, Now.AddSeconds(seconds)).Amount); else Assert.Throws<WorkflowConflict>(() => r.Accept(Guid.NewGuid(), 100, Now.AddSeconds(seconds)));
    }
    [Fact] public void FirstBidIncrementAndTieFollowExplicitRule()
    {
        var r = Open(); Assert.Throws<WorkflowConflict>(() => r.Accept(Guid.NewGuid(), 99, Now)); var first = r.Accept(Guid.NewGuid(), 105, Now); Assert.Equal(3, first.RoomVersion);
        Assert.Throws<WorkflowConflict>(() => r.Accept(Guid.NewGuid(), 105, Now)); Assert.Throws<WorkflowConflict>(() => r.Accept(Guid.NewGuid(), 114, Now));
        var second = r.Accept(Guid.NewGuid(), 115, Now); Assert.Equal(4, second.RoomVersion); Assert.Equal(second.Id, r.LeadingBidId); Assert.Equal(2, r.BidCount);
    }
    [Fact] public void GridRejectsNonMultipleWithoutMutatingLeader()
    {
        var r = Open(Rules() with { IncrementRule = "StartingPriceGrid" }); Assert.Throws<RuleViolation>(() => r.Accept(Guid.NewGuid(), 105, Now)); Assert.Null(r.LeadingBidId); Assert.Equal(0, r.BidCount); r.Accept(Guid.NewGuid(), 110, Now);
    }
    [Fact] public void DisabledExtensionNeverResetsDeadline()
    {
        var r = Open(); r.Accept(Guid.NewGuid(), 100, Now.AddSeconds(59)); Assert.Equal(Now.AddSeconds(60), r.EndsAt); Assert.Equal(0, r.ExtensionCount);
    }
    [Theory] [InlineData("FromBidTime", 65)] [InlineData("AddToDeadline", 70)]
    public void ConfiguredExtensionUsesOnlyAcceptedBidAndRespectsCap(string formula, int end)
    {
        var r = Open(Rules() with { ExtensionRule = formula, ExtensionWindowSeconds = 5, ExtensionSeconds = 10, MaxExtensions = 1 });
        Assert.Throws<WorkflowConflict>(() => r.Accept(Guid.NewGuid(), 1, Now.AddSeconds(55))); Assert.Equal(0, r.ExtensionCount);
        var first = r.Accept(Guid.NewGuid(), 100, Now.AddSeconds(55)); Assert.True(first.Extended); Assert.Equal(Now.AddSeconds(end), r.EndsAt);
        var second = r.Accept(Guid.NewGuid(), 110, r.EndsAt.AddSeconds(-1)); Assert.False(second.Extended); Assert.Equal(1, r.ExtensionCount);
    }
    [Fact] public void NoExtensionOutsideWindowOrAtDeadline()
    {
        var r = Open(Rules() with { ExtensionRule = "AddToDeadline", ExtensionWindowSeconds = 5, ExtensionSeconds = 10, MaxExtensions = 2 });
        Assert.False(r.Accept(Guid.NewGuid(), 100, Now.AddSeconds(54)).Extended); Assert.Throws<WorkflowConflict>(() => r.Accept(Guid.NewGuid(), 110, Now.AddSeconds(60))); Assert.Equal(Now.AddSeconds(60), r.EndsAt);
    }
    [Fact] public void FromBidTimeNeverShortensDeadline()
    {
        var r = Open(Rules() with { ExtensionRule = "FromBidTime", ExtensionWindowSeconds = 10, ExtensionSeconds = 1, MaxExtensions = 2 }); Assert.False(r.Accept(Guid.NewGuid(), 100, Now.AddSeconds(50)).Extended); Assert.Equal(Now.AddSeconds(60), r.EndsAt);
    }
    [Fact] public void ClosingIsDueOnlyIdempotentAndPreventsLateBid()
    {
        var r = Open(); var bid = r.Accept(Guid.NewGuid(), 100, Now); Assert.Null(r.CloseIfDue(Now.AddSeconds(59))); var result = r.CloseIfDue(Now.AddSeconds(60)); Assert.Equal(bid.Id, result!.WinningBidId); Assert.Equal("ClosedWithWinner", r.State);
        Assert.Null(r.CloseIfDue(Now.AddSeconds(61))); Assert.Throws<WorkflowConflict>(() => r.Accept(Guid.NewGuid(), 110, Now.AddSeconds(61)));
    }
    [Fact] public void NoBidProducesNoWinnerWithoutTrade() { var r = Open(); var result = r.CloseIfDue(Now.AddMinutes(1)); Assert.Equal("ClosedNoWinner", r.State); Assert.Null(result!.WinnerId); Assert.Null(result.Amount); }
    [Fact] public void StaleVersionAndInvalidAmountsDoNotChangeState()
    {
        var r = Open(); Assert.Throws<WorkflowConflict>(() => r.Expect(1)); Assert.Throws<RuleViolation>(() => r.Accept(Guid.NewGuid(), -1, Now)); Assert.Throws<RuleViolation>(() => r.Accept(Guid.NewGuid(), 100.00001m, Now)); Assert.Equal(2, r.Version);
    }
}
