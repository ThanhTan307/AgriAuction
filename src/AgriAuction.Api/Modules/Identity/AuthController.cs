using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AgriAuction.Application.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace AgriAuction.Api.Modules.Identity;

public sealed record LoginRequest(string? Email, string? Password);

[ApiController]
[Route("api/v1")]
public sealed class AuthController(IIdentityService identity, IConfiguration config, IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous, HttpGet("auth/csrf")]
    public IActionResult Csrf()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [AllowAnonymous, HttpPost("auth/login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, [FromQuery] bool useCookies = false, CancellationToken ct = default)
    {
        if (useCookies) await antiforgery.ValidateRequestAsync(HttpContext);
        var lifetime = config.GetValue<int>("Auth:LifetimeMinutes");
        var session = await identity.SignInAsync(request.Email, request.Password, lifetime, ct);
        if (session is null) return InvalidLogin();
        var account = session.User;
        var expires = session.ExpiresAt;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.DisplayName), new Claim(ClaimTypes.Email, account.Email),
            new Claim(ClaimTypes.Role, account.Role), new Claim("sid", session.SessionId.ToString())
        };
        var token = new JwtSecurityToken(config["Auth:Issuer"], config["Auth:Audience"], claims,
            expires: expires.UtcDateTime,
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Auth:SigningKey"]!)), SecurityAlgorithms.HmacSha256));
        if (useCookies)
            await HttpContext.SignInAsync(SessionAuthentication.CookieScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, SessionAuthentication.CookieScheme)),
                new AuthenticationProperties { ExpiresUtc = expires, IsPersistent = false });
        Response.Headers.CacheControl = "no-store";
        return Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = expires,
            user = account });
    }

    private ObjectResult InvalidLogin() => StatusCode(401, new ProblemDetails
    {
        Status = 401, Title = "Email hoặc mật khẩu không đúng, hoặc tài khoản không khả dụng.",
        Extensions = { ["code"] = "INVALID_CREDENTIALS", ["traceId"] = HttpContext.TraceIdentifier }
    });

    [Authorize, HttpGet("me")]
    public Task<UserView> Me(CancellationToken ct) => identity.GetUserAsync(SessionAuthentication.Actor(User), ct);

    [Authorize, HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue("sid")!);
        await identity.RevokeAsync(id, ct);
        await HttpContext.SignOutAsync(SessionAuthentication.CookieScheme);
        return NoContent();
    }
}
