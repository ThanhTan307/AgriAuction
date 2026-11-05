using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AgriAuction.Api.Modules.Transactions;

[ApiController,Authorize(Roles=Roles.Administrator),Route("api/v1/admin/deposit-settlements")]
public sealed class DepositSettlementsController(IDepositSettlementService settlements,IHostEnvironment environment):ControllerBase
{
    [HttpGet]public Task<IReadOnlyList<SettlementCandidate>> List(CancellationToken ct){SimulationPolicy.Require(environment);return settlements.ListAsync(SessionAuthentication.Actor(User),ct);}
    [HttpPost("{id:guid}/{command}")]public Task<DepositDispositionReceipt> Act(Guid id,string command,DepositDispositionRequest request,CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        if(Request.Headers["Idempotency-Key"].Count!=1||!Guid.TryParseExact(Request.Headers["Idempotency-Key"],"D",out var key)||key==Guid.Empty)
            throw new RuleViolation(new(){["Idempotency-Key"]=["Cần một UUID khác rỗng."]});
        return settlements.ExecuteAsync(id,command,request,key,SessionAuthentication.Actor(User),ct);
    }
}
