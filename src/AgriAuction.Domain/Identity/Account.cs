namespace AgriAuction.Domain.Identity;

public static class Roles
{
    public const string Administrator = nameof(Administrator);
    public const string Farmer = nameof(Farmer);
    public const string Enterprise = nameof(Enterprise);
    public static readonly string[] All = [Administrator, Farmer, Enterprise];
}

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Farmer;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long ProfileVersion { get; set; } = 1;
    public string? ApprovalState { get; set; }
    public long ApprovalVersion { get; set; } = 1;
    public DateTimeOffset? BiddingSuspendedUntil { get; set; }
    public long RestrictionVersion { get; set; }=1;
}

public sealed class AccountReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid ActorId { get; set; }
    public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public long Version { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class AccountSanction
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid ActorId { get; set; }
    public Guid DecisionEntryId { get; set; }
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; }="";
    public string Kind { get; set; }="";
    public string Reason { get; set; }="";
    public DateTimeOffset? Until { get; set; }
    public long Version { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class LoginSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Account Account { get; set; } = null!;
}
