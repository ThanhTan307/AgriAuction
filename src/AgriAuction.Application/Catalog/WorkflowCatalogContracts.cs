using System.Text.Json.Serialization;
namespace AgriAuction.Application.Catalog;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowCatalogRequest(string Kind,string Name,bool Active,long ExpectedVersion=1);
public sealed record WorkflowCatalogView(Guid Id,string Kind,string Name,bool Active,long Version);
public interface IWorkflowCatalog
{
    Task<IReadOnlyList<WorkflowCatalogView>> ListAsync(string kind,bool admin,CancellationToken ct);
    Task<WorkflowCatalogView> SaveAsync(Guid? id,WorkflowCatalogRequest request,Guid actor,CancellationToken ct);
}
