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
///   <see cref="IdentityOptions.AdminPasswordSeed"/>. Asignado al role <c>admin</c>.
///   Si no se configura semilla, se aseguran los roles pero no se crea ningún usuario
///   y se registra una advertencia (<see cref="AdminSeedMissing"/>).
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
    // ASCII "AETHRA", seguido de 0x0136, como clave estable de PostgreSQL advisory lock.
    private const long AdvisoryLockKey = 0x4145544852410136;

    /// <summary>
    /// EventId emitido cuando la base de datos está vacía pero no se configuró <see cref="IdentityOptions.AdminPasswordSeed"/>.
    /// </summary>
    public static readonly EventId AdminSeedMissing = new(136, nameof(AdminSeedMissing));
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
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database
                .BeginTransactionAsync(ct)
                .ConfigureAwait(false);

            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
                        ct)
                    .ConfigureAwait(false);
            }

            db.ChangeTracker.Clear();

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
                if (string.IsNullOrWhiteSpace(opts.AdminPasswordSeed))
                {
                    // No se sugiere Identity:AllowBootstrapLogin: sin semilla ni hash, SingleUserStore
                    // acepta la contraseña pública por defecto, que es justo lo que este cambio cierra.
                    logger.LogWarning(
                        AdminSeedMissing,
                        "Identity seeder: no se configuró 'Identity:AdminPasswordSeed'. Se aseguraron los roles builtin pero no se creó ningún usuario inicial. Para crear el primer admin, defina la variable de entorno 'Identity__AdminPasswordSeed' y reinicie el servicio (solo se usa mientras no exista ningún usuario).");
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    return;
                }

                var seedPassword = opts.AdminPasswordSeed;
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

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
