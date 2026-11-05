using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Notifications;
using AgriAuction.Domain.Realtime;
using AgriAuction.Domain.Transactions;
using Xunit;

namespace AgriAuction.UnitTests;

public sealed class TransactionTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static (SimulationRoom Room, SimulationBid Bid, SimulationResult Result) Closed()
    {
        var rules = new SimulationBidRules { StartingPrice = 100, MinimumIncrement = 10, Currency = "VND", PriceBasis = "per kg simulation", FirstBidRule = "AtLeastStartingPrice", IncrementRule = "MinimumIncrease", TieRule = "FirstCommittedWins", ExtensionRule = "Disabled", ExtensionWindowSeconds = 0, ExtensionSeconds = 0, MaxExtensions = 0, CloseRule = "AtOrAfterDeadline" };
        var room = SimulationRoom.Configure(Guid.NewGuid(), Guid.NewGuid(), rules, Now, Now.AddMinutes(1), Now.AddSeconds(-1));
        room.Start(Guid.NewGuid(), Now); var bid = room.Accept(Guid.NewGuid(), 123.4567m, Now); var result = room.CloseIfDue(Now.AddMinutes(1))!; return (room, bid, result);
    }
    [Fact] public void ValidResultPreservesPriceBasisAndLocksUnconfirmedWorkflows()
    {
        var (r, b, s) = Closed(); var t = PostAuctionTransaction.FromResult(r, s, b, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Test", "{}", Now.AddMinutes(2));
        Assert.Equal(123.4567m, t.WinningPrice); Assert.Equal("per kg simulation", t.PriceBasis); Assert.Equal("Simulation", t.Mode);
        Assert.Equal("AwaitingPolicy", t.State); Assert.Equal("PolicyLocked", t.PaymentState); Assert.Equal("PolicyLocked", t.DeliveryState);
        Assert.Equal(s.WinningBidId, t.WinningBidId); Assert.Equal(s.RoomVersion, t.ResultVersion);
    }
    [Theory]
    [InlineData("room")][InlineData("version")][InlineData("winner")][InlineData("amount")][InlineData("bid")][InlineData("mode")][InlineData("unclosed")]
    public void InvalidOrUnclosedResultCannotCreateTransaction(string change)
    {
        var (r, b, s) = Closed();
        switch (change) { case "room": s.RoomId = Guid.NewGuid(); break; case "version": s.RoomVersion++; break; case "winner": s.WinnerId = Guid.NewGuid(); break; case "amount": s.Amount++; break; case "bid": b.Id = Guid.NewGuid(); break; case "mode": s.Mode = "Real"; break; case "unclosed": s.ClosedAt = Now; break; }
        Assert.Throws<WorkflowConflict>(() => PostAuctionTransaction.FromResult(r, s, b, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Test", "{}", Now.AddMinutes(2)));
    }
    [Fact] public void OwnerCannotBeRecordedAsWinningBuyer()
    {
        var (r, b, s) = Closed(); Assert.Throws<WorkflowConflict>(() => PostAuctionTransaction.FromResult(r, s, b, Guid.NewGuid(), Guid.NewGuid(), b.EnterpriseId, "Test", "{}", Now.AddMinutes(2)));
    }
    [Fact] public void ReadReplayKeepsFirstTimestamp()
    {
        var n = new Notification(); Assert.Null(n.ReadAt); n.MarkRead(Now); n.MarkRead(Now.AddDays(1)); Assert.Equal(Now, n.ReadAt);
    }
}
