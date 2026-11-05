using System.Text;
using AgriAuction.Api.Configuration;
using AgriAuction.Api.Middleware;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Identity;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Auctions;
using AgriAuction.Application.Realtime;
using AgriAuction.Infrastructure.Realtime;
using AgriAuction.Api.Modules.Realtime;
using AgriAuction.Application.Transactions;
using AgriAuction.Application.Notifications;
using AgriAuction.Infrastructure.Transactions;
using AgriAuction.Infrastructure.Notifications;
using AgriAuction.Api.Modules.Transactions;
using AgriAuction.Application.Disputes;
using AgriAuction.Infrastructure.Disputes;
using AgriAuction.Api.Modules.Disputes;
using AgriAuction.Application.Reporting;
using AgriAuction.Infrastructure.Reporting;
using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Catalog;
using AgriAuction.Infrastructure.Identity;
using AgriAuction.Infrastructure.Lots;
using AgriAuction.Infrastructure.Auctions;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
// Console logging works without Windows Event Log registration/admin privileges.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
var allowLocal = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var productMode = builder.Configuration.GetValue<bool>("Product:Enabled");
CompletionTestTarget? completionTarget = null;
if (Environment.GetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONFIG") is not null)
{
    var raw = Environment.GetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONNECTION") ?? "";
    completionTarget = CompletionLifecycle.Read(builder.Environment.EnvironmentName, raw);
    builder.Configuration["AllowedHosts"]="localhost;127.0.0.1;10.0.2.2";
    if (!args.Contains("--completion-initialize") && !args.Contains("--completion-maintain") && builder.Configuration["urls"] != CompletionTestGuard.BaseUrl)
        throw new CompletionTargetRejected("COMPLETION_URL_REJECTED");
    if (args.Any(a => a is "--bootstrap-admin" or "--bootstrap-demo-users" or "--bootstrap-phase4-demo-users" or "--bootstrap-gvhd-demo" or "--migrate"))
        throw new CompletionTargetRejected("COMPLETION_LEGACY_ENTRYPOINT_REJECTED");
    await CompletionLifecycle.VerifyAsync(completionTarget, raw, !args.Contains("--completion-initialize"));
    if (string.IsNullOrWhiteSpace(builder.Configuration["Auth:SigningKey"]))
        throw new CompletionTargetRejected("COMPLETION_AUTH_REQUIRED");
}
if (string.IsNullOrWhiteSpace(builder.Configuration["Auth:SigningKey"]) && allowLocal)
    builder.Configuration["Auth:SigningKey"] = ProjectSettings.ReadLocal("auth.json", "signingKey");
if (Encoding.UTF8.GetByteCount(builder.Configuration["Auth:SigningKey"] ?? "") < 32)
    throw new InvalidOperationException("Auth__SigningKey phải có ít nhất 32 byte, cấu hình bằng môi trường hoặc .local/auth.json trong Development.");
var maxLifetime = allowLocal ? 720 : 120;
if (builder.Configuration.GetValue<int>("Auth:LifetimeMinutes") is < 1 || builder.Configuration.GetValue<int>("Auth:LifetimeMinutes") > maxLifetime)
    throw new InvalidOperationException($"Auth:LifetimeMinutes phải từ 1 đến {maxLifetime}.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(ProjectSettings.Connection(builder.Configuration, allowLocal)));
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IWorkflowCatalog, WorkflowCatalog>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IAccountWorkflow, AccountWorkflow>();
builder.Services.AddScoped<IAccountSanctionService, AccountSanctionService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<ILotService, LotService>();
builder.Services.AddScoped<IAuctionService, AuctionService>();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddScoped<IRoomOutbox, RoomOutbox>();
builder.Services.AddScoped<IPostAuctionProcessor, PostAuctionProcessor>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddSingleton(new CompletionProfile(productMode || completionTarget is not null));
builder.Services.AddScoped<ITransactionLifecycle, TransactionLifecycle>();
builder.Services.AddScoped<IDepositSettlementService, DepositSettlementService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IComplaintService, ComplaintService>();
builder.Services.AddScoped<IComplaintOutbox, ComplaintOutbox>();
builder.Services.AddScoped<ICompletionMaintenance, CompletionMaintenance>();
builder.Services.AddScoped<NotificationRealtime>();
if(completionTarget?.WorkersEnabled==true)builder.Services.AddHostedService<CompletionMaintenanceWorker>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<ICompletionReportService, CompletionReportService>();
if (allowLocal && (builder.Configuration.GetValue<bool?>("Simulation:ComplaintWorkerEnabled") ?? !builder.Environment.IsEnvironment("Testing")))
    builder.Services.AddHostedService<ComplaintWorker>();
if (allowLocal && (builder.Configuration.GetValue<bool?>("Simulation:PostAuctionWorkerEnabled") ?? !builder.Environment.IsEnvironment("Testing")))
    builder.Services.AddHostedService<PostAuctionWorker>();
builder.Services.AddSingleton<IRoomNotifier, SignalRRoomNotifier>();
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 8192);
if (allowLocal && (builder.Configuration.GetValue<bool?>("Simulation:WorkerEnabled") ?? !builder.Environment.IsEnvironment("Testing")))
    builder.Services.AddHostedService<SimulationAuctionWorker>();
builder.Services.AddSingleton<IDepositSimulationProvider, SimulationDepositProvider>();
builder.Services.AddSingleton<IImageProcessor, ImageProcessor>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
if (builder.Environment.IsEnvironment("Testing"))
{
    if (completionTarget is not null)
    {
        var keyPath = builder.Configuration["Security:DataProtectionPath"];
        if (string.IsNullOrWhiteSpace(keyPath)) keyPath = Path.Combine(ProjectSettings.SourceRoot(), ".local", "keys");
        builder.Services.AddDataProtection().SetApplicationName("AgriAuction.Phase1")
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    }
    else
    {
        // Isolated test-host keys: no dependency on a logged-in Windows user profile.
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
    }
}
else
{
    var keyPath = builder.Configuration["Security:DataProtectionPath"];
    if (string.IsNullOrWhiteSpace(keyPath)) keyPath = Path.Combine(ProjectSettings.SourceRoot(), ".local", "product", "keys");
    var protection = builder.Services.AddDataProtection().SetApplicationName("AgriAuction.Product")
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
}
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "agri.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = allowLocal ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthentication(options => options.DefaultScheme = "WebOrBearer")
    .AddPolicyScheme("WebOrBearer", null, options => options.ForwardDefaultSelector = context =>
        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? SessionAuthentication.BearerScheme : SessionAuthentication.CookieScheme)
    .AddCookie(SessionAuthentication.CookieScheme, options =>
    {
        options.Cookie.Name = "agri.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = allowLocal ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.LoginPath = "/pages/login.html";
        options.SlidingExpiration = allowLocal;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                if (!await SessionAuthentication.IsValid(context.Principal, context.HttpContext.RequestServices.GetRequiredService<AppDbContext>(), context.HttpContext.RequestAborted))
                    context.RejectPrincipal();
            },
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs")) context.Response.StatusCode = 401;
                else context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; }
        };
    })
    .AddJwtBearer(SessionAuthentication.BearerScheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true, ValidIssuer = builder.Configuration["Auth:Issuer"],
            ValidateAudience = true, ValidAudience = builder.Configuration["Auth:Audience"],
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Auth:SigningKey"]!)),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(10),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new()
        {
            OnTokenValidated = async context =>
            {
                if (!await SessionAuthentication.IsValid(context.Principal, context.HttpContext.RequestServices.GetRequiredService<AppDbContext>(), context.HttpContext.RequestAborted))
                    context.Fail("Session expired or revoked.");
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("login", limiter => { limiter.PermitLimit = builder.Environment.IsEnvironment("Testing") ? 1000 : 10; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
    // Bound concurrent native image decodes/encodes as well as request bytes/pixels.
    options.AddConcurrencyLimiter("lot-upload", limiter => { limiter.PermitLimit = 2; limiter.QueueLimit = 0; });
});
var app = builder.Build();
app.UseMiddleware<ApiErrors>();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
        await Results.Problem(statusCode: context.HttpContext.Response.StatusCode,
            title: context.HttpContext.Response.StatusCode == 401 ? "Vui lòng đăng nhập." : "Bạn không có quyền thực hiện thao tác này.",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.HttpContext.TraceIdentifier }).ExecuteAsync(context.HttpContext);
    else if (context.HttpContext.Response.StatusCode == 403)
    {
        context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
        await context.HttpContext.Response.SendFileAsync(Path.Combine(ProjectSettings.SourceRoot(), "pages", "access-denied.html"), context.HttpContext.RequestAborted);
    }
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs/simulation"))
    {
        if (!allowLocal) { context.Response.StatusCode = 403; return; }
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin != context.Request.Scheme + "://" + context.Request.Host) { context.Response.StatusCode = 403; return; }
    }
    await next();
});
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method) && context.User.Identity?.IsAuthenticated == true
        && !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next();
});
app.MapControllers();
app.MapHub<AuctionHub>("/hubs/simulation", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
if(completionTarget is not null)app.MapHub<NotificationsHub>("/hubs/notifications",options=>options.CloseOnAuthenticationExpiration=true).RequireAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/api/v1/capabilities", () => Results.Ok(new { completion = productMode || completionTarget is not null }));
var sourceRoot = ProjectSettings.SourceRoot();
app.MapGet("/pages/admin/analytics-dashboard.html", () => Results.File(Path.Combine(sourceRoot, "pages", "admin", "analytics-dashboard.html"), "text/html; charset=utf-8"))
    .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator));
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(sourceRoot, "assets")), RequestPath = "/assets" });
app.MapGet("/", (HttpContext context) => Results.Redirect(context.User.IsInRole(Roles.Administrator)
    ? "/pages/admin/category-management.html" : context.User.IsInRole(Roles.Farmer) ? "/pages/farmer/lots.html"
    : context.User.IsInRole(Roles.Enterprise) ? "/pages/business/market-explorer.html" : context.User.Identity?.IsAuthenticated == true ? "/pages/profile.html" : "/pages/login.html"));
