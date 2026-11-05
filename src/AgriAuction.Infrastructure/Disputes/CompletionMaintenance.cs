using System.Text.Json;
using AgriAuction.Application.Disputes;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AgriAuction.Infrastructure.Disputes;

public sealed class CompletionMaintenance(AppDbContext db):ICompletionMaintenance
{
    public async Task<int> AdvanceDeadlinesAsync(CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var rows=await db.Complaints.FromSqlRaw("SELECT * FROM complaints WHERE policy_profile='D35-Simulation-v1' AND state='Open' AND responded_at IS NULL AND response_due_at<=clock_timestamp() ORDER BY response_due_at,id LIMIT 20 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        var now=await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        foreach(var c in rows)
        {
            c.ExpireResponse(now);var recipients=await db.Users.Where(u=>u.IsEnabled&&(u.Role=="Administrator"||u.Id==c.ComplainantId||u.Id==c.RespondentId)).Select(u=>u.Id).ToArrayAsync(ct);
            db.ComplaintEntries.Add(new(){ComplaintId=c.Id,ActorId=null,ActorRole="System",Kind="ResponseDeadlineExpired",Text="Hết hạn 3 ngày chưa có giải trình của bên còn lại; chuyển hàng đợi Admin.",State=c.State,Version=c.Version,OccurredAt=now,NextAttemptAt=now,RecipientIds=recipients});
            db.AuditLogs.Add(new(){EntityId=c.Id,ActorId=Guid.Empty,Action="ComplaintResponseDeadlineExpired",After=JsonSerializer.Serialize(new{c.State,c.Version,c.ResponseDueAt}),OccurredAt=now});
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return rows.Count;
    }
}
