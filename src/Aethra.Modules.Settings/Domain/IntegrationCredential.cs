using System.Text.RegularExpressions;
using Aethra.Modules.Settings.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Settings.Domain;

/// <summary>
/// Credencial externa (token, password, API key) centralizada en el módulo Settings.
/// Otros módulos (Cloudflare, Github webhooks, registry interno) resuelven el valor por
/// <see cref="Name"/> en lugar de almacenar credenciales dentro de su propio aggregate.
///
/// El valor en texto plano viaja una sola vez: al crear o rotar la credencial. A partir
/// de ahí solo persiste <see cref="ValueCipher"/> cifrado con DataProtection (purpose
/// <c>aethra-integration-creds</c>). La única forma de extraer el valor es vía
/// <see cref="IIntegrationCredentialCodec.Decode"/> dentro del módulo.
/// </summary>
public sealed class IntegrationCredential : AggregateRoot<IntegrationCredentialId>
{
    // namespace:slug — ej. "cloudflare:default", "registry:internal".
    private static readonly Regex NameRegex = new(
        "^[a-z]+:[a-z0-9-]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Identificador único y estable que otros módulos usan vía
    /// <c>IIntegrationCredentialResolver.GetSecretAsync</c>. Formato <c>namespace:slug</c>.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>Tipo del proveedor — metadata para UI; no participa en lookups.</summary>
    public IntegrationCredentialType Type { get; private set; }

    /// <summary>Nombre legible para humanos, libre.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Descripción opcional (ej. para distinguir credenciales del mismo tipo).</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Valor cifrado con DataProtection. Nunca devolver crudo fuera del módulo —
    /// usar <see cref="IIntegrationCredentialCodec.Decode"/> dentro del resolver.
    /// </summary>
    public byte[] ValueCipher { get; private set; }

    /// <summary>
    /// Metadata opcional (clave→valor). Sirve para parámetros no-secretos asociados al
    /// proveedor (ej. account_id, region) sin necesidad de crear un campo por tipo.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    private IntegrationCredential(
        IntegrationCredentialId id,
        string name,
        IntegrationCredentialType type,
        string displayName,
        string? description,
        byte[] valueCipher,
        IReadOnlyDictionary<string, string>? metadata,
        DateTimeOffset createdAt) : base(id)
    {
        Name = name;
        Type = type;
        DisplayName = displayName;
        Description = description;
        ValueCipher = valueCipher;
        Metadata = metadata;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Crea una nueva credencial. El <paramref name="plainValue"/> se cifra inmediatamente
    /// con <paramref name="codec"/>; el plain solo queda en memoria durante la transacción.
    /// </summary>
    public static IntegrationCredential Create(
        string name,
        IntegrationCredentialType type,
        string displayName,
        string plainValue,
        IReadOnlyDictionary<string, string>? metadata,
        IIntegrationCredentialCodec codec,
        DateTimeOffset now,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ValidateName(name);
        ValidateDisplayName(displayName);
        ArgumentException.ThrowIfNullOrEmpty(plainValue);

        if (description is { Length: > 500 })
        {
            throw new ArgumentException("La descripción no puede exceder 500 caracteres.", nameof(description));
        }

        var normalizedMetadata = NormalizeMetadata(metadata);
        var cipher = codec.Encode(plainValue);

        var credential = new IntegrationCredential(
            IntegrationCredentialId.New(),
            name.Trim().ToLowerInvariant(),
            type,
            displayName.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            cipher,
            normalizedMetadata,
            now);

        credential.Raise(new IntegrationCredentialCreatedEvent(credential.Id, credential.Name, credential.Type));
        return credential;
    }

    /// <summary>
    /// Reemplaza el valor cifrado por uno nuevo. Útil cuando el operador rota un token.
    /// </summary>
    public void Rotate(string newPlainValue, IIntegrationCredentialCodec codec, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentException.ThrowIfNullOrEmpty(newPlainValue);

        ValueCipher = codec.Encode(newPlainValue);
        RotatedAt = now;
        Raise(new IntegrationCredentialRotatedEvent(Id, Name));
    }

    /// <summary>
    /// Marca que la credencial acaba de ser leída con éxito por un consumidor. El resolver
    /// llama esto fire-and-forget — no se incluye en eventos para evitar ruido en el audit log.
    /// </summary>
    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;

    // ---------------------- Validación ----------------------

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre de la credencial no puede estar vacío.", nameof(name));
        }
        var trimmed = name.Trim().ToLowerInvariant();
        if (trimmed.Length > 100)
        {
            throw new ArgumentException("El nombre no puede exceder 100 caracteres.", nameof(name));
        }
        if (!NameRegex.IsMatch(trimmed))
        {
            throw new ArgumentException(
                "El nombre debe seguir el formato 'namespace:slug' (lowercase, alfanumérico y guiones).",
                nameof(name));
        }
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("displayName no puede estar vacío.", nameof(displayName));
        }
        if (displayName.Trim().Length > 200)
        {
            throw new ArgumentException("displayName no puede exceder 200 caracteres.", nameof(displayName));
        }
    }

    private static Dictionary<string, string>? NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }
        var dict = new Dictionary<string, string>(metadata.Count, StringComparer.Ordinal);
        foreach (var kv in metadata)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }
            if (kv.Key.Length > 64)
            {
                throw new ArgumentException($"Clave de metadata '{kv.Key}' excede 64 caracteres.", nameof(metadata));
            }
            dict[kv.Key.Trim()] = kv.Value ?? string.Empty;
        }
        return dict.Count == 0 ? null : dict;
    }

    // EF Core
    private IntegrationCredential() : base()
    {
        Name = string.Empty;
        DisplayName = string.Empty;
        ValueCipher = [];
    }
}