app.MapGet("/pages/login.html", () => Results.File(Path.Combine(sourceRoot, "pages", "login.html"), "text/html; charset=utf-8"));
app.MapGet("/pages/register.html", () => Results.File(Path.Combine(sourceRoot, "pages", "register.html"), "text/html; charset=utf-8"));
app.MapGet("/pages/admin/category-management.html", () => Results.File(Path.Combine(sourceRoot, "pages", "admin", "category-management.html"), "text/html; charset=utf-8"))
    .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator));
app.MapGet("/pages/admin/lot-inspection.html", () => Results.File(Path.Combine(sourceRoot, "pages", "admin", "lot-inspection.html"), "text/html; charset=utf-8"))
    .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator));
app.MapGet("/pages/farmer/lots.html", () => Results.File(Path.Combine(sourceRoot, "pages", "farmer", "lots.html"), "text/html; charset=utf-8"))
    .RequireAuthorization(policy => policy.RequireRole(Roles.Farmer));
app.MapGet("/pages/profile.html", () => Results.File(Path.Combine(sourceRoot, "pages", "profile.html"), "text/html; charset=utf-8"))
    .RequireAuthorization();
app.MapGet("/pages/admin/accounts.html", () => Results.File(Path.Combine(sourceRoot, "pages", "admin", "accounts.html"), "text/html; charset=utf-8"))
    .RequireAuthorization(policy=>policy.RequireRole(Roles.Administrator));
