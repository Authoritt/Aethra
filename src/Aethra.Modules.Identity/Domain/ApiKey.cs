using System.Collections.ObjectModel;
using Aethra.Modules.Identity.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// API key con scopes. El secret en texto plano se devuelve UNA SOLA VEZ al crear
/// (ver <see cref="Create"/>); a partir de ahí solo persiste el hash determinístico
/// (ver <see cref="IApiKeyHasher"/>) que permite lookup O(log n).
///
/// Cada key puede revocarse o expirar — el handler de autenticación rechaza ambos casos.
/// El conjunto de <see cref="Scopes"/> determina qué endpoints puede invocar.
/// El scope especial <c>"*"</c> equivale a admin (todos los scopes).
/// </summary>
public sealed class ApiKey : AggregateRoot<ApiKeyId>
{
    public string Name { get; private set; }

    /// <summary>
    /// Primeros caracteres del secret tras el prefijo <c>aethra_</c>. Sirve para
    /// que el usuario identifique visualmente qué key es cuál sin revelarla entera.
    /// </summary>
    public string KeyPrefix { get; private set; }

    /// <summary>Hash determinístico (Argon2id deterministic) del secret.</summary>
    public byte[] KeyHash { get; private set; }

    /// <summary>Scopes asignados a esta key. Inmutable después de Create.</summary>
    public IReadOnlySet<string> Scopes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Última vez que la key fue presentada y validada con éxito.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>Si está seteado, la key fue revocada manualmente y no autentica.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Expiración opcional. Si está seteada y ya pasó, la key no autentica.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt.HasValue && ExpiresAt.Value <= now;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    private ApiKey(
        ApiKeyId id,
        string name,
        string keyPrefix,
        byte[] keyHash,
        IReadOnlySet<string> scopes,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt) : base(id)
    {
        Name = name;
        KeyPrefix = keyPrefix;
        KeyHash = keyHash;
        Scopes = scopes;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Crea una nueva ApiKey. Devuelve la instancia ya con el hash calculado.
    /// El <paramref name="plainSecret"/> que recibe debe haber sido generado con
    /// <see cref="ApiKeyGenerator.Generate"/>; el aggregate solo se encarga de hashearlo
    /// y guardarlo. El caller es quien devuelve el plaintext al usuario UNA SOLA VEZ.
    /// </summary>
    public static ApiKey Create(
        string name,
        string plainSecret,
        IEnumerable<string> scopes,
        DateTimeOffset now,
        IApiKeyHasher hasher,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        ValidateName(name);
        ArgumentException.ThrowIfNullOrEmpty(plainSecret);

        var normalizedScopes = NormalizeScopes(scopes);
        if (normalizedScopes.Count == 0)
        {
            throw new ArgumentException("Una API key requiere al menos un scope.", nameof(scopes));
        }

        if (expiresAt.HasValue && expiresAt.Value <= now)
        {
            throw new ArgumentException("La expiración no puede estar en el pasado.", nameof(expiresAt));
        }

        var visiblePrefix = ApiKeyGenerator.ExtractVisiblePrefix(plainSecret);
        var hash = hasher.Hash(plainSecret);

        var apiKey = new ApiKey(
            ApiKeyId.New(),
            name.Trim(),
            visiblePrefix,
            hash,
            normalizedScopes,
            now,
            expiresAt);

        apiKey.Raise(new ApiKeyCreatedEvent(apiKey.Id, apiKey.Name, [.. normalizedScopes]));
        return apiKey;
    }

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAt.HasValue)
        {
            return;
        }
        RevokedAt = now;
        Raise(new ApiKeyRevokedEvent(Id, Name));
    }

    public bool HasScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }
        return Scopes.Contains(AdminScope) || Scopes.Contains(scope);
    }

    // ---------------------- Scopes ----------------------

    /// <summary>Scope wildcard. Equivale a tener todos los scopes.</summary>
    public const string AdminScope = "*";

    /// <summary>
    /// Catálogo de scopes válidos en F6/F7. Cada endpoint de módulo declara una policy
    /// "scope:&lt;name&gt;" en <c>Program.cs</c> que acepta este scope o <see cref="AdminScope"/>.
    /// </summary>
    public static IReadOnlySet<string> AllScopes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "projects:read",
        "projects:write",
        "deployments:read",
        "deployments:write",
        "deployments:trigger",
        "services:read",
        "services:write",
        "monitoring:read",
        "monitoring:write",
        "metrics:read",
        "cloudflare:read",
        "cloudflare:write",
        "vms:read",
        "vms:write",
        "notes:read",
        "notes:write",
        "proxy:read",
        "proxy:write",
        "settings:read",
        "settings:write",
        "context:read",
        AdminScope,
    };

    /// <summary>
    /// Valida nombres y devuelve un set inmutable con los scopes únicos.
    /// </summary>
    public static IReadOnlySet<string> NormalizeScopes(IEnumerable<string>? scopes)
    {
        if (scopes is null)
        {
            return EmptyScopes;
        }
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in scopes)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            var trimmed = raw.Trim();
            if (!AllScopes.Contains(trimmed))
            {
                throw new ArgumentException($"Scope inválido: '{trimmed}'.", nameof(scopes));
            }
            set.Add(trimmed);
        }
        return new ReadOnlySet<string>(set);
    }

    private static readonly IReadOnlySet<string> EmptyScopes =
        new ReadOnlySet<string>(new HashSet<string>(StringComparer.Ordinal));

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre de la API key no puede estar vacío.", nameof(name));
        }
        if (name.Trim().Length > 100)
        {
            throw new ArgumentException("El nombre no puede exceder 100 caracteres.", nameof(name));
        }
    }

    // EF Core
    private ApiKey() : base()
    {
        Name = string.Empty;
        KeyPrefix = string.Empty;
        KeyHash = [];
        Scopes = EmptyScopes;
    }
}
