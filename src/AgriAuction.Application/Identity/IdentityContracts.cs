namespace AgriAuction.Application.Identity;

public sealed record UserView(Guid Id, string Email, string DisplayName, string Role, string? ApprovalState = null, long ApprovalVersion = 1);
public sealed record SignInResult(UserView User, Guid SessionId, DateTimeOffset ExpiresAt);

public interface IIdentityService
{
    Task<SignInResult?> SignInAsync(string? email, string? password, int lifetimeMinutes, CancellationToken ct);
    Task<UserView> GetUserAsync(Guid id, CancellationToken ct);
    Task RevokeAsync(Guid sessionId, CancellationToken ct);
}