foreach (var page in new[] { "transactions.html", "notifications.html" })
    app.MapGet("/pages/" + page, () => Results.File(Path.Combine(sourceRoot, "pages", page), "text/html; charset=utf-8")).RequireAuthorization();
app.MapGet("/pages/business/auction-room.html", () => Results.File(Path.Combine(sourceRoot, "pages", "business", "auction-room.html"), "text/html; charset=utf-8"))
    .RequireAuthorization();
app.MapGet("/pages/admin/auction-preparation.html", () => Results.File(Path.Combine(sourceRoot, "pages", "admin", "auction-preparation.html"), "text/html; charset=utf-8"))
    .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator));
app.MapGet("/pages/business/dispute-claim.html", () => Results.File(Path.Combine(sourceRoot, "pages", "business", "dispute-claim.html"), "text/html; charset=utf-8"))
    .RequireAuthorization(policy => policy.RequireRole(Roles.Farmer, Roles.Enterprise));
app.MapGet("/pages/admin/dispute-arbitration.html", () => Results.File(Path.Combine(sourceRoot, "pages", "admin", "dispute-arbitration.html"), "text/html; charset=utf-8"))
    .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator));
foreach (var page in new[] { "market-explorer.html", "deposit-wallet.html" })
    app.MapGet("/pages/business/" + page, () => Results.File(Path.Combine(sourceRoot, "pages", "business", page), "text/html; charset=utf-8"))
        .RequireAuthorization(policy => policy.RequireRole(Roles.Enterprise));

