using Aethra.Modules.Identity.Domain;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// Crea los 3 roles builtin y el usuario admin inicial si la BD está fresca.
///
/// - Roles: <c>admin</c> (wildcard <c>*</c>), <c>developer</c> (resource:*) y <c>viewer</c>
///   (todos los :read). Son IsSystem=true; el use case de borrar rol los rechaza.
/// - User admin: leído de <see cref="IdentityOptions.AdminEmail"/> +
///   <see cref="IdentityOptions.AdminPasswordSeed"/> (o "aethra-dev" en Development).
///   Asignado al role <c>admin</c>.
///
/// Idempotente: se puede llamar varias veces. Si los roles ya existen no se tocan
/// (los IsSystem nunca se mutan). Si el user admin ya existe NO se sobreescribe la
/// password — la rotación pasa por el endpoint de management.
/// </summary>
public sealed class IdentitySeeder(
    IdentityDbContext db,
    IUserPasswordCodec passwordCodec,
    IOptions<IdentityOptions> options,
    IClock clock,
    ILogger<IdentitySeeder> logger)
{
    // Catálogos de scopes builtin: declarados estáticos para evitar alocaciones por SeedAsync
    // (también evita CA1861 sobre new[] inline).
    private static readonly string[] AdminScopes = [ApiKey.AdminScope];

    private static readonly string[] DeveloperScopes =
    [
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
        "vms:read",
        "notes:read",
        "notes:write",
        "proxy:read",
        "context:read",
        "notifications:read",
        "notifications:write",
    ];

    private static readonly string[] ViewerScopes =
    [
        "projects:read",
        "deployments:read",
        "services:read",
        "monitoring:read",
        "metrics:read",
        "vms:read",
        "cloudflare:read",
        "notes:read",
        "proxy:read",
        "settings:read",
        "context:read",
        "users:read",
        "notifications:read",
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // -------- Roles builtin --------
        var rolesByslug = await db.Roles
            .Where(r => Role.SystemSlugs.Contains(r.Slug))
            .ToDictionaryAsync(r => r.Slug, ct)
            .ConfigureAwait(false);

        Role EnsureRole(string slug, string displayName, IEnumerable<string> scopes)
        {
            if (rolesByslug.TryGetValue(slug, out var existing))
            {
                return existing;
            }
            var created = Role.CreateSystem(slug, displayName, scopes, now);
            db.Roles.Add(created);
            rolesByslug[slug] = created;
            logger.LogInformation("Identity seeder: creado role builtin {Slug}", slug);
            return created;
        }

        var adminRole = EnsureRole(Role.AdminSlug, "Administrador", AdminScopes);
        EnsureRole(Role.DeveloperSlug, "Desarrollador", DeveloperScopes);
        EnsureRole(Role.ViewerSlug, "Visualizador", ViewerScopes);

        // -------- User admin bootstrap --------
        var userCount = await db.Users.CountAsync(ct).ConfigureAwait(false);
        if (userCount == 0)
        {
            var opts = options.Value;
            var seedPassword = !string.IsNullOrWhiteSpace(opts.AdminPasswordSeed)
                ? opts.AdminPasswordSeed
                : "aethra-dev";
            var cipher = passwordCodec.HashAndProtect(seedPassword);
            var adminEmail = opts.AdminEmail;
            var user = User.Create(adminEmail, cipher, displayName: "Administrador", now);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Asignar role admin tras persistir para que el RoleId del builtin exista.
            user.AssignRole(adminRole.Id, now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            logger.LogInformation(
                "Identity seeder: creado admin user '{Email}' (cambialo o creá users adicionales en /settings/users)",
                adminEmail);
        }
        else
        {
            // Si hay users pero los roles builtin no existían, guardamos solo las inserciones de roles.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
