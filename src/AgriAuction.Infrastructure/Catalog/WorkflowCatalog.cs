using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Lots;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AgriAuction.Infrastructure.Catalog;
public sealed class WorkflowCatalog(AppDbContext db):IWorkflowCatalog
{
    private static void Kind(string kind){if(kind is not ("ComplaintReason" or "Inspection"))throw new RuleViolation(new(){["kind"]=["Loại danh mục không hợp lệ."]});}
    public async Task<IReadOnlyList<WorkflowCatalogView>> ListAsync(string kind,bool admin,CancellationToken ct)
    {Kind(kind);return await db.WorkflowCatalogItems.AsNoTracking().Where(i=>i.Kind==kind&&(admin||i.Active)).OrderBy(i=>i.Name).Take(100).Select(i=>new WorkflowCatalogView(i.Id,i.Kind,i.Name,i.Active,i.Version)).ToListAsync(ct);}
    public async Task<WorkflowCatalogView> SaveAsync(Guid? id,WorkflowCatalogRequest r,Guid actor,CancellationToken ct)
    {
        Kind(r.Kind);var name=r.Name?.Trim()??"";if(name.Length is <1 or >200||name.Any(char.IsControl))throw new RuleViolation(new(){["name"]=["Tên từ 1 đến 200 ký tự hợp lệ."]});
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Catalog writers and approval readers share the same ordering boundary.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(7360021)",ct);
        if(!await db.Users.AnyAsync(u=>u.Id==actor&&u.Role=="Administrator"&&u.IsEnabled,ct))throw new PermissionDenied();
        WorkflowCatalogItem row;
        if(id is null){if(await db.WorkflowCatalogItems.CountAsync(i=>i.Kind==r.Kind,ct)>=100)throw new WorkflowConflict("CATALOG_LIMIT","Tối đa 100 mục mỗi loại.");row=new(){Kind=r.Kind};db.WorkflowCatalogItems.Add(row);}
        else {row=await db.WorkflowCatalogItems.SingleOrDefaultAsync(i=>i.Id==id,ct)??throw new ResourceMissing();if(row.Kind!=r.Kind||row.Version!=r.ExpectedVersion)throw new WorkflowConflict("VERSION_CONFLICT","Danh mục đã thay đổi.");row.Version++;}
        row.Name=name;row.Active=r.Active;db.AuditLogs.Add(new(){ActorId=actor,EntityId=row.Id,Action="WorkflowCatalogSaved",After=JsonSerializer.Serialize(new{row.Id,row.Kind,row.Name,row.Active,row.Version})});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(row.Id,row.Kind,row.Name,row.Active,row.Version);
    }
}
