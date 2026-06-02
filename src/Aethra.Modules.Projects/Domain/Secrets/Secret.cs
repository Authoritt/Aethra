using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.Secrets;

/// <summary>
/// Secreto con scope polimórfico, cifrado at-rest. Vive en una tabla SEPARADA de
/// <see cref="EnvironmentVariable"/> (env vars planas) para reducir el blast-radius de un leak
/// de la tabla principal — diseño explícito de F9.0/F9.1.
///
/// El <see cref="ValueCipher"/> nunca se persiste en claro: el writer cifra el plaintext con
/// DataProtection (purpose <c>aethra-secrets</c>) antes de insertar, y sólo el orquestador de
/// deploy lo descifra justo antes de pasarlo al satélite.
///
/// Resolución (lo más cercano gana): Instance &gt; Client &gt; Template &gt; Project.
/// </summary>
public sealed class Secret : Entity<SecretId>
{
    public EnvScopeType ScopeType { get; private set; }

    /// <summary>
    /// ID textual del scope. Heterogéneo (<c>prj_*</c>, <c>tpl_*</c>, <c>cli_*</c>, <c>ins_*</c>)
    /// según <see cref="ScopeType"/>. String evita FK polimórfico.
    /// </summary>
    public string ScopeId { get; private set; }

    public string Key { get; private set; }

    /// <summary>Valor cifrado (DataProtection). Nunca plaintext en BD ni en logs.</summary>
    public byte[] ValueCipher { get; private set; }

    /// <summary>
    /// Origen lógico. <c>null</c> = creado manualmente. <c>"binding:bnd_..."</c> = inyectado por
    /// un ServiceBinding. Permite revoke selectivo sin pisar overrides manuales.
    /// </summary>
    public string? Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Secret(
        SecretId id,
        EnvScopeType scopeType,
        string scopeId,
        string key,
        byte[] valueCipher,
        string? source,
        DateTimeOffset now) : base(id)
    {
        ScopeType = scopeType;
        ScopeId = scopeId;
        Key = key;
        ValueCipher = valueCipher;
        Source = source;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Secret Create(
        EnvScopeType scopeType,
        string scopeId,
        string key,
        byte[] valueCipher,
        DateTimeOffset now,
        string? source = null)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(valueCipher);
        return new Secret(SecretId.New(), scopeType, scopeId, key.Trim(), valueCipher, source, now);
    }

    public void UpdateCipher(byte[] valueCipher, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(valueCipher);
        ValueCipher = valueCipher;
        UpdatedAt = now;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("La clave del secreto no puede estar vacía.", nameof(key));
        }
        var k = key.Trim();
        if (k.Length > 256)
        {
            throw new ArgumentException("La clave no puede exceder 256 caracteres.", nameof(key));
        }
        foreach (var c in k)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                throw new ArgumentException(
                    "La clave solo admite letras, dígitos y guion bajo (convención POSIX).", nameof(key));
            }
        }
    }

    // EF Core
    private Secret() : base() { ScopeId = string.Empty; Key = string.Empty; ValueCipher = []; }
}
