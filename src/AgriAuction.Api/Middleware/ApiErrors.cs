using AgriAuction.Application.Catalog;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;
using AgriAuction.Application.Lots;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriAuction.Api.Middleware;

public sealed class ApiErrors(RequestDelegate next, ILogger<ApiErrors> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            var (status, code, title) = error switch
            {
                RuleViolation => (400, "VALIDATION_ERROR", "Dữ liệu không hợp lệ."),
                AntiforgeryValidationException => (400, "CSRF_INVALID", "Phiên biểu mẫu hết hạn. Vui lòng tải lại trang."),
                ResourceMissing => (404, "NOT_FOUND", "Không tìm thấy tài nguyên trong phạm vi được phép."),
                PermissionDenied => (403, "FORBIDDEN", "Không có quyền thực hiện thao tác này."),
                WorkflowConflict workflow => (409, workflow.Code, workflow.Message),
                VersionConflict or DbUpdateConcurrencyException => (409, "VERSION_CONFLICT", "Dữ liệu đã thay đổi. Vui lòng tải lại trước khi sửa."),
                BadHttpRequestException { StatusCode: 413 } => (413, "UPLOAD_TOO_LARGE", "Dữ liệu tải lên vượt giới hạn cho phép."),
                InvalidDataException => (400, "UPLOAD_INVALID", "Dữ liệu tải lên không hợp lệ hoặc vượt giới hạn."),
                DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_categories_code" } } =>
                    (409, "DUPLICATE_CODE", "Mã danh mục đã được sử dụng."),
                DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                    (409, "DUPLICATE_OPERATION", "Thao tác trùng đã bị database chặn. Hãy tải lại dữ liệu."),
                NpgsqlException or DbUpdateException => (503, "DATABASE_UNAVAILABLE", "Không thể xử lý với database. Không có thao tác thành công được xác nhận."),
                _ => (500, "INTERNAL_ERROR", "Không thể hoàn tất yêu cầu.")
            };
            if (status >= 500) logger.LogError("Request {TraceId} failed ({ErrorType}).", context.TraceIdentifier, error.GetType().Name);
            ProblemDetails problem = error is RuleViolation rules ? new ValidationProblemDetails(rules.Errors)
                : code == "DUPLICATE_CODE" ? new ValidationProblemDetails(new Dictionary<string, string[]> { ["code"] = [title] }) : new ProblemDetails();
            problem.Status = status;
            problem.Title = title;
            problem.Extensions["code"] = code;
            problem.Extensions["traceId"] = context.TraceIdentifier;
            // Serialize the explicit RFC7807 shape, retaining field errors even when
            // an older installed shared runtime writes ProblemDetails as its base type.
            var payload = new Dictionary<string, object?>
            {
                ["type"] = "about:blank", ["title"] = title, ["status"] = status,
                ["code"] = code, ["traceId"] = context.TraceIdentifier
            };
            if (problem is ValidationProblemDetails validation) payload["errors"] = validation.Errors;
            await Results.Json(payload, statusCode: status, contentType: "application/problem+json").ExecuteAsync(context);
        }
    }
}
