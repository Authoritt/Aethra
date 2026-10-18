namespace Aethra.Modules.Identity.Domain.Totp;

/// <summary>
/// Estado autoritativo y de corta vida de un intento de login TOTP. El JWT entregado al
/// cliente solo transporta el identificador; consumo, expiracion e intentos viven en BD.
/// </summary>
public sealed class TotpChallenge
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public string Id { get; private set; }
    public string UserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? ConsumedAt { get; private set; }

    public TotpChallenge(string id, string userId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        Id = id;
        UserId = userId;
        CreatedAt = createdAt.UtcDateTime;
        ExpiresAt = createdAt.Add(Lifetime).UtcDateTime;
    }

    // EF Core
    private TotpChallenge()
    {
        Id = string.Empty;
        UserId = string.Empty;
    }
}
