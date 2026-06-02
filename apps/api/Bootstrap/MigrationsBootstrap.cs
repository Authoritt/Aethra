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

namespace Aethra.Api.Bootstrap;

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
                continue;
            }
            logger.LogInformation("{Context}: aplicando {Count} migraciones: {Migrations}",
                ctx.GetType().Name, pending.Count, string.Join(", ", pending));
            await ctx.Database.MigrateAsync().ConfigureAwait(false);
        }

        // F11.1: tras migrar, asegurar los 3 roles builtin + el user admin si la BD está vacía.
        // Idempotente — si ya existe el admin solo se confirman los roles builtin.
        var seeder = sp.GetRequiredService<Aethra.Modules.Identity.Infrastructure.IdentitySeeder>();
        await seeder.SeedAsync().ConfigureAwait(false);
    }
}
