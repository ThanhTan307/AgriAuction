using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Realtime;
using AgriAuction.Domain.Transactions;
using Xunit;

namespace AgriAuction.UnitTests;

public sealed class CompletionTransactionTests
{
    private static readonly DateTimeOffset Now = new(2030,1,1,0,0,0,TimeSpan.Zero);
    private static PostAuctionTransaction Trade(string currency="VND",string basis="whole lot simulation")
    {
        var r=new SimulationBidRules {StartingPrice=100,MinimumIncrement=10,Currency=currency,PriceBasis=basis,FirstBidRule="AtLeastStartingPrice",IncrementRule="MinimumIncrease",TieRule="FirstCommittedWins",ExtensionRule="Disabled",ExtensionWindowSeconds=0,ExtensionSeconds=0,MaxExtensions=0,CloseRule="AtOrAfterDeadline"};
        var room=SimulationRoom.Configure(Guid.NewGuid(),Guid.NewGuid(),r,Now,Now.AddMinutes(1),Now.AddMinutes(-1));room.Start(Guid.NewGuid(),Now);
        var bid=room.Accept(Guid.NewGuid(),123.4567m,Now);var result=room.CloseIfDue(Now.AddMinutes(1))!;
        return PostAuctionTransaction.FromResult(room,result,bid,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Synthetic","{}",Now.AddMinutes(2));
    }
    [Theory][InlineData("USD","whole lot simulation")][InlineData("VND","per kg")]
    public void ProfileNeverConvertsCurrencyOrPriceBasis(string currency,string basis)
        => Assert.Throws<WorkflowConflict>(()=>Trade(currency,basis).ActivateCompletion("D35-Simulation-v1"));
    [Fact] public void PaymentDoesNotCompleteOrChangeWinnerPrice()
    {
        var t=Trade();t.ActivateCompletion("D35-Simulation-v1");t.Apply("RequestPayment",t.EnterpriseId,"Enterprise",1,Now.AddMinutes(3));t.Apply("ConfirmPayment",Guid.NewGuid(),"Administrator",2,Now.AddMinutes(4));
        Assert.Equal(123.4567m,t.WinningPrice);Assert.Null(t.CompletedAt);Assert.Equal("InProgress",t.State);Assert.Equal("AwaitingDelivery",t.DeliveryState);
    }
    [Fact] public void CompletionNeedsPaymentReceiptAndFarmerAndCannotRewriteTimestamp()
    {
        var t=Trade();t.ActivateCompletion("D35-Simulation-v1");t.Apply("Deliver",t.FarmerId,"Farmer",1,Now.AddMinutes(3));t.Apply("Receive",t.EnterpriseId,"Enterprise",2,Now.AddMinutes(4));
        Assert.Throws<WorkflowConflict>(()=>t.Apply("Complete",t.FarmerId,"Farmer",3,Now.AddMinutes(5)));
        t.Apply("RequestPayment",t.EnterpriseId,"Enterprise",3,Now.AddMinutes(5));t.Apply("ConfirmPayment",Guid.NewGuid(),"Administrator",4,Now.AddMinutes(6));
        Assert.Throws<WorkflowConflict>(()=>t.Apply("Complete",Guid.NewGuid(),"Administrator",5,Now.AddMinutes(7)));
        t.Apply("Complete",t.FarmerId,"Farmer",5,Now.AddMinutes(7));Assert.Throws<WorkflowConflict>(()=>t.Apply("Complete",t.FarmerId,"Farmer",6,Now.AddMinutes(8)));Assert.Equal(Now.AddMinutes(7),t.CompletedAt);
    }
}
