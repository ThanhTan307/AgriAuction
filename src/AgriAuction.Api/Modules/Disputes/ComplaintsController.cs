using System.Security.Claims;
using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Disputes;
using AgriAuction.Application.Lots;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Disputes;
using AgriAuction.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriAuction.Api.Modules.Disputes;

public sealed class ComplaintForm
{
    public Guid TransactionId { get; set; }
    public string? Scope { get; set; }
    public string? Reason { get; set; }
    public Guid? ReasonId { get; set; }
    public string? Description { get; set; }
    public List<IFormFile> Files { get; set; } = [];
}
public sealed class ResponseForm
{
    public long ExpectedVersion { get; set; }
    public string? Text { get; set; }
    public List<IFormFile> Files { get; set; } = [];
}

[ApiController, Authorize, Route("api/v1/complaints")]
public sealed class ComplaintsController(IComplaintService complaints, IImageProcessor images, IHostEnvironment environment) : ControllerBase
{
    private Guid Actor => SessionAuthentication.Actor(User);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;
    private void Gate() => SimulationPolicy.Require(environment);
    private Guid Key() => Guid.TryParse(Request.Headers["Idempotency-Key"], out var key) && key != Guid.Empty ? key : throw new RuleViolation(new() { ["Idempotency-Key"] = ["Cần UUID khác rỗng."] });
    private void Fields(params string[] allowed)
    {
        var names = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Request.Form.Keys.Any(k => !names.Contains(k)) || Request.Form.Files.Any(f => !string.Equals(f.Name, "files", StringComparison.OrdinalIgnoreCase)))
            throw new RuleViolation(new() { ["form"] = ["Trường dữ liệu không được hỗ trợ."] });
    }
    private async Task<List<ImagePayload>> Decode(List<IFormFile> files, CancellationToken ct)
    {
        Complaint.EvidenceCount(files.Count); var result = new List<ImagePayload>();
        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            var image = await images.DecodeAsync(stream, file.Length, file.FileName, file.ContentType, ct);
            if (image.Data.Length > ImageLimits.MaxBytes) throw new RuleViolation(new() { ["files"] = ["Ảnh sau chuẩn hóa vượt 5 MiB."] });
            result.Add(image);
        }
        return result;
    }
    [HttpGet]
    public Task<ComplaintPage> List(string? state, string? scope, string? query, int page = 1, int pageSize = 20, CancellationToken ct = default)
    { Gate(); return complaints.ListAsync(Actor, Role, state, scope, query, page, pageSize, ct); }
    [HttpGet("{id:guid}")]
    public Task<ComplaintDetail> Get(Guid id, CancellationToken ct) { Gate(); return complaints.GetAsync(id, Actor, Role, ct); }
    [HttpGet("{id:guid}/entries")]
    public Task<ComplaintEntryPage> Entries(Guid id, int page = 1, int pageSize = 50, CancellationToken ct = default) { Gate(); return complaints.EntriesAsync(id, Actor, Role, page, pageSize, ct); }
    [HttpGet("evidence/{id:guid}")]
    public async Task<IActionResult> Evidence(Guid id, CancellationToken ct)
    {
        Gate(); var data = await complaints.EvidenceAsync(id, Actor, Role, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = "inline; filename=\"" + data.ServerFileName + "\"";
        return File(data.Data, "image/png");
    }
    [HttpPost, Authorize(Roles = Roles.Farmer + "," + Roles.Enterprise), RequestSizeLimit(3 * ImageLimits.MaxBytes + 131072), RequestFormLimits(MultipartBodyLengthLimit = 3 * ImageLimits.MaxBytes + 131072), EnableRateLimiting("lot-upload")]
    public async Task<IActionResult> Create([FromForm] ComplaintForm form, CancellationToken ct)
    {
        Gate(); var key = Key(); Fields("transactionId", "scope", "reason", "description", "reasonId");
        await complaints.AuthorizeTargetAsync(form.TransactionId, Actor, Role, ct);
        var receipt = await complaints.CreateAsync(new(form.TransactionId, form.Scope, form.Reason, form.Description,form.ReasonId), await Decode(form.Files, ct), key, Actor, Role, ct);
        return Created("/api/v1/complaints/" + receipt.ComplaintId, receipt);
    }
    [HttpPost("{id:guid}/responses"), Authorize(Roles = Roles.Farmer + "," + Roles.Enterprise), RequestSizeLimit(3 * ImageLimits.MaxBytes + 131072), RequestFormLimits(MultipartBodyLengthLimit = 3 * ImageLimits.MaxBytes + 131072), EnableRateLimiting("lot-upload")]
    public async Task<ComplaintReceipt> Respond(Guid id, [FromForm] ResponseForm form, CancellationToken ct)
    {
        Gate(); var key = Key(); Fields("expectedVersion", "text");
        await complaints.AuthorizeCaseAsync(id, Actor, Role, ct);
        return await complaints.RespondAsync(id, new(form.ExpectedVersion, form.Text), await Decode(form.Files, ct), key, Actor, Role, ct);
    }
    [HttpPost("{id:guid}/review"), Authorize(Roles = Roles.Administrator)]
    public Task<ComplaintReceipt> Review(Guid id, ComplaintReviewRequest request, CancellationToken ct) { Gate(); return complaints.ReviewAsync(id, request, Key(), Actor, Role, ct); }
    [HttpPost("{id:guid}/comments"), Authorize(Roles = Roles.Administrator)]
    public Task<ComplaintReceipt> Comment(Guid id, ComplaintReviewRequest request, CancellationToken ct) { Gate(); return complaints.CommentAsync(id, request, Key(), Actor, Role, ct); }
    [HttpPost("{id:guid}/decision"), Authorize(Roles = Roles.Administrator)]
    public Task<ComplaintReceipt> Decide(Guid id, ComplaintDecisionRequest request, CancellationToken ct) { Gate(); return complaints.DecideAsync(id, request, Key(), Actor, Role, ct); }
}
