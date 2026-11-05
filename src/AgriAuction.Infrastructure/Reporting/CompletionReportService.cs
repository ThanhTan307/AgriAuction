using AgriAuction.Application.Reporting;
using AgriAuction.Application.Lots;
using AgriAuction.Domain.Reporting;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AgriAuction.Infrastructure.Reporting;
public sealed class CompletionReportService(AppDbContext db):ICompletionReportService
{
    public async Task<CompletionReport> GetAsync(ReportQuery query,Guid actor,string role,CancellationToken ct)
    {
        var p=ReportPeriod.Create(query.From,query.Through,query.TimeZone);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='5s'",ct);
        if(role is not ("Farmer" or "Administrator")||!await db.Users.AnyAsync(u=>u.Id==actor&&u.Role==role&&u.IsEnabled,ct))throw new PermissionDenied();
        var trades=db.PostAuctionTransactions.AsNoTracking().Where(t=>t.PolicyProfile=="D35-Simulation-v1"&&(role=="Administrator"||t.FarmerId==actor));
        var completed=trades.Where(t=>t.State=="Completed"&&t.CompletedAt>=p.FromUtc&&t.CompletedAt<p.ToUtc);
        var total=await completed.GroupBy(t=>t.Currency).Select(g=>g.Sum(t=>t.WinningPrice).ToString()).SingleOrDefaultAsync(ct)??"0";
        var completedCount=await completed.LongCountAsync(ct);
        var winners=db.SimulationResults.AsNoTracking().Where(r=>r.WinnerId!=null&&r.ClosedAt>=p.FromUtc&&r.ClosedAt<p.ToUtc&&db.Auctions.Any(a=>a.Id==db.SimulationRooms.Where(room=>room.Id==r.RoomId).Select(room=>room.AuctionId).First()&&a.PolicyProfile=="D35-Simulation-v1"&&(role=="Administrator"||a.FarmerId==actor)));
        var denominator=await winners.LongCountAsync(ct);var numerator=await trades.Where(t=>t.State=="Completed"&&winners.Any(r=>r.RoomId==t.RoomId)).LongCountAsync(ct);
        await tx.CommitAsync(ct);return new(new(p.From,p.Through,p.FromUtc,p.ToUtc,ReportPeriod.Zone),"Simulation",total,"VND",completedCount,denominator,numerator,denominator==0?null:decimal.Round(100m*numerator/denominator,4));
    }
}
