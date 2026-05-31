namespace Aethra.Modules.Proxy.Domain;

/// <summary>
/// Cuenta ACME persistida (tabla <c>tls_account</c>, una sola row). Guarda la account key
/// que <c>Certes.AcmeContext</c> usa para firmar pedidos. Sin esta key cada arranque crearía
/// una cuenta nueva y agotaría el rate-limit de Let's Encrypt en pocas horas.
///
/// El PEM se persiste cifrado con DataProtection (purpose <c>"aethra-acme-account"</c>).
/// </summary>
public sealed class AcmeAccount
{
    /// <summary>Identificador fijo: siempre <c>"default"</c>. Garantiza singleton a nivel BD.</summary>
    public string Id { get; private set; } = DefaultId;

    public string AccountKeyPemCipherText { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public bool UseStaging { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public const string DefaultId = "default";

    public static AcmeAccount Create(string accountKeyPemCipherText, string email, bool useStaging, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKeyPemCipherText);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return new AcmeAccount
        {
            Id = DefaultId,
            AccountKeyPemCipherText = accountKeyPemCipherText,
            Email = email,
            UseStaging = useStaging,
            CreatedAt = now,
        };
    }

    // EF Core
    private AcmeAccount() { }
}
