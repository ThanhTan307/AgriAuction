using AgriAuction.Api.Modules.Identity;
using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AgriAuction.Api.Modules.Catalog;
[ApiController,Authorize,Route("api/v1/workflow-catalog")]
public sealed class WorkflowCatalogController(IWorkflowCatalog catalog,CompletionProfile profile,IHostEnvironment environment):ControllerBase
{
    private void Require(){SimulationPolicy.Require(environment);if(!profile.Enabled)throw new AgriAuction.Domain.Lots.WorkflowConflict("POLICY_LOCKED","Danh mục Completion chưa mở.");}
    [HttpGet]public Task<IReadOnlyList<WorkflowCatalogView>> List(string kind,CancellationToken ct){Require();return catalog.ListAsync(kind,User.IsInRole("Administrator"),ct);}
    [HttpPost,Authorize(Roles="Administrator")]public Task<WorkflowCatalogView> Create(WorkflowCatalogRequest request,CancellationToken ct){Require();return catalog.SaveAsync(null,request,SessionAuthentication.Actor(User),ct);}
    [HttpPatch("{id:guid}"),Authorize(Roles="Administrator")]public Task<WorkflowCatalogView> Update(Guid id,WorkflowCatalogRequest request,CancellationToken ct){Require();return catalog.SaveAsync(id,request,SessionAuthentication.Actor(User),ct);}
}
