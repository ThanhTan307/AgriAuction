using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Lots;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriAuction.Api.Modules.Lots;

public sealed class ImageUploadForm
{
    public IFormFile? File { get; set; }
    public long ExpectedVersion { get; set; }
}

[ApiController, Authorize(Roles = Roles.Farmer), Route("api/v1/lots")]
public sealed class LotsController(ILotService lots, IImageProcessor images, IHostEnvironment environment) : ControllerBase
{
    private Guid Actor => SessionAuthentication.Actor(User);
    private void RequireDemoPolicy()
    {
        // User-approved Phase 2 exception is ONLY Development/Testing. No flag
        // can turn an enabled account into a verified identity in Production.
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            throw new WorkflowConflict("ACCOUNT_POLICY_PENDING", "Chưa có quy trình tài khoản được xác nhận để tạo/gửi lô ở môi trường vận hành.");
    }
    [HttpGet]
    public Task<LotPage> List(string? query, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => lots.ListAsync(Actor, false, query, page, pageSize, ct);
    [HttpGet("{id:guid}")]
    public Task<LotView> Get(Guid id, CancellationToken ct) => lots.GetAsync(id, Actor, false, ct);
    [HttpPost]
    public async Task<IActionResult> Create(CreateLotRequest request, CancellationToken ct)
    {
        RequireDemoPolicy();
        var lot = await lots.CreateAsync(request, Actor, ct);
        return Created($"/api/v1/lots/{lot.Id}", lot);
    }
    [HttpPatch("{id:guid}")]
    public Task<LotView> Update(Guid id, UpdateLotRequest request, CancellationToken ct) => lots.UpdateAsync(id, request, Actor, ct);
    [HttpPost("{id:guid}/submit")]
    public Task<LotView> Submit(Guid id, LotVersionRequest request, CancellationToken ct)
    {
        RequireDemoPolicy();
        return lots.SubmitAsync(id, request.ExpectedVersion, Actor, ct);
    }
    [HttpPost("{id:guid}/images"), RequestSizeLimit(ImageLimits.MaxBytes + 65536), RequestFormLimits(MultipartBodyLengthLimit = ImageLimits.MaxBytes + 65536), EnableRateLimiting("lot-upload")]
    public async Task<IActionResult> Upload(Guid id, [FromForm] ImageUploadForm form, CancellationToken ct)
    {
        // Authorize ownership/state/version before invoking a native image decoder.
        var current = await lots.GetAsync(id, Actor, false, ct);
        if (current.Version != form.ExpectedVersion) throw new WorkflowConflict("VERSION_CONFLICT", "Lô đã thay đổi. Hãy tải lại dữ liệu.");
        if (current.State is not LotStates.Draft and not LotStates.Rejected)
            throw new WorkflowConflict("LOT_NOT_EDITABLE", "Không thể đổi ảnh khi lô đang duyệt/đã duyệt.");
        var file = form.File ?? throw new RuleViolation(new() { ["file"] = ["Vui lòng chọn ảnh JPEG/PNG."] });
        await using var input = file.OpenReadStream();
        var image = await images.DecodeAsync(input, file.Length, file.FileName, file.ContentType, ct);
        return StatusCode(201, await lots.AddImageAsync(id, form.ExpectedVersion, Actor, image, ct));
    }
    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public Task<LotView> DetachImage(Guid id, Guid imageId, LotVersionRequest request, CancellationToken ct)
        => lots.RemoveImageAsync(id, imageId, request.ExpectedVersion, Actor, ct);
}

[ApiController, Authorize(Roles = Roles.Administrator), Route("api/v1/admin/lots")]
public sealed class LotInspectionController(ILotService lots) : ControllerBase
{
    private Guid Actor => SessionAuthentication.Actor(User);
    [HttpGet("pending")]
    public Task<LotPage> Pending(string? query, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => lots.ListAsync(Actor, true, query, page, pageSize, ct);
    [HttpGet("{id:guid}")]
    public Task<LotView> Get(Guid id, CancellationToken ct) => lots.GetAsync(id, Actor, true, ct);
    [HttpPost("{id:guid}/decision")]
    public Task<LotView> Decide(Guid id, LotDecisionRequest request, CancellationToken ct) => lots.DecideAsync(id, request, Actor, ct);
}

[ApiController, Authorize(Roles = Roles.Farmer + "," + Roles.Administrator), Route("api/v1/media")]
public sealed class LotMediaController(ILotService lots) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var data = await lots.ReadImageAsync(id, SessionAuthentication.Actor(User), User.IsInRole(Roles.Administrator), ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = "inline; filename=\"lot-image.png\"";
        return File(data, "image/png");
    }
}
