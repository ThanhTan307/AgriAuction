using AgriAuction.Application.Transactions;
using AgriAuction.Application.Disputes;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Reporting;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Realtime;
using AgriAuction.Infrastructure.Persistence;
using AgriAuction.Infrastructure.Transactions;
using AgriAuction.Infrastructure.Disputes;
using AgriAuction.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgriAuction.IntegrationTests;

// Opt-in independent Completion fixture: never touches the legacy test database.
public sealed class CompletionPaymentTests(CompletionPostgresFixture fixture) : IClassFixture<CompletionPostgresFixture>
{
    private async Task<(Guid Id, Guid Farmer, Guid Buyer, Guid Admin)> Seed(bool legacy = false, bool winner = true, int bidCount = 1)
    {
        await using var db = fixture.Create();
        var accounts = new[] { Roles.Farmer, Roles.Enterprise, Roles.Administrator }.Select(r => new Account {
            Email = Guid.NewGuid()+"@completion.invalid", NormalizedEmail=Guid.NewGuid().ToString(), DisplayName=r, Role=r, PasswordHash="synthetic-not-login" }).ToArray();
        db.Users.AddRange(accounts); var farmer=accounts[0].Id; var buyer=accounts[1].Id; var admin=accounts[2].Id;
        var cat=Category.Create("C"+Guid.NewGuid().ToString("N")[..20],"Completion synthetic",true);db.Categories.Add(cat);
        var now=DateTimeOffset.UtcNow;
        var lot=Lot.Create(farmer,new() { CategoryId=cat.Id,Title="Completion synthetic winner",Description="Synthetic integration data",Location="Test",Quantity=10,QuantityUnit="box",Weight=100,WeightUnit="kg",ProposedStartingPrice=100,ProposedIncrement=10,Currency="VND",PriceBasis=CompletionProfile.PriceBasis,ProposedStartsAt=now.AddDays(1),ProposedEndsAt=now.AddDays(2) });
        db.Lots.Add(lot);await db.SaveChangesAsync();
        var image=new LotImage { LotId=lot.Id,UploadedBy=farmer,Data=[1],Width=1,Height=1,Sha256="SYNTHETIC" };
        var rev=lot.Submit(1);lot.Decide(rev.Id,"Approve","Synthetic approval");db.LotImages.Add(image);db.LotRevisions.Add(rev);
        db.RevisionImages.Add(new() { RevisionId=rev.Id,ImageId=image.Id });db.LotReviews.Add(new() { LotId=lot.Id,RevisionId=rev.Id,AdminId=admin,Outcome="Approve",Reason="Synthetic approval" });
        var terms=new SimulationTerms { RegistrationOpensAt=now.AddHours(-4),RegistrationClosesAt=now.AddHours(-2),StartsAt=now.AddHours(-1),EndsAt=now.AddMinutes(-2),DepositAmount=20,Currency="VND" };
        var auction=Auction.Prepare(lot,admin,terms,now.AddHours(-3));if(!legacy)auction.UseCompletionProfile(CompletionProfile.Version);auction.Publish(admin,now.AddHours(-3));db.Auctions.Add(auction);
        var rules=new SimulationBidRules { StartingPrice=100,MinimumIncrement=10,Currency="VND",PriceBasis=CompletionProfile.PriceBasis,FirstBidRule="AtLeastStartingPrice",IncrementRule="MinimumIncrease",TieRule="FirstCommittedWins",ExtensionRule="Disabled",ExtensionWindowSeconds=0,ExtensionSeconds=0,MaxExtensions=0,CloseRule="AtOrAfterDeadline" };
        var room=SimulationRoom.Configure(auction.Id,admin,rules,terms.StartsAt,terms.EndsAt,now.AddHours(-2));room.Start(admin,terms.StartsAt);
        db.SimulationRooms.Add(room);await db.SaveChangesAsync();
        if(winner){for(var i=0;i<bidCount;i++){var bid=room.Accept(buyer,123.4567m+i*10,terms.EndsAt.AddSeconds(-1));db.SimulationBids.Add(bid);await db.SaveChangesAsync();}}
        var result=room.CloseIfDue(terms.EndsAt)!;db.SimulationResults.Add(result);
        db.PostAuctionEvents.Add(new() { RoomId=room.Id,CreatedAt=result.ClosedAt,NextAttemptAt=result.ClosedAt });await db.SaveChangesAsync();
        await new PostAuctionProcessor(db).ProcessAsync(default);
        if(!winner)return(auction.Id,farmer,buyer,admin);
        var id=await db.PostAuctionTransactions.Where(t=>t.RoomId==room.Id).Select(t=>t.Id).SingleAsync();return(id,farmer,buyer,admin);
    }
    private async Task<TransactionReceipt> Act(Guid id,string action,long version,Guid actor,string role,Guid? key=null,string? note=null)
    {
        await using var db=fixture.Create();return await new TransactionLifecycle(db,new(true)).ExecuteAsync(id,action,new(version,note),key??Guid.NewGuid(),actor,role,default);
    }
    private async Task<Guid> Deposit(Guid tradeId,Guid buyer,Guid admin,bool noWinner=false)
    {
        await using var db=fixture.Create();var auctionId=noWinner?tradeId:await db.PostAuctionTransactions.Where(t=>t.Id==tradeId).Select(t=>t.AuctionId).SingleAsync();var a=await db.Auctions.SingleAsync(a=>a.Id==auctionId);
        var at=a.Terms.RegistrationClosesAt.AddMinutes(-1);var r=AuctionRegistration.Register(a,buyer,at);r.ConfirmSimulation(1,at);db.AuctionRegistrations.Add(r);
        var d=new SimulationDeposit{RegistrationId=r.Id,Amount=a.Terms.DepositAmount,Currency="VND",State="SimulatedHeld",CreatedAt=at,ConfirmedAt=at,ConfirmedBy=admin};db.SimulationDeposits.Add(d);db.SimulationLedger.Add(new(){DepositId=d.Id,ActorId=admin,Amount=d.Amount,Currency=d.Currency,Reference="SIM-"+d.Id,OccurredAt=at});await db.SaveChangesAsync();return d.Id;
    }
    private async Task<DepositDispositionReceipt> Settle(Guid id,string action,Guid admin,Guid? key=null,DepositDispositionRequest? request=null)
    {
        await using var db=fixture.Create();return await new DepositSettlementService(db,new(true)).ExecuteAsync(id,action,request??new(),key??Guid.NewGuid(),admin,default);
    }
    private async Task Complete((Guid Id,Guid Farmer,Guid Buyer,Guid Admin) s)
    {await Act(s.Id,"RequestPayment",1,s.Buyer,Roles.Enterprise);await Act(s.Id,"ConfirmPayment",2,s.Admin,Roles.Administrator);await Act(s.Id,"Deliver",3,s.Farmer,Roles.Farmer);await Act(s.Id,"Receive",4,s.Buyer,Roles.Enterprise);await Act(s.Id,"Complete",5,s.Farmer,Roles.Farmer);}
    [Fact] public async Task CursorReadsAll101BidsWithoutDuplicatesAndKeepsAliasesAndScope()
    {
        var s=await Seed(bidCount:101);await using var db=fixture.Create();
        var auctionId=await db.PostAuctionTransactions.Where(t=>t.Id==s.Id).Select(t=>t.AuctionId).SingleAsync();
        var rooms=new AgriAuction.Infrastructure.Realtime.RoomService(db,new(true));
        var snapshot=await rooms.SnapshotAsync(auctionId,s.Farmer,Roles.Farmer,default);Assert.Equal(100,snapshot.Bids.Count);
        var first=await rooms.BidsAsync(auctionId,s.Farmer,Roles.Farmer,null,100,default);Assert.Equal(100,first.Items.Count);Assert.NotNull(first.NextBeforeVersion);
        var last=await rooms.BidsAsync(auctionId,s.Farmer,Roles.Farmer,long.Parse(first.NextBeforeVersion!),100,default);Assert.Single(last.Items);Assert.Null(last.NextBeforeVersion);
        Assert.Equal(101,first.Items.Concat(last.Items).Select(b=>b.Id).Distinct().Count());Assert.Equal(snapshot.Bids[0].Bidder,first.Items[0].Bidder);Assert.All(last.Items,b=>Assert.Equal(first.Items[0].Bidder,b.Bidder));
        await Assert.ThrowsAsync<PermissionDenied>(()=>rooms.BidsAsync(auctionId,Guid.NewGuid(),Roles.Farmer,null,100,default));
    }
    [Fact]public async Task CompletionComplaintNeedsCompletedAndCatalogAndPersistsDeadlines()
    {
        var s=await Seed();await using var db=fixture.Create();var service=new ComplaintService(db,new TransactionService(db));
        var reason=new WorkflowCatalogItem{Kind="ComplaintReason",Name="Synthetic reason"};db.WorkflowCatalogItems.Add(reason);await db.SaveChangesAsync();
        var request=new CreateComplaintRequest(s.Id,"TransactionRecord",reason.Name,"Synthetic complaint",reason.Id);IReadOnlyList<ImagePayload> images=[new(LotApiTests.Photo(),24,16)];
        await Assert.ThrowsAsync<WorkflowConflict>(()=>service.CreateAsync(request,images,Guid.NewGuid(),s.Buyer,Roles.Enterprise,default));db.ChangeTracker.Clear();
        await Complete(s);var receipt=await service.CreateAsync(request,images,Guid.NewGuid(),s.Buyer,Roles.Enterprise,default);
        var c=await db.Complaints.AsNoTracking().SingleAsync(c=>c.Id==receipt.ComplaintId);Assert.Equal(c.CreatedAt.AddDays(3),c.ResponseDueAt);Assert.NotNull(c.ComplaintDeadline);Assert.Equal("Open",(await db.PostAuctionTransactions.AsNoTracking().SingleAsync(t=>t.Id==s.Id)).DisputeState);
        await Assert.ThrowsAsync<WorkflowConflict>(()=>service.CreateAsync(request,images,Guid.NewGuid(),s.Buyer,Roles.Enterprise,default));
    }
    [Fact]public async Task CompletedRevenueIsWholeLotOwnerScopedAndReportDoesNotWrite()
    {
        var s=await Seed();await Complete(s);await using var db=fixture.Create();var before=await db.AuditLogs.LongCountAsync();var date=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var report=await new CompletionReportService(db).GetAsync(new(date.AddDays(-1),date.AddDays(1),"Asia/Ho_Chi_Minh"),s.Farmer,Roles.Farmer,default);
        Assert.Equal(1,report.CompletedInPeriod);Assert.Equal(1,report.WinningAuctions);Assert.Equal(100,report.SuccessRatePercent);Assert.Equal(123.4567m,decimal.Parse(report.SimulatedRevenue,System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(before,await db.AuditLogs.LongCountAsync());
        await Assert.ThrowsAsync<PermissionDenied>(()=>new CompletionReportService(db).GetAsync(new(date,date,"Asia/Ho_Chi_Minh"),s.Buyer,Roles.Enterprise,default));
    }
    [Fact]public async Task ConcurrentCompletionComplaintsCreateOneActiveCaseAndDecisionRollbackIsAtomic()
    {
        var s=await Seed();await Complete(s);Guid reasonId;
        await using(var db=fixture.Create()){var reason=new WorkflowCatalogItem{Kind="ComplaintReason",Name="Synthetic concurrent reason"};db.WorkflowCatalogItems.Add(reason);await db.SaveChangesAsync();reasonId=reason.Id;}
        async Task<ComplaintReceipt?> Create(Guid actor,string role){await using var db=fixture.Create();try{return await new ComplaintService(db,new TransactionService(db)).CreateAsync(new(s.Id,"TransactionRecord","Synthetic concurrent reason","Synthetic concurrent complaint",reasonId),[new(LotApiTests.Photo(),24,16)],Guid.NewGuid(),actor,role,default);}catch(WorkflowConflict e)when(e.Code=="ACTIVE_COMPLAINT_EXISTS"){return null;}}
        var cases=await Task.WhenAll(Create(s.Buyer,Roles.Enterprise),Create(s.Farmer,Roles.Farmer));var receipt=Assert.Single(cases,r=>r is not null)!;
        await using(var db=fixture.Create()){await new ComplaintService(db,new TransactionService(db)).ReviewAsync(receipt.ComplaintId,new(1,"Synthetic review"),Guid.NewGuid(),s.Admin,Roles.Administrator,default);var trigger="decisionfault_"+Guid.NewGuid().ToString("N");var sql=$"CREATE FUNCTION {trigger}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.transaction_id='{s.Id}'::uuid AND NEW.kind='DisputeDecisionRecorded' THEN RAISE EXCEPTION 'synthetic dispute history fault'; END IF; RETURN NEW; END $$; CREATE TRIGGER {trigger} BEFORE INSERT ON post_auction_history FOR EACH ROW EXECUTE FUNCTION {trigger}();";await db.Database.ExecuteSqlRawAsync(sql);}
        await using(var db=fixture.Create()){await Assert.ThrowsAsync<DbUpdateException>(()=>new ComplaintService(db,new TransactionService(db)).DecideAsync(receipt.ComplaintId,new(2,"Accept","Synthetic grounded decision",true),Guid.NewGuid(),s.Admin,Roles.Administrator,default));}
        await using(var db=fixture.Create()){var row=await db.Complaints.AsNoTracking().SingleAsync(c=>c.Id==receipt.ComplaintId);Assert.Equal("UnderReview",row.State);Assert.Equal(2,row.Version);Assert.Equal("Open",(await db.PostAuctionTransactions.SingleAsync(t=>t.Id==s.Id)).DisputeState);Assert.False(await db.ComplaintEntries.AnyAsync(e=>e.ComplaintId==row.Id&&e.Kind=="DecisionRecorded"));}
    }
    [Fact]public async Task SyntheticExpiredResponseMovesToAdminQueueOnceUsingDatabaseClock()
    {
        var s=await Seed();await Complete(s);await using var db=fixture.Create();var reason=new WorkflowCatalogItem{Kind="ComplaintReason",Name="Synthetic expiry reason"};db.WorkflowCatalogItems.Add(reason);await db.SaveChangesAsync();
        var receipt=await new ComplaintService(db,new TransactionService(db)).CreateAsync(new(s.Id,"TransactionRecord",reason.Name,"Synthetic expiry complaint",reason.Id),[new(LotApiTests.Photo(),24,16)],Guid.NewGuid(),s.Buyer,Roles.Enterprise,default);
        // Explicit synthetic test clock fixture. No legacy or real transaction timestamp is changed.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE complaints SET created_at=created_at-interval '4 days',updated_at=updated_at-interval '4 days',response_due_at=response_due_at-interval '4 days' WHERE id={receipt.ComplaintId}");db.ChangeTracker.Clear();
        await new CompletionMaintenance(db).AdvanceDeadlinesAsync(default);db.ChangeTracker.Clear();await new CompletionMaintenance(db).AdvanceDeadlinesAsync(default);
        var row=await db.Complaints.AsNoTracking().SingleAsync(c=>c.Id==receipt.ComplaintId);Assert.Equal("UnderReview",row.State);Assert.Equal(2,row.Version);Assert.Single(await db.ComplaintEntries.Where(e=>e.ComplaintId==row.Id&&e.Kind=="ResponseDeadlineExpired"&&e.ActorId==null).ToListAsync());
    }
    [Fact] public async Task NoWinnerRefundsHeldDepositWithoutInventingTrade()
    {
        var s=await Seed(winner:false);var id=await Deposit(s.Id,s.Buyer,s.Admin,true);var r=await Settle(id,"Refund",s.Admin);Assert.Equal(20,r.Amount);
        await using var db=fixture.Create();Assert.False(await db.PostAuctionTransactions.AnyAsync(t=>t.AuctionId==s.Id));Assert.Single(await db.DepositSettlements.Where(x=>x.DepositId==id).ToListAsync());
    }
    [Fact] public async Task WinnerRefundRequiresCompletedAndDoesNotDeductFromPrice()
    {
        var s=await Seed();var id=await Deposit(s.Id,s.Buyer,s.Admin);await Assert.ThrowsAsync<WorkflowConflict>(()=>Settle(id,"Refund",s.Admin));
        await Act(s.Id,"RequestPayment",1,s.Buyer,Roles.Enterprise);await Act(s.Id,"ConfirmPayment",2,s.Admin,Roles.Administrator);await Act(s.Id,"Deliver",3,s.Farmer,Roles.Farmer);await Act(s.Id,"Receive",4,s.Buyer,Roles.Enterprise);await Act(s.Id,"Complete",5,s.Farmer,Roles.Farmer);
        var key=Guid.NewGuid();var r=await Settle(id,"Refund",s.Admin,key);Assert.Equal(20,r.Amount);Assert.Equal(r,await Settle(id,"Refund",s.Admin,key));
        await using(var read=fixture.Create()){
            var registrationId=await read.SimulationDeposits.Where(d=>d.Id==id).Select(d=>d.RegistrationId).SingleAsync();
            var registration=await new AgriAuction.Infrastructure.Auctions.AuctionService(read,new AgriAuction.Infrastructure.Auctions.SimulationDepositProvider(),new(true)).RegistrationAsync(registrationId,s.Buyer,false,default);
            Assert.Equal("SimulatedHeld",registration.Deposit.State);Assert.Equal("SimulatedRefunded",registration.Deposit.SettlementKind);Assert.NotNull(registration.Deposit.SettledAt);
            await Assert.ThrowsAsync<AgriAuction.Application.Catalog.ResourceMissing>(()=>new AgriAuction.Infrastructure.Auctions.AuctionService(read,new AgriAuction.Infrastructure.Auctions.SimulationDepositProvider(),new(true)).RegistrationAsync(registrationId,s.Farmer,false,default));
        }
        await using var db=fixture.Create();Assert.Equal(123.4567m,(await db.PostAuctionTransactions.SingleAsync(t=>t.Id==s.Id)).WinningPrice);Assert.Single(await db.SimulationLedger.Where(l=>l.DepositId==id).ToListAsync());Assert.Single(await db.DepositSettlements.Where(l=>l.DepositId==id).ToListAsync());
    }
    [Fact] public async Task RefundAndForfeitRaceSettlesOnlyOnceWithDecisionBasis()
    {
        var s=await Seed();await using var db=fixture.Create();var loser=new Account{Email=Guid.NewGuid()+"@completion.invalid",NormalizedEmail=Guid.NewGuid().ToString(),DisplayName="Loser",Role=Roles.Enterprise,PasswordHash="synthetic"};db.Users.Add(loser);await db.SaveChangesAsync();
        var id=await Deposit(s.Id,loser.Id,s.Admin);await Assert.ThrowsAsync<WorkflowConflict>(()=>Settle(id,"Forfeit",s.Admin));
        var decision=await Settle(id,"RecordViolation",s.Admin,request:new("Recorded synthetic violation","Synthetic evidence reference: fixture"));
        async Task<bool> Attempt(string action){try{await Settle(id,action,s.Admin,request:action=="Forfeit"?new(DecisionId:decision.RecordId):null);return true;}catch(WorkflowConflict){return false;}}
        var results=await Task.WhenAll(Attempt("Refund"),Attempt("Forfeit"));Assert.Single(results,x=>x);Assert.Single(await db.DepositSettlements.AsNoTracking().Where(l=>l.DepositId==id).ToListAsync());
    }
    [Fact] public async Task WinnerPriceIsWholeLotAndPaymentThenDeliveryCompleteExactlyOnce()
    {
        var s=await Seed();var a=await Act(s.Id,"RequestPayment",1,s.Buyer,Roles.Enterprise);Assert.Equal(123.4567m,a.Amount);Assert.Equal("AwaitingConfirmation",a.PaymentState);
        var b=await Act(s.Id,"ConfirmPayment",2,s.Admin,Roles.Administrator);Assert.Equal("SimulatedConfirmed",b.PaymentState);
        await Act(s.Id,"Deliver",3,s.Farmer,Roles.Farmer);await Act(s.Id,"Receive",4,s.Buyer,Roles.Enterprise);
        var key=Guid.NewGuid();var done=await Act(s.Id,"Complete",5,s.Farmer,Roles.Farmer,key);var replay=await Act(s.Id,"Complete",5,s.Farmer,Roles.Farmer,key);
        Assert.Equal(done,replay);Assert.NotNull(done.CompletedAt);Assert.Equal("Completed",done.State);
        await using var db=fixture.Create();Assert.Equal(5,await db.TransactionCommands.CountAsync(c=>c.TransactionId==s.Id));Assert.Equal(6,await db.TransactionHistory.CountAsync(c=>c.TransactionId==s.Id));Assert.Equal(15,await db.Notifications.CountAsync(c=>c.TransactionId==s.Id&&c.Kind=="TransactionActionRecorded"&&(c.RecipientId==s.Farmer||c.RecipientId==s.Buyer||c.RecipientId==s.Admin)));
    }
    [Fact] public async Task SameKeyConcurrentRetryHasOneReceiptAndDifferentPayloadConflicts()
    {
        var s=await Seed();var key=Guid.NewGuid();var replies=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>Act(s.Id,"RequestPayment",1,s.Buyer,Roles.Enterprise,key)));
        Assert.All(replies,r=>Assert.Equal(replies[0],r));await Assert.ThrowsAsync<WorkflowConflict>(()=>Act(s.Id,"RequestPayment",2,s.Buyer,Roles.Enterprise,key));
    }
    [Fact] public async Task DifferentKeyConcurrentConfirmationsCannotConfirmTwice()
    {
        var s=await Seed();await Act(s.Id,"RequestPayment",1,s.Buyer,Roles.Enterprise);
        async Task<bool> Attempt(){try{await Act(s.Id,"ConfirmPayment",2,s.Admin,Roles.Administrator);return true;}catch(WorkflowConflict){return false;}}
        var results=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>Attempt()));Assert.Single(results,x=>x);
    }
    [Fact] public async Task RoleOwnerAndPaymentPreconditionsAreEnforced()
    {
        var s=await Seed();await Assert.ThrowsAsync<AgriAuction.Application.Catalog.ResourceMissing>(()=>Act(s.Id,"RequestPayment",1,Guid.NewGuid(),Roles.Enterprise));
        await Assert.ThrowsAsync<WorkflowConflict>(()=>Act(s.Id,"ConfirmPayment",1,s.Buyer,Roles.Enterprise));
        await Assert.ThrowsAsync<WorkflowConflict>(()=>Act(s.Id,"Complete",1,s.Farmer,Roles.Farmer));
        await Act(s.Id,"RequestPayment",1,s.Buyer,Roles.Enterprise);await Assert.ThrowsAsync<WorkflowConflict>(()=>Act(s.Id,"RequestPayment",2,s.Buyer,Roles.Enterprise));
    }
    [Fact] public async Task LegacyNeverActivatesAndIssueBlocksCompletion()
    {
        var old=await Seed(true);await Assert.ThrowsAsync<WorkflowConflict>(()=>Act(old.Id,"RequestPayment",1,old.Buyer,Roles.Enterprise));
        var s=await Seed();await Act(s.Id,"ReportDeliveryIssue",1,s.Buyer,Roles.Enterprise,note:"Hàng có vấn đề");await Assert.ThrowsAsync<WorkflowConflict>(()=>Act(s.Id,"Complete",2,s.Farmer,Roles.Farmer));
    }
    [Fact] public async Task AuditFaultRollsBackTradeHistoryReceiptAndInbox()
    {
        var s=await Seed();await using var db=fixture.Create();var name="fault_"+Guid.NewGuid().ToString("N");
        var sql=$"CREATE FUNCTION {name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.entity_id = '{s.Id}'::uuid AND NEW.action = 'TransactionRequestPayment' THEN RAISE EXCEPTION 'synthetic audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {name} BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION {name}();";
        await db.Database.ExecuteSqlRawAsync(sql);await Assert.ThrowsAsync<DbUpdateException>(()=>Act(s.Id,"RequestPayment",1,s.Buyer,Roles.Enterprise));
        var t=await db.PostAuctionTransactions.AsNoTracking().SingleAsync(x=>x.Id==s.Id);Assert.Equal(1,t.Version);Assert.Equal("AwaitingRequest",t.PaymentState);Assert.False(await db.TransactionCommands.AnyAsync(x=>x.TransactionId==s.Id));Assert.Single(await db.TransactionHistory.Where(x=>x.TransactionId==s.Id).ToListAsync());Assert.False(await db.Notifications.AnyAsync(x=>x.TransactionId==s.Id&&x.Kind=="TransactionActionRecorded"));
    }
}
