using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Auctions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriAuction.Api.Modules.Auctions;

internal static class SimulationPolicy
{
    public static void Require(IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            throw new WorkflowConflict("SIMULATION_ENVIRONMENT_REQUIRED", "Phase 3 Simulation chỉ được thao tác trong Development/Testing; chưa xác minh KYC hoặc thu tiền thật.");
    }
}

[ApiController, Authorize(Roles = Roles.Administrator), Route("api/v1/admin/auctions")]
public sealed class AuctionPreparationController(IAuctionService auctions, IHostEnvironment environment) : ControllerBase
{
    private Guid Actor => SessionAuthentication.Actor(User);
    [HttpGet("approved-lots")]
    public Task<IReadOnlyList<ApprovedLotOption>> Approved(CancellationToken ct) => auctions.ApprovedLotsAsync(ct);
    [HttpGet]
    public Task<AuctionPage> List(string? query, int page = 1, int pageSize = 20, CancellationToken ct = default) => auctions.ListAsync(true, query, page, pageSize, ct);
    [HttpGet("{id:guid}")]
    public Task<AuctionView> Get(Guid id, CancellationToken ct) => auctions.GetAsync(id, true, ct);
    [HttpPost]
    public async Task<IActionResult> Prepare(PrepareAuctionRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        var result = await auctions.PrepareAsync(request, Actor, ct);
        return Created($"/api/v1/admin/auctions/{result.Id}", result);
    }
    [HttpPost("{id:guid}/publish-simulation")]
    public Task<AuctionView> Publish(Guid id, PublishSimulationRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        return auctions.PublishAsync(id, request.ExpectedVersion, Actor, ct);
    }
}

[ApiController, Authorize(Roles = Roles.Enterprise + "," + Roles.Administrator), Route("api/v1/auctions")]
public sealed class AuctionsController(IAuctionService auctions, IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public Task<AuctionPage> List(string? query, int page = 1, int pageSize = 20, CancellationToken ct = default,Guid? categoryId=null,string? location=null,string? state=null) => auctions.ListAsync(false, query, page, pageSize, ct,categoryId,location,state);
    [HttpGet("{id:guid}")]
    public Task<AuctionView> Get(Guid id, CancellationToken ct) => auctions.GetAsync(id, false, ct);
    [HttpGet("{id:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> Image(Guid id, Guid imageId, CancellationToken ct)
    {
        var data = await auctions.ImageAsync(id, imageId, User.IsInRole(Roles.Administrator), ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = "inline; filename=\"auction-image.png\"";
        return File(data, "image/png");
    }
    [HttpPost("{id:guid}/registrations"), Authorize(Roles = Roles.Enterprise)]
    public async Task<IActionResult> Register(Guid id, RegisterAuctionRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        var result = await auctions.RegisterAsync(id, SessionAuthentication.Actor(User), ct);
        return Created($"/api/v1/me/registrations/{result.Id}", result);
    }
    [HttpGet("{id:guid}/my-registration"), Authorize(Roles = Roles.Enterprise)]
    public async Task<IActionResult> MyRegistration(Guid id, CancellationToken ct)
        => Ok(new { registration = await auctions.MyRegistrationAsync(id, SessionAuthentication.Actor(User), ct) });
}

[ApiController, Authorize(Roles = Roles.Enterprise), Route("api/v1/me/registrations")]
public sealed class MyRegistrationsController(IAuctionService auctions) : ControllerBase
{
    [HttpGet]
    public Task<RegistrationPage> List(int page = 1, int pageSize = 20, CancellationToken ct = default) => auctions.RegistrationsAsync(SessionAuthentication.Actor(User), false, page, pageSize, ct);
    [HttpGet("{id:guid}")]
    public Task<RegistrationView> Get(Guid id, CancellationToken ct) => auctions.RegistrationAsync(id, SessionAuthentication.Actor(User), false, ct);
}
[ApiController,Authorize(Roles=Roles.Farmer),Route("api/v1/me/auctions")]
public sealed class FarmerAuctionsController(IAuctionService auctions):ControllerBase
{
    [HttpGet]public Task<AuctionPage> List(int page=1,int pageSize=20,CancellationToken ct=default)=>auctions.OwnerAsync(SessionAuthentication.Actor(User),page,pageSize,ct);
}

[ApiController, Authorize(Roles = Roles.Administrator), Route("api/v1/admin/registrations")]
public sealed class AdminRegistrationsController(IAuctionService auctions, IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public Task<RegistrationPage> List(int page = 1, int pageSize = 20, CancellationToken ct = default) => auctions.RegistrationsAsync(SessionAuthentication.Actor(User), true, page, pageSize, ct);
    [HttpGet("{id:guid}")]
    public Task<RegistrationView> Get(Guid id, CancellationToken ct) => auctions.RegistrationAsync(id, SessionAuthentication.Actor(User), true, ct);
    [HttpPost("{id:guid}/confirm-simulation")]
    public Task<SimulationConfirmation> Confirm(Guid id, ConfirmSimulationRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        var raw = Request.Headers["Idempotency-Key"];
        if (raw.Count != 1 || !Guid.TryParseExact(raw[0], "D", out var key) || key == Guid.Empty)
            throw new RuleViolation(new() { ["idempotencyKey"] = ["Cần đúng một header Idempotency-Key dạng UUID."] });
        return auctions.ConfirmAsync(id, key, request, SessionAuthentication.Actor(User), ct);
    }
}
