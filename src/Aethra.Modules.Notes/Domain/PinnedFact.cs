using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Notes.Domain;

/// <summary>
/// Hecho fijado al lado de una nota (clave/valor) — pensado para credenciales operativas
/// (admin_password, jwt_signing_key, dashboard_token, ...) que deben quedar a mano sin
/// vivir en variables de entorno reales.
///
/// El valor SIEMPRE se almacena cifrado vía DataProtection (mismo patrón que
/// <c>Aethra.Modules.Services.Infrastructure.Provisioning.AdminCredentials</c>). El flag
/// <see cref="IsSecret"/> controla únicamente si la UI debe enmascararlo por default.
/// Cifrar incluso los no-secretos elimina ramas y simplifica el repositorio.
///
/// Unicidad: (ScopeType, ScopeId, Key).
/// </summary>
public sealed class PinnedFact : AggregateRoot<PinnedFactId>
{
    public NoteScopeType ScopeType { get; private set; }
    public string ScopeId { get; private set; }
    public string Key { get; private set; }
    public byte[] ValueCipher { get; private set; }
    public bool IsSecret { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PinnedFact(
        PinnedFactId id,
        NoteScopeType scopeType,
        string scopeId,
        string key,
        byte[] valueCipher,
        bool isSecret,
        string? description,
        DateTimeOffset now) : base(id)
    {
        ScopeType = scopeType;
        ScopeId = scopeId;
        Key = key;
        ValueCipher = valueCipher;
        IsSecret = isSecret;
        Description = description;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static PinnedFact Create(
        NoteScopeType scopeType,
        string scopeId,
        string key,
        byte[] valueCipher,
        bool isSecret,
        string? description,
        DateTimeOffset now)
    {
        ValidateScopeId(scopeId);
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(valueCipher);
        if (valueCipher.Length == 0)
        {
            throw new ArgumentException("El cipher no puede estar vacío.", nameof(valueCipher));
        }
        return new PinnedFact(
            PinnedFactId.New(),
            scopeType,
            scopeId.Trim(),
            key.Trim(),
            valueCipher,
            isSecret,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            now);
    }

    public void UpdateValue(byte[] valueCipher, bool? isSecret, string? description, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(valueCipher);
        if (valueCipher.Length == 0)
        {
            throw new ArgumentException("El cipher no puede estar vacío.", nameof(valueCipher));
        }
        ValueCipher = valueCipher;
        if (isSecret is not null)
        {
            IsSecret = isSecret.Value;
        }
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAt = now;
    }

    private static void ValidateScopeId(string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            throw new ArgumentException("El scopeId no puede estar vacío.", nameof(scopeId));
        }
        if (scopeId.Length > 64)
        {
            throw new ArgumentException("El scopeId no puede exceder 64 caracteres.", nameof(scopeId));
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("La clave no puede estar vacía.", nameof(key));
        }
        var k = key.Trim();
        if (k.Length > 128)
        {
            throw new ArgumentException("La clave no puede exceder 128 caracteres.", nameof(key));
        }
        foreach (var c in k)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
            {
                throw new ArgumentException(
                    "La clave solo admite letras, dígitos, '_', '-', '.'.", nameof(key));
            }
        }
    }

    // EF Core
    private PinnedFact() : base()
    {
        ScopeId = string.Empty;
        Key = string.Empty;
        ValueCipher = [];
    }
}
