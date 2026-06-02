using System.Collections.ObjectModel;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// Rol RBAC: agrupa scopes que se otorgan a un <see cref="User"/>.
///
/// Los 3 roles builtin son <c>admin</c> (wildcard <c>*</c>), <c>developer</c>
/// (resource:* sobre projects/deployments/builds/monitors) y <c>viewer</c>
/// (todos los :read). Cualquier rol custom referencia un subset de
/// <see cref="ApiKey.AllScopes"/>.
///
/// IsSystem=true marca los builtin para que el caller no pueda borrarlos ni
/// alterar sus scopes — proteger admin/developer/viewer es necesario porque
/// un user puede dejarse sin rol si se eliminan accidentalmente.
/// </summary>
public sealed class Role : AggregateRoot<RoleId>
{
    public string Slug { get; private set; }
    public string DisplayName { get; private set; }
    public IReadOnlySet<string> Scopes { get; private set; }
    public bool IsSystem { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Role(
        RoleId id,
        string slug,
        string displayName,
        IReadOnlySet<string> scopes,
        bool isSystem,
        DateTimeOffset now) : base(id)
    {
        Slug = slug;
        DisplayName = displayName;
        Scopes = scopes;
        IsSystem = isSystem;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Crea un rol custom (IsSystem=false). Para roles builtin usar <see cref="CreateSystem"/>.
    /// </summary>
    public static Role CreateCustom(
        string slug,
        string displayName,
        IEnumerable<string> scopes,
        DateTimeOffset now)
    {
        ValidateSlug(slug);
        ValidateDisplayName(displayName);
        var normalized = ApiKey.NormalizeScopes(scopes);
        if (normalized.Count == 0)
        {
            throw new ArgumentException("Un rol requiere al menos un scope.", nameof(scopes));
        }
        return new Role(RoleId.New(), slug.Trim().ToLowerInvariant(), displayName.Trim(), normalized, false, now);
    }

    /// <summary>
    /// Crea un rol builtin protegido. Solo llamado por el seeder al bootstrap inicial.
    /// </summary>
    public static Role CreateSystem(
        string slug,
        string displayName,
        IEnumerable<string> scopes,
        DateTimeOffset now)
    {
        ValidateSlug(slug);
        ValidateDisplayName(displayName);
        var normalized = ApiKey.NormalizeScopes(scopes);
        if (normalized.Count == 0)
        {
            throw new ArgumentException("Un rol requiere al menos un scope.", nameof(scopes));
        }
        return new Role(RoleId.New(), slug.Trim().ToLowerInvariant(), displayName.Trim(), normalized, true, now);
    }

    public void UpdateScopes(IEnumerable<string> scopes, DateTimeOffset now)
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("Los roles del sistema no pueden modificarse.");
        }
        var normalized = ApiKey.NormalizeScopes(scopes);
        if (normalized.Count == 0)
        {
            throw new ArgumentException("Un rol requiere al menos un scope.", nameof(scopes));
        }
        Scopes = normalized;
        UpdatedAt = now;
    }

    public void UpdateDisplayName(string displayName, DateTimeOffset now)
    {
        ValidateDisplayName(displayName);
        DisplayName = displayName.Trim();
        UpdatedAt = now;
    }

    /// <summary>True si el rol contiene el scope solicitado o el wildcard <c>*</c>.</summary>
    public bool HasScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }
        return Scopes.Contains(ApiKey.AdminScope) || Scopes.Contains(scope);
    }

    private static void ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("El slug del rol no puede estar vacío.", nameof(slug));
        }
        var trimmed = slug.Trim();
        if (trimmed.Length > 64)
        {
            throw new ArgumentException("El slug no puede exceder 64 caracteres.", nameof(slug));
        }
        foreach (var c in trimmed)
        {
            // Permite letras minúsculas, dígitos, guion y guion bajo — fácil de tipear y
            // estable en URLs/CLI.
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
            {
                throw new ArgumentException(
                    "El slug solo acepta a-z, 0-9, guion (-) y guion bajo (_).",
                    nameof(slug));
            }
        }
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("El nombre del rol no puede estar vacío.", nameof(displayName));
        }
        if (displayName.Trim().Length > 100)
        {
            throw new ArgumentException("El nombre no puede exceder 100 caracteres.", nameof(displayName));
        }
    }

    // Catálogo de slugs builtin — se usa para validar fallback y evitar duplicados.
    public const string AdminSlug = "admin";
    public const string DeveloperSlug = "developer";
    public const string ViewerSlug = "viewer";

    public static IReadOnlySet<string> SystemSlugs { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AdminSlug,
        DeveloperSlug,
        ViewerSlug,
    };

    // EF Core
    private Role() : base()
    {
        Slug = string.Empty;
        DisplayName = string.Empty;
        Scopes = new ReadOnlySet<string>(new HashSet<string>(StringComparer.Ordinal));
    }
}
