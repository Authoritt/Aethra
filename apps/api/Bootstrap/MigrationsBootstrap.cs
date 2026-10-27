using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// Permite interceptar o sustituir la ejecución del backfill de URLs de monitores (p. ej. en pruebas).
/// </summary>
public interface IMonitorUrlBackfillStep
{
    Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}

/// <summary>
/// Permite interceptar o sustituir la ejecución del sembrado de identidad (p. ej. en pruebas).
/// </summary>
public interface IIdentitySeedStep
{
    Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}

/// <summary>
/// Aplica migraciones al arrancar en Development. En producción se usa
/// <c>dotnet ef database update</c> explícito durante el deploy.
///
/// Cada módulo registra su DbContext; este helper los recolecta y ejecuta migraciones
/// en orden estable (alfabético por nombre del context). El SharedDbContext va primero
/// porque su schema "shared" puede ser referenciado por otros (idempotency_keys).
/// </summary>
public static class MigrationsBootstrap
{
    public static async Task ApplyPendingMigrationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

        var contexts = new DbContext[]
        {
            sp.GetRequiredService<SharedDbContext>(),
            sp.GetRequiredService<ProjectsDbContext>(),
            sp.GetRequiredService<VmsDbContext>(),
            sp.GetRequiredService<MetricsDbContext>(),
            sp.GetRequiredService<ProxyDbContext>(),
            sp.GetRequiredService<DeploymentsDbContext>(),
            sp.GetRequiredService<ServicesDbContext>(),
            sp.GetRequiredService<CloudflareDbContext>(),
            sp.GetRequiredService<MonitoringDbContext>(),
            sp.GetRequiredService<NotesDbContext>(),
            sp.GetRequiredService<NotificationsDbContext>(),
            sp.GetRequiredService<IdentityDbContext>(),
            sp.GetRequiredService<SettingsDbContext>(),
        };

        foreach (var ctx in contexts)
        {
            var pending = (await ctx.Database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();
            if (pending.Count == 0)
            {
                logger.LogInformation("{Context}: sin migraciones pendientes", ctx.GetType().Name);
            }
            else
            {
                logger.LogInformation("{Context}: aplicando {Count} migraciones: {Migrations}",
                    ctx.GetType().Name, pending.Count, string.Join(", ", pending));
                await ctx.Database.MigrateAsync().ConfigureAwait(false);
            }
        }

        // F11.1 / Issue #21: tras migrar, asegurar los 3 roles builtin + el user admin si la BD está vacía.
        // Idempotente — si ya existe el admin solo se confirman los roles builtin.
        await services.SeedIdentityAsync().ConfigureAwait(false);
    }

    public const string SeedIdentityConfigKey = "Aethra:SeedIdentityOnStart";
    public const bool SeedIdentityDefault = false;

    /// <summary>
    /// Indica si se debe ejecutar el sembrado de identidad al arrancar cuando las migraciones no corren en el proceso.
    /// Por defecto es false; en despliegues con migraciones externas gestionadas permite asegurar los roles builtin y el admin.
    /// </summary>
    public static bool ShouldSeedIdentity(IConfiguration configuration)
        => configuration.GetValue(SeedIdentityConfigKey, SeedIdentityDefault);

    /// <summary>
    /// Asegura los roles builtin y el usuario admin inicial si la BD está vacía.
    /// Desacoplado de la ejecución de migraciones automáticas.
    /// </summary>
    public static async Task SeedIdentityAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var step = services.GetService<IIdentitySeedStep>();
        if (step is not null)
        {
            await step.ExecuteAsync(services, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var seeder = sp.GetRequiredService<Aethra.Modules.Identity.Infrastructure.IdentitySeeder>();
        await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
    }

    public const string MonitorUrlBackfillConfigKey = "Aethra:MonitorUrlBackfillOnStart";
    public const bool MonitorUrlBackfillDefault = true;

    /// <summary>
    /// Indica si se debe ejecutar el backfill de URLs de monitores al arrancar según la configuración.
    /// Por defecto es true para producción gestionada; se desactiva explícitamente en tests sin BD.
    /// </summary>
    public static bool ShouldApplyMonitorUrlBackfill(IConfiguration configuration)
        => configuration.GetValue(MonitorUrlBackfillConfigKey, MonitorUrlBackfillDefault);

    /// <summary>
    /// Completa las claves normalizadas de monitores después de que el pipeline externo o
    /// este proceso haya aplicado la migración que las agrega.
    /// </summary>
    public static async Task ApplyMonitorUrlBackfillAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var step = services.GetService<IMonitorUrlBackfillStep>();
        if (step is not null)
        {
            await step.ExecuteAsync(services, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");
        var db = sp.GetRequiredService<MonitoringDbContext>();

        await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, logger, cancellationToken).ConfigureAwait(false);
    }
}