if (args.Contains("--migrate") || args.Contains("--bootstrap-admin") || args.Contains("--bootstrap-demo-users") || args.Contains("--bootstrap-phase4-demo-users") || args.Contains("--bootstrap-gvhd-demo"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Validate the dedicated target before any schema/migration command is executed.
    if (args.Contains("--bootstrap-gvhd-demo"))
        GvhdDemoBootstrap.ValidateTarget(builder.Environment.EnvironmentName,
            db.Database.GetDbConnection().ConnectionString, ProjectSettings.Schema(builder.Configuration));
    if (args.Contains("--migrate"))
    {
        var schema = ProjectSettings.Schema(builder.Configuration);
        // SQL identifiers cannot be parameters. Schema() accepts only an ASCII identifier;
        // quoting remains mandatory. This command never drops or truncates existing data.
        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "CREATE SCHEMA IF NOT EXISTS \"" + schema + "\"";
            await command.ExecuteNonQueryAsync();
        }
        await db.Database.MigrateAsync();
        Console.WriteLine("Migrations applied to the configured application schema.");
    }
    if (args.Contains("--bootstrap-admin"))
    {
        var email = builder.Configuration["Bootstrap:Email"]?.Trim() ?? "";
        var password = builder.Configuration["Bootstrap:Password"] ?? "";
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || password.Length < 12)
            throw new InvalidOperationException("Cấu hình Bootstrap__Email và Bootstrap__Password (ít nhất 12 ký tự); không có tài khoản mặc định.");
        var normalized = email.ToUpperInvariant();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>();
        var existing = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized);
        if (existing is not null)
        {
            if (existing.Role != Roles.Administrator || !existing.IsEnabled || hasher.VerifyHashedPassword(existing, existing.PasswordHash, password) == PasswordVerificationResult.Failed)
                throw new InvalidOperationException("Email đã tồn tại. Không ghi đè tài khoản/mật khẩu/quyền hiện có.");
            Console.WriteLine("Configured administrator already exists; no account data changed.");
        }
        else
        {
            var account = new Account { Email = email, NormalizedEmail = normalized, DisplayName = "Quản trị viên", Role = Roles.Administrator };
            account.PasswordHash = hasher.HashPassword(account, password);
            db.Users.Add(account);
            await db.SaveChangesAsync();
            Console.WriteLine("Administrator created. Password was not logged.");
        }
    }
    if (args.Contains("--bootstrap-demo-users"))
        await DemoUserBootstrap.RunAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>(), builder.Configuration, builder.Environment);
    if (args.Contains("--bootstrap-phase4-demo-users"))
        await Phase4DemoBootstrap.RunAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>(), builder.Configuration, builder.Environment);
    if (args.Contains("--bootstrap-gvhd-demo"))
        await GvhdDemoBootstrap.RunAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>(),
            scope.ServiceProvider.GetRequiredService<AgriAuction.Application.Catalog.ICategoryService>(), builder.Configuration);
    if (args.Contains("--bootstrap-product"))
        await ProductBootstrap.RunAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>(),
            scope.ServiceProvider.GetRequiredService<AgriAuction.Application.Catalog.ICategoryService>(), builder.Configuration);
    return;
}
if (args.Contains("--completion-initialize"))
{
    if (completionTarget is null) throw new CompletionTargetRejected("COMPLETION_CONFIG_REQUIRED");
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.OpenConnectionAsync();
    await using (var command = db.Database.GetDbConnection().CreateCommand())
    {
        command.CommandText = "CREATE SCHEMA IF NOT EXISTS \"" + completionTarget.Schema + "\"";
        await command.ExecuteNonQueryAsync();
    }
    await CompletionLifecycle.VerifyAsync(completionTarget, Environment.GetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONNECTION")!, true);
    await db.Database.MigrateAsync();
    await CompletionBootstrap.RunAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>(), builder.Configuration);
    Console.WriteLine("COMPLETION_INITIALIZED: identity, schema ownership and migrations verified; synthetic identities only.");
    return;
}
if (args.Contains("--completion-maintain"))
{
    if (completionTarget is null) throw new CompletionTargetRejected("COMPLETION_CONFIG_REQUIRED");
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IRoomService>().CloseDueBatchAsync(default);
    var processor = scope.ServiceProvider.GetRequiredService<IPostAuctionProcessor>();
    await processor.RecoverAsync(default); await processor.ProcessAsync(default);
    await scope.ServiceProvider.GetRequiredService<ICompletionMaintenance>().AdvanceDeadlinesAsync(default);
    await scope.ServiceProvider.GetRequiredService<IComplaintOutbox>().DispatchAsync(default);
    return;
}
if (productMode && completionTarget is null)
{
    await using var scope = app.Services.CreateAsyncScope();
    await ProductBootstrap.EnsureWorkflowDefaultsAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>());
}
app.Run();

public partial class Program { }
