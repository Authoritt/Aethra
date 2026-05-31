namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// Configuracion del modulo Identity desde appsettings:Identity.
///
/// F0: single user, credenciales en config.
/// F1+: tabla User + multi-user (campo OwnerId ya reservado).
/// </summary>
public sealed class IdentityOptions
{
    public string AdminEmail { get; set; } = "admin@aethra.local";

    /// <summary>
    /// Si esta presente, se trata como password en texto plano que se hashea en memoria
    /// al arranque. Recomendado: settear via variable de entorno
    /// <c>Identity__AdminPasswordSeed</c> en primer arranque y removerla despues.
    /// </summary>
    public string? AdminPasswordSeed { get; set; }

    /// <summary>
    /// Alternativa: hash ya generado (formato del <c>PasswordHasher</c>).
    /// Tiene precedencia sobre <see cref="AdminPasswordSeed"/>.
    /// </summary>
    public string? AdminPasswordHash { get; set; }
}
